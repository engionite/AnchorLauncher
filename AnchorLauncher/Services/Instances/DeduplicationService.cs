using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Net;
using AnchorLauncher.Services.Storage;
using Microsoft.Win32.SafeHandles;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Reclaims disk space by replacing byte-identical content shared across instances (mods, resource
/// packs, shader packs) with NTFS hard links to a single cached copy. Hard links need no admin
/// rights and work within one volume. Only immutable downloads are touched — never config, saves or
/// options, which are edited per-instance and must stay independent.
/// </summary>
public class DeduplicationService
{
    private static readonly string[] ContentFolders = { "mods", "resourcepacks", "shaderpacks" };
    private static string CacheRoot => Path.Combine(LauncherStorageService.AppDataRoot, "contentcache");

    public record DedupReport(int Files, long Bytes);

    /// <summary>Counts how much could be reclaimed, without changing anything on disk.</summary>
    public async Task<DedupReport> ScanAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var groups = await BuildGroupsAsync(progress, ct);
        long bytes = 0; int files = 0;
        foreach (var g in groups.Values)
        {
            if (g.Count < 2) continue;
            var size = SafeLen(g[0]);
            bytes += size * (g.Count - 1);
            files += g.Count - 1;
        }
        return new DedupReport(files, bytes);
    }

    /// <summary>Hard-links duplicate files to one cached copy and reports what was reclaimed.</summary>
    public async Task<DedupReport> ReclaimAsync(IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheRoot);
        var groups = (await BuildGroupsAsync(progress, ct)).Where(kv => kv.Value.Count >= 2).ToList();

        long saved = 0; int linked = 0;
        for (int gi = 0; gi < groups.Count; gi++)
        {
            ct.ThrowIfCancellationRequested();
            var hash  = groups[gi].Key;
            var files = groups[gi].Value;
            var size  = SafeLen(files[0]);
            var cache = Path.Combine(CacheRoot, hash + Path.GetExtension(files[0]));
            progress?.Report(DownloadProgress.At(100.0 * gi / Math.Max(1, groups.Count), Path.GetFileName(files[0])));

            // Ensure a verified cached master exists before deleting anything.
            try
            {
                if (!File.Exists(cache) || SafeLen(cache) != size)
                    File.Copy(files[0], cache, overwrite: true);
            }
            catch (Exception ex) { Debug.WriteLine($"[Dedup] cache copy failed: {ex.Message}"); continue; }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (HardLinkCount(file) > 1) continue;   // already shared → leave it alone
                try
                {
                    File.Delete(file);
                    if (CreateHardLink(file, cache, IntPtr.Zero)) { linked++; saved += size; }
                    else File.Copy(cache, file, overwrite: true);   // link failed → restore a real copy
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Dedup] link '{file}' failed: {ex.Message}");
                    try { if (!File.Exists(file)) File.Copy(cache, file, overwrite: true); } catch { }
                }
            }
        }
        progress?.Report(DownloadProgress.At(100, string.Empty));
        return new DedupReport(linked, saved);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, List<string>>> BuildGroupsAsync(
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var instances = await new InstanceService().LoadAllAsync();

        var candidates = new List<string>();
        foreach (var inst in instances)
            foreach (var folder in ContentFolders)
            {
                var dir = Path.Combine(inst.GameDir, folder);
                if (!Directory.Exists(dir)) continue;
                candidates.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)));
            }

        // Hash only files that share a byte-size with another (cheap pre-filter avoids hashing uniques).
        var bySize = candidates.GroupBy(SafeLen).Where(g => g.Key > 0 && g.Count() >= 2).ToList();
        var total  = bySize.Sum(g => g.Count());
        var groups = new Dictionary<string, List<string>>();
        int done = 0;

        foreach (var sizeGroup in bySize)
            foreach (var file in sizeGroup)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(DownloadProgress.At(100.0 * done++ / Math.Max(1, total), Path.GetFileName(file)));
                var hash = await DownloadHelper.ComputeSha1Async(file, ct);
                if (!groups.TryGetValue(hash, out var list)) groups[hash] = list = new List<string>();
                list.Add(file);
            }
        return groups;
    }

    private static long SafeLen(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }

    // ── Win32 ─────────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION info);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    /// <summary>NTFS hard-link count for a file (1 = unique copy). Returns 1 on any error.</summary>
    private static uint HardLinkCount(string path)
    {
        try
        {
            const uint FILE_READ_ATTRIBUTES = 0x80, FILE_SHARE_ALL = 0x07, OPEN_EXISTING = 3, FLAG_BACKUP_SEMANTICS = 0x02000000;
            using var h = CreateFile(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (h.IsInvalid) return 1;
            return GetFileInformationByHandle(h, out var info) ? info.NumberOfLinks : 1;
        }
        catch { return 1; }
    }
}
