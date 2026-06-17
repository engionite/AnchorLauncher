using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Net;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Imports a Modrinth modpack (<c>.mrpack</c>) into a new instance: reads <c>modrinth.index.json</c>,
/// creates the instance at the pack's Minecraft version + loader, installs that loader, copies the
/// <c>overrides/</c>, and downloads every listed file. Download URLs are restricted to the domains the
/// Modrinth modpack spec allows (cdn.modrinth.com / GitHub / GitLab) — a malicious pack cannot pull
/// arbitrary executables from elsewhere.
/// </summary>
public class ModpackImportService
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private readonly InstanceService  _instances = new();
    private readonly ModLoaderService _loaders   = new();

    public bool IsMrpack(string path) =>
        path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) ||
        (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && ZipHasEntry(path, "modrinth.index.json"));

    public async Task<MinecraftInstance> ImportMrpackAsync(
        string mrpackPath, string? overrideName,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var loc = Services.Platform.Loc.I;
        progress?.Report(DownloadProgress.At(0, loc["imp_reading"]));

        using var zip = ZipFile.OpenRead(mrpackPath);
        var indexEntry = zip.GetEntry("modrinth.index.json")
            ?? throw new InvalidOperationException("Not a Modrinth modpack (.mrpack): missing modrinth.index.json.");

        ModrinthIndex index;
        using (var s = indexEntry.Open())
            index = await JsonSerializer.DeserializeAsync<ModrinthIndex>(s, _json, ct)
                    ?? throw new InvalidOperationException("Malformed modrinth.index.json.");

        var deps = index.Dependencies ?? new();
        if (!deps.TryGetValue("minecraft", out var mcVersion) || string.IsNullOrWhiteSpace(mcVersion))
            throw new InvalidOperationException("Modpack does not specify a Minecraft version.");

        var (loaderType, loaderVer) = ResolveLoader(deps);

        var name = string.IsNullOrWhiteSpace(overrideName)
            ? (string.IsNullOrWhiteSpace(index.Name) ? "Imported Pack" : index.Name!)
            : overrideName!;

        var inst = await _instances.CreateAsync(name, mcVersion, "release", loaderType, loaderVer);

        // Install the loader so the instance is launchable (best-effort — files still import on failure)
        if (loaderType != ModLoaderType.Vanilla)
        {
            progress?.Report(DownloadProgress.At(8, $"{loc["imp_loader"]} {loaderType} {loaderVer}…"));
            try { await _loaders.InstallAsync(inst, progress, ct); }
            catch (Exception ex) { Debug.WriteLine($"[Import] loader install failed: {ex.Message}"); }
        }

        // Copy bundled configs/resources from overrides/ (and client-overrides/)
        progress?.Report(DownloadProgress.At(14, loc["imp_configs"]));
        ExtractOverrides(zip, "overrides", inst.GameDir);
        ExtractOverrides(zip, "client-overrides", inst.GameDir);

        // Download the pack's files (mods, etc.) to their declared paths
        var files = index.Files ?? new();
        int total = files.Count, done = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            done++;

            // Skip files the pack marks as client-unsupported
            if (f.Env != null && f.Env.TryGetValue("client", out var c) &&
                c.Equals("unsupported", StringComparison.OrdinalIgnoreCase))
                continue;

            var url = f.Downloads?.FirstOrDefault(IsAllowedDownload);
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(f.Path) || IsUnsafePath(f.Path))
            {
                Debug.WriteLine($"[Import] skipped file (no allowed URL / unsafe path): {f.Path}");
                continue;
            }

            var dest = Path.Combine(inst.GameDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            var pct = 18 + (int)(78.0 * done / Math.Max(1, total));
            progress?.Report(DownloadProgress.At(pct, $"{loc["imp_downloading"]} {Path.GetFileName(f.Path)} ({done}/{total})"));

            var sha1 = f.Hashes != null && f.Hashes.TryGetValue("sha1", out var h) ? h : null;
            await DownloadHelper.DownloadFileAsync(url!, dest, sha1, ct);
        }

        await _instances.SaveAsync(inst);
        progress?.Report(DownloadProgress.At(100, loc["imp_done"]));
        Debug.WriteLine($"[Import] '{inst.Name}' imported ({total} files, {loaderType} {loaderVer}).");
        return inst;
    }

    private static (ModLoaderType, string?) ResolveLoader(Dictionary<string, string> deps)
    {
        if (deps.TryGetValue("fabric-loader", out var fl)) return (ModLoaderType.Fabric, fl);
        if (deps.TryGetValue("quilt-loader",  out var ql)) return (ModLoaderType.Quilt,  ql);
        if (deps.TryGetValue("neoforge",      out var nf)) return (ModLoaderType.NeoForge, nf);
        if (deps.TryGetValue("forge",         out var fg)) return (ModLoaderType.Forge,  fg);
        return (ModLoaderType.Vanilla, null);
    }

    private static void ExtractOverrides(ZipArchive zip, string prefix, string gameDir)
    {
        foreach (var e in zip.Entries)
        {
            if (!e.FullName.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(e.Name)) continue;   // directory marker

            var rel = e.FullName.Substring(prefix.Length + 1);
            if (IsUnsafePath(rel)) continue;              // zip-slip guard

            var dest = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try { e.ExtractToFile(dest, overwrite: true); }
            catch (Exception ex) { Debug.WriteLine($"[Import] override extract failed ({rel}): {ex.Message}"); }
        }
    }

    /// <summary>Modrinth spec: downloads must come from these domains.</summary>
    private static bool IsAllowedDownload(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
        var host = u.Host.ToLowerInvariant();
        return host == "cdn.modrinth.com"
            || host.EndsWith(".modrinth.com")
            || host == "github.com" || host.EndsWith(".github.com")
            || host.EndsWith(".githubusercontent.com")
            || host == "gitlab.com" || host.EndsWith(".gitlab.com");
    }

    private static bool IsUnsafePath(string rel) =>
        rel.Contains("..") || Path.IsPathRooted(rel) || rel.Contains(':');

    private static bool ZipHasEntry(string zipPath, string entry)
    {
        try { using var z = ZipFile.OpenRead(zipPath); return z.GetEntry(entry) != null; }
        catch { return false; }
    }

    // ── modrinth.index.json shape (only the fields we use) ──────────────────────
    private sealed class ModrinthIndex
    {
        [JsonPropertyName("name")]         public string? Name { get; set; }
        [JsonPropertyName("versionId")]    public string? VersionId { get; set; }
        [JsonPropertyName("dependencies")] public Dictionary<string, string>? Dependencies { get; set; }
        [JsonPropertyName("files")]        public List<ModrinthFile>? Files { get; set; }
    }

    private sealed class ModrinthFile
    {
        [JsonPropertyName("path")]      public string? Path { get; set; }
        [JsonPropertyName("hashes")]    public Dictionary<string, string>? Hashes { get; set; }
        [JsonPropertyName("downloads")] public List<string>? Downloads { get; set; }
        [JsonPropertyName("env")]       public Dictionary<string, string>? Env { get; set; }
    }
}
