using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Marketplace;
using AnchorLauncher.Services.Net;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Exports an instance to a Modrinth modpack (.mrpack). Mods recognised on Modrinth (by SHA-1)
/// become lightweight download references; everything else (unknown mods, config, resource/shader
/// packs) is bundled under overrides/. Personal data (saves, options.txt) is never included.
/// </summary>
public class ModpackExportService
{
    private readonly ModrinthClient _modrinth = new();

    // Content folders copied verbatim into the pack's overrides/ (config + packs).
    private static readonly string[] OverrideFolders = { "config", "resourcepacks", "shaderpacks" };

    public async Task ExportMrpackAsync(
        MinecraftInstance instance, string destPath,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), "anchor_mrpack_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var overrides = Path.Combine(temp, "overrides");

            var files   = new List<object>();
            var modsDir = Path.Combine(instance.GameDir, "mods");

            // 1) Mods: reference Modrinth files by hash; bundle the rest as overrides.
            if (Directory.Exists(modsDir))
            {
                var jars = Directory.EnumerateFiles(modsDir, "*.jar").ToList();
                for (int i = 0; i < jars.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var jar  = jars[i];
                    var name = Path.GetFileName(jar);
                    progress?.Report(DownloadProgress.At(85.0 * i / Math.Max(1, jars.Count), name));

                    var sha1 = await DownloadHelper.ComputeSha1Async(jar, ct);
                    var hit  = await _modrinth.GetFileByHashAsync(sha1, ct);

                    if (hit is { } h)
                        files.Add(new
                        {
                            path      = $"mods/{h.FileName}",
                            hashes    = new Dictionary<string, string> { ["sha1"] = h.Sha1, ["sha512"] = h.Sha512 },
                            env       = new Dictionary<string, string> { ["client"] = "required", ["server"] = "required" },
                            downloads = new[] { h.Url },
                            fileSize  = h.FileSize
                        });
                    else
                        CopyInto(jar, Path.Combine(overrides, "mods", name));
                }
            }

            // 2) Config + resource/shader packs → overrides verbatim.
            progress?.Report(DownloadProgress.At(90, "config"));
            foreach (var folder in OverrideFolders)
            {
                var src = Path.Combine(instance.GameDir, folder);
                if (!Directory.Exists(src)) continue;
                foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(instance.GameDir, file);
                    CopyInto(file, Path.Combine(overrides, rel));
                }
            }

            // 3) The modrinth.index.json manifest.
            var deps = new Dictionary<string, string> { ["minecraft"] = instance.Version };
            var loaderKey = instance.ModLoader switch
            {
                ModLoaderType.Fabric   => "fabric-loader",
                ModLoaderType.Forge    => "forge",
                ModLoaderType.NeoForge => "neoforge",
                ModLoaderType.Quilt    => "quilt-loader",
                _                      => null
            };
            if (loaderKey != null && !string.IsNullOrWhiteSpace(instance.ModLoaderVersion))
                deps[loaderKey] = instance.ModLoaderVersion!;

            var index = new
            {
                formatVersion = 1,
                game          = "minecraft",
                versionId     = "1.0.0",
                name          = instance.Name,
                files,
                dependencies  = deps
            };

            await File.WriteAllTextAsync(
                Path.Combine(temp, "modrinth.index.json"),
                JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }), ct);

            // 4) Zip the lot into the .mrpack.
            progress?.Report(DownloadProgress.At(96, Path.GetFileName(destPath)));
            if (File.Exists(destPath)) File.Delete(destPath);
            await Task.Run(() => ZipFile.CreateFromDirectory(
                temp, destPath, CompressionLevel.Optimal, includeBaseDirectory: false), ct);

            progress?.Report(DownloadProgress.At(100, Path.GetFileName(destPath)));
            Debug.WriteLine($"[Export] {instance.Name} → {destPath} ({files.Count} referenced mods)");
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        }
    }

    private static void CopyInto(string sourceFile, string destFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
        File.Copy(sourceFile, destFile, overwrite: true);
    }
}
