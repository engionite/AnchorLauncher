using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace AnchorLauncher.Services.Platform;

/// <summary>
/// In-app updater for the single-file build. Downloads the latest AnchorLauncher.exe and swaps it
/// in via rename — Windows allows <i>renaming</i> a running executable (just not overwriting/deleting
/// it), so we move the running exe aside, drop the new one in its place, relaunch and exit. The stale
/// copy is deleted on the next startup. No GitHub trip, no reinstall.
/// </summary>
public static class SelfUpdateService
{
    public const string DownloadUrl =
        "https://github.com/engionite/AnchorLauncher/releases/latest/download/AnchorLauncher.exe";

    private const string OldName = "AnchorLauncher.old.exe";
    private const string NewName = "AnchorLauncher.new.exe";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(20) };

    /// <summary>True when this is an installed .exe sitting in a folder we can write to (so the swap can work).</summary>
    public static bool CanSelfUpdate()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return false;
            var dir = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(dir)) return false;
            var probe = Path.Combine(dir, ".anchor_write_test");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Removes the leftover *.old.exe from a previous self-update. Call once at startup.</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var old = Path.Combine(Path.GetDirectoryName(exe)!, OldName);
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception ex) { Debug.WriteLine($"[SelfUpdate] cleanup skipped: {ex.Message}"); }
    }

    /// <summary>
    /// Downloads the latest build, swaps it in, and launches it. On success the new process is
    /// already starting — the caller should shut the app down immediately. Throws on any failure
    /// (caller then falls back to opening the download page).
    /// </summary>
    public static async Task DownloadAndApplyAsync(IProgress<double> progress, CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path.");
        var dir = Path.GetDirectoryName(exe)!;
        var newExe = Path.Combine(dir, NewName);
        var oldExe = Path.Combine(dir, OldName);

        // 1. Download to a side file with progress.
        if (File.Exists(newExe)) File.Delete(newExe);
        using (var resp = await _http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(newExe);
            var buffer = new byte[1 << 20];
            long done = 0; int n; double last = -1;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0)
                {
                    var pct = 100.0 * done / total;
                    if (pct - last >= 0.5) { progress.Report(pct); last = pct; }
                }
            }
        }

        // 2. Sanity-check it's a real, non-trivial Windows executable before swapping.
        if (new FileInfo(newExe).Length < 1_000_000 || !LooksLikeExe(newExe))
        {
            TryDelete(newExe);
            throw new InvalidOperationException("The downloaded update looks invalid.");
        }
        progress.Report(100);

        // 3. Swap by rename (a running exe can be renamed, not overwritten).
        if (File.Exists(oldExe)) TryDelete(oldExe);
        File.Move(exe, oldExe);
        try
        {
            File.Move(newExe, exe);
        }
        catch
        {
            try { File.Move(oldExe, exe); } catch { /* best-effort revert */ }
            throw;
        }

        // 4. Launch the freshly-installed build. Caller shuts this instance down next.
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = dir });
    }

    private static bool LooksLikeExe(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
