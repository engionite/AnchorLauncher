using System.Diagnostics;
using System.IO;
using AnchorLauncher.Models;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Sync;

/// <summary>
/// File-copy cloud sync: mirrors selected per-instance data (worlds, screenshots,
/// options.txt, config/) into the user's already-mounted OneDrive / Google Drive folder
/// under <c>AnchorLauncher\[instance]\</c>. Only newer files are copied. No OAuth involved.
/// </summary>
public class CloudSyncService
{
    /// <summary>Resolves the sync root for the configured provider, or null with a reason.</summary>
    public (string? Path, string? Error) ResolveRoot(GlobalSettings settings)
    {
        try
        {
            switch (settings.CloudProvider)
            {
                case CloudProvider.OneDrive:
                    var oneDrive = Environment.GetEnvironmentVariable("OneDrive")
                                ?? Environment.GetEnvironmentVariable("OneDriveConsumer");
                    return string.IsNullOrEmpty(oneDrive) || !Directory.Exists(oneDrive)
                        ? (null, "OneDrive folder not found on this PC — is OneDrive set up?")
                        : (Path.Combine(oneDrive, "AnchorLauncher"), null);

                case CloudProvider.GoogleDrive:
                    var candidates = new[]
                    {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "My Drive"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Google Drive"),
                        @"G:\My Drive"
                    };
                    var hit = candidates.FirstOrDefault(Directory.Exists);
                    return hit == null
                        ? (null, "Google Drive folder not found — use Custom path and browse to it.")
                        : (Path.Combine(hit, "AnchorLauncher"), null);

                default:
                    return string.IsNullOrWhiteSpace(settings.CloudCustomPath) || !Directory.Exists(settings.CloudCustomPath)
                        ? (null, "Custom sync path is not set or does not exist.")
                        : (Path.Combine(settings.CloudCustomPath, "AnchorLauncher"), null);
            }
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>Copies the selected targets for every instance. Returns a summary line.</summary>
    public async Task<string> SyncNowAsync(GlobalSettings settings, CancellationToken ct = default)
    {
        var (root, error) = ResolveRoot(settings);
        if (root == null) return error ?? "Sync root could not be resolved.";

        return await Task.Run(async () =>
        {
            int files = 0, instances = 0;
            var list = await new InstanceService().LoadAllAsync();

            foreach (var instance in list)
            {
                ct.ThrowIfCancellationRequested();
                var destRoot = Path.Combine(root, Sanitize(instance.Name));
                bool touched = false;

                if (settings.SyncWorlds)
                    touched |= CopyTree(Path.Combine(instance.GameDir, "saves"),
                                        Path.Combine(destRoot, "saves"), ref files, ct);
                if (settings.SyncScreenshots)
                    touched |= CopyTree(Path.Combine(instance.GameDir, "screenshots"),
                                        Path.Combine(destRoot, "screenshots"), ref files, ct);
                if (settings.SyncOptions)
                    touched |= CopyFile(Path.Combine(instance.GameDir, "options.txt"),
                                        Path.Combine(destRoot, "options.txt"), ref files);
                if (settings.SyncControls)
                    touched |= CopyTree(Path.Combine(instance.GameDir, "config"),
                                        Path.Combine(destRoot, "config"), ref files, ct);

                if (touched) instances++;
            }

            Debug.WriteLine($"[CloudSync] {files} files → {root}");
            return files == 0
                ? "Everything is already in sync."
                : $"Synced {files} file(s) across {instances} instance(s) → {root}";
        }, ct);
    }

    /// <summary>Copies only new/updated files (by last-write time).</summary>
    private static bool CopyTree(string sourceDir, string destDir, ref int counter, CancellationToken ct)
    {
        if (!Directory.Exists(sourceDir)) return false;
        bool touched = false;

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel  = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            if (CopyFile(file, dest, ref counter)) touched = true;
        }
        return touched;
    }

    private static bool CopyFile(string source, string dest, ref int counter)
    {
        try
        {
            if (!File.Exists(source)) return false;
            var srcInfo = new FileInfo(source);
            var dstInfo = new FileInfo(dest);
            if (dstInfo.Exists && dstInfo.LastWriteTimeUtc >= srcInfo.LastWriteTimeUtc) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
            counter++;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CloudSync] copy failed for {source}: {ex.Message}");
            return false;
        }
    }

    private static string Sanitize(string name)
        => new string(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray());
}
