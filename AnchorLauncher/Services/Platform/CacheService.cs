using System.Diagnostics;
using System.IO;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Platform;

/// <summary>Clears regenerable cache (assets, manifest, stray .part files). Keeps instances, accounts, settings, java.</summary>
public static class CacheService
{
    public static Task<long> ClearAsync() => Task.Run(() =>
    {
        long freed = 0;
        try
        {
            var root = LauncherStorageService.AppDataRoot;

            freed += DeleteDir(Path.Combine(root, "assets"));

            var manifest = Path.Combine(root, "version_manifest.json");
            if (File.Exists(manifest)) { freed += SizeOf(manifest); File.Delete(manifest); }

            // Stray partial downloads anywhere under the data root
            foreach (var part in Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories))
            {
                try { freed += SizeOf(part); File.Delete(part); } catch { }
            }

            Debug.WriteLine($"[Cache] cleared {freed} bytes.");
        }
        catch (Exception ex) { Debug.WriteLine($"[Cache] ClearAsync failed: {ex.Message}"); }
        return freed;
    });

    public static string FormatBytes(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:0.0} GB" :
        bytes >= 1_048_576     ? $"{bytes / 1_048_576.0:0.0} MB" :
        bytes >= 1024          ? $"{bytes / 1024.0:0.0} KB" : $"{bytes} B";

    private static long SizeOf(string file) { try { return new FileInfo(file).Length; } catch { return 0; } }

    private static long DeleteDir(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long size = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            size += SizeOf(f);
        try { Directory.Delete(dir, recursive: true); } catch { }
        return size;
    }
}
