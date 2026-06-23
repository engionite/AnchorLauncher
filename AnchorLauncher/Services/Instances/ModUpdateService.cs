using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Marketplace;
using AnchorLauncher.Services.Net;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Checks an instance's installed mods against Modrinth (by SHA-1) and updates the ones with a
/// newer compatible build. CurseForge-only or local mods are silently skipped — they simply
/// aren't recognised by the hash lookup.
/// </summary>
public class ModUpdateService
{
    private readonly ModrinthClient        _modrinth = new();
    private readonly InstanceContentService _content = new();

    /// <summary>One updatable mod: the local entry plus the newer file to fetch.</summary>
    public record ModUpdate(ModEntry Mod, string NewFileName, string Url, string NewSha1);

    /// <summary>Hashes every enabled jar and asks Modrinth for a newer build, reporting progress as
    /// it scans. Mods not on Modrinth, or already current, are skipped.</summary>
    public async Task<List<ModUpdate>> CheckAsync(
        MinecraftInstance instance, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var updates = new List<ModUpdate>();
        var mods    = _content.ListMods(instance.GameDir).Where(m => m.IsEnabled).ToList();
        var loader  = LoaderName(instance.ModLoader);

        for (int i = 0; i < mods.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var mod = mods[i];
            progress?.Report(DownloadProgress.At(100.0 * i / Math.Max(1, mods.Count), mod.FileName));

            var sha1 = await Sha1Async(mod.FullPath, ct);
            if (sha1 == null) continue;

            var latest = await _modrinth.GetUpdateByHashAsync(sha1, instance.Version, loader, ct);
            if (latest != null && !string.Equals(latest.Value.Sha1, sha1, StringComparison.OrdinalIgnoreCase))
                updates.Add(new ModUpdate(mod, latest.Value.FileName, latest.Value.Url, latest.Value.Sha1));
        }

        progress?.Report(DownloadProgress.At(100, string.Empty));
        return updates;
    }

    /// <summary>Downloads each chosen update into mods/ and removes the superseded jar.</summary>
    public async Task ApplyAsync(
        MinecraftInstance instance, IReadOnlyList<ModUpdate> updates,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var modsDir = Path.Combine(instance.GameDir, "mods");
        Directory.CreateDirectory(modsDir);

        for (int i = 0; i < updates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var u = updates[i];
            progress?.Report(DownloadProgress.At(100.0 * i / Math.Max(1, updates.Count), u.NewFileName));

            var dest = Path.Combine(modsDir, u.NewFileName);
            await DownloadHelper.DownloadFileAsync(u.Url, dest, null, ct);

            // Remove the old jar when the filename changed (same name = already overwritten).
            var old = u.Mod.FullPath;
            if (!string.Equals(Path.GetFileName(old), u.NewFileName, StringComparison.OrdinalIgnoreCase)
                && File.Exists(old))
                try { File.Delete(old); } catch (Exception ex) { Debug.WriteLine($"[ModUpdate] old delete: {ex.Message}"); }
        }

        progress?.Report(DownloadProgress.At(100, string.Empty));
    }

    private static async Task<string?> Sha1Async(string path, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(path);
            using var sha = SHA1.Create();
            var hash = await sha.ComputeHashAsync(fs, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex) { Debug.WriteLine($"[ModUpdate] sha1 '{path}': {ex.Message}"); return null; }
    }

    private static string? LoaderName(ModLoaderType loader) => loader switch
    {
        ModLoaderType.Fabric   => "fabric",
        ModLoaderType.Forge    => "forge",
        ModLoaderType.NeoForge => "neoforge",
        ModLoaderType.Quilt    => "quilt",
        _                      => null
    };
}
