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

    /// <summary>
    /// Finishes a self-update at startup: closes the previous version that launched us (its PID is
    /// passed via <c>--replaced-pid</c>) and removes the leftover *.old.exe. No-op on a normal launch.
    /// </summary>
    public static void FinishPendingUpdate(string[] args)
    {
        try
        {
            // Close the exact predecessor process by the PID it handed us. Reliable regardless of the
            // old build's exit behaviour or how Windows reports its (renamed) image name.
            var idx = Array.FindIndex(args, a => a.Equals("--replaced-pid", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < args.Length && int.TryParse(args[idx + 1], out var pid))
            {
                try { var old = Process.GetProcessById(pid); old.Kill(); old.WaitForExit(3000); }
                catch { /* already exited */ }
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var dir = Path.GetDirectoryName(exe)!;
            // Remove any leftover predecessor(s): the canonical *.old.exe and any unique-named fallbacks.
            foreach (var stale in Directory.EnumerateFiles(dir, "AnchorLauncher.old*.exe"))
                try { File.Delete(stale); } catch { /* still exiting; cleaned on a later launch */ }
        }
        catch (Exception ex) { Debug.WriteLine($"[SelfUpdate] FinishPendingUpdate skipped: {ex.Message}"); }
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

        // 1. Download to a side file with progress (retry once on a transient network failure).
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(newExe)) File.Delete(newExe);
                using var resp = await _http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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
                break;   // downloaded OK
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < 2)
            {
                Debug.WriteLine($"[SelfUpdate] download attempt {attempt} failed ({ex.Message}); retrying");
                TryDelete(newExe);
                await Task.Delay(1500, ct);
            }
        }

        // 2. Sanity-check it's a real, non-trivial Windows executable before swapping.
        if (new FileInfo(newExe).Length < 1_000_000 || !LooksLikeExe(newExe))
        {
            TryDelete(newExe);
            throw new InvalidOperationException("The downloaded update looks invalid.");
        }
        progress.Report(100);

        // 3. Swap by rename (a running exe can be renamed, not overwritten). Each move is retried
        //    with backoff: a freshly-written unsigned exe is routinely locked for a few seconds by
        //    antivirus real-time scanning, which is what makes an immediate single-shot move fail.
        if (File.Exists(oldExe)) TryDelete(oldExe);
        if (File.Exists(oldExe))            // still locked → step the running exe aside under a unique name
            oldExe = Path.Combine(dir, $"AnchorLauncher.old.{Environment.ProcessId}.exe");

        await MoveWithRetryAsync(exe, oldExe, ct);
        try
        {
            await MoveWithRetryAsync(newExe, exe, ct);
        }
        catch
        {
            try { File.Move(oldExe, exe); } catch { /* best-effort revert */ }
            throw;
        }

        // 4. Launch the freshly-installed build, handing it our PID so it can close us — this
        //    guarantees a single launcher even if our own exit is delayed. Caller also exits.
        Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = dir,
            Arguments = $"--replaced-pid {Environment.ProcessId}"
        });
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

    /// <summary>File.Move with backoff — tolerates the transient lock antivirus puts on a freshly
    /// written executable while it scans it (the usual cause of a failed in-place swap).</summary>
    private static async Task MoveWithRetryAsync(string from, string to, CancellationToken ct, int attempts = 8)
    {
        for (int i = 0; ; i++)
        {
            try { File.Move(from, to); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && i < attempts)
            {
                await Task.Delay(750, ct);
            }
        }
    }

    /// <summary>Persists why a self-update failed (the dialog otherwise just swaps to the web
    /// fallback). Writes to %AppData%\AnchorLauncher\logs\update.log so it can be diagnosed.</summary>
    public static void LogFailure(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnchorLauncher", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "update.log"),
                $"[{DateTime.Now:O}] self-update failed:\n{ex}\n\n");
        }
        catch { /* logging must never throw */ }
    }
}
