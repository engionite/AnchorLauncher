using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace AnchorSetup.Services;

public sealed record InstallOptions(
    string InstallDir,
    string LanguageAnchorValue,   // e.g. "Русский (ru-RU)" — written verbatim into settings.json
    bool   DesktopShortcut,
    bool   StartMenuShortcut,
    bool   RunAtStartup);

/// <summary>Progress tick: a 0–100 percentage plus a SetupLoc key for the status line.</summary>
public sealed record InstallProgress(double Percent, string StatusKey);

/// <summary>
/// Does the real installation work: lay down the launcher exe (downloaded from the GitHub
/// release, or copied from a local payload), pre-set Anchor's language, create shortcuts,
/// optionally run at startup, and register a working uninstaller in Add/Remove Programs.
/// Everything is per-user (%LOCALAPPDATA%) so no administrator elevation is needed.
/// </summary>
public static class InstallService
{
    public const string AppName     = "Anchor Launcher";
    public const string AppExeName  = "AnchorLauncher.exe";
    public const string AppVersion  = "1.0.0";
    public const string Publisher   = "Simplexity Development";
    public const string RegRunName  = "AnchorLauncher";
    public const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AnchorLauncher";

    /// <summary>Stable "latest release" asset URL — always points at the newest published build.</summary>
    public const string DownloadUrl =
        "https://github.com/engionite/AnchorLauncher/releases/latest/download/AnchorLauncher.exe";

    public static string DefaultInstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", AppName);

    public static string AnchorDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnchorLauncher");

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    // ── Install ──────────────────────────────────────────────────────────────────

    public static async Task InstallAsync(InstallOptions o, IProgress<InstallProgress> p, CancellationToken ct)
    {
        p.Report(new(2, "ins_preparing"));
        Directory.CreateDirectory(o.InstallDir);
        var exePath = Path.Combine(o.InstallDir, AppExeName);

        // 1) Lay down the launcher exe — local payload if present, otherwise download.
        var localPayload = FindLocalPayload();
        if (localPayload != null)
            await CopyWithProgressAsync(localPayload, exePath, 5, 85, p, ct);
        else
            await DownloadWithProgressAsync(DownloadUrl, exePath, 5, 85, p, ct);

        // 2) Pre-set Anchor's language so it boots localized (merge — never clobber existing settings).
        p.Report(new(87, "ins_language"));
        ApplyLanguage(o.LanguageAnchorValue);

        // 3) Shortcuts.
        p.Report(new(92, "ins_shortcuts"));
        if (o.StartMenuShortcut)
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"),
                exePath, o.InstallDir);
        if (o.DesktopShortcut)
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"),
                exePath, o.InstallDir);

        // 4) Optional run-at-startup.
        SetRunAtStartup(o.RunAtStartup, exePath);

        // 5) Register the uninstaller (Add/Remove Programs) + drop a self-copy as uninstall.exe.
        p.Report(new(97, "ins_registering"));
        RegisterUninstaller(o.InstallDir, exePath);

        p.Report(new(100, "step_finish"));
    }

    /// <summary>Looks for a bundled launcher next to setup.exe (offline installs &amp; testing).</summary>
    private static string? FindLocalPayload()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, AppExeName),
            Path.Combine(AppContext.BaseDirectory, "payload", AppExeName),
        };
        var env = Environment.GetEnvironmentVariable("ANCHOR_SETUP_PAYLOAD");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Insert(0, env);

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task CopyWithProgressAsync(
        string src, string dst, double from, double to, IProgress<InstallProgress> p, CancellationToken ct)
    {
        var total = new FileInfo(src).Length;
        await using var input  = File.OpenRead(src);
        await using var output = File.Create(dst);
        await PumpAsync(input, output, total, from, to, "ins_files", p, ct);
    }

    private static async Task DownloadWithProgressAsync(
        string url, string dst, double from, double to, IProgress<InstallProgress> p, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var input  = await resp.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(dst);
        await PumpAsync(input, output, total, from, to, "ins_downloading", p, ct);
    }

    private static async Task PumpAsync(
        Stream input, Stream output, long total, double from, double to,
        string statusKey, IProgress<InstallProgress> p, CancellationToken ct)
    {
        var buffer = new byte[1 << 20];   // 1 MiB
        long done = 0; int n; double last = -1;
        p.Report(new(from, statusKey));
        while ((n = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), ct);
            done += n;
            if (total > 0)
            {
                var pct = from + (to - from) * done / total;
                if (pct - last >= 0.5) { p.Report(new(pct, statusKey)); last = pct; }
            }
        }
        p.Report(new(to, statusKey));
    }

    // ── Language pre-set ─────────────────────────────────────────────────────────

    private static void ApplyLanguage(string anchorValue)
    {
        try
        {
            Directory.CreateDirectory(AnchorDataDir);
            var path = Path.Combine(AnchorDataDir, "settings.json");

            JsonObject root;
            if (File.Exists(path))
            {
                try { root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject(); }
                catch { root = new JsonObject(); }
            }
            else root = new JsonObject();

            root["Language"] = anchorValue;
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Debug.WriteLine($"[Setup] ApplyLanguage failed: {ex.Message}"); }
    }

    // ── Shortcuts (WScript.Shell COM — no extra dependency) ──────────────────────

    private static void CreateShortcut(string lnkPath, string target, string workingDir)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            var sc = shell.CreateShortcut(lnkPath);
            sc.TargetPath       = target;
            sc.WorkingDirectory = workingDir;
            sc.Description      = "Premium Minecraft launcher";
            sc.IconLocation     = target + ", 0";
            sc.Save();
            Marshal.FinalReleaseComObject(sc);
            Marshal.FinalReleaseComObject(shell);
        }
        catch (Exception ex) { Debug.WriteLine($"[Setup] CreateShortcut failed: {ex.Message}"); }
    }

    private static void SetRunAtStartup(bool enable, string exePath)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run == null) return;
            if (enable) run.SetValue(RegRunName, $"\"{exePath}\"");
            else        run.DeleteValue(RegRunName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Debug.WriteLine($"[Setup] SetRunAtStartup failed: {ex.Message}"); }
    }

    // ── Uninstaller registration ─────────────────────────────────────────────────

    private static void RegisterUninstaller(string installDir, string exePath)
    {
        try
        {
            // Drop a copy of this very installer as the uninstaller (it handles --uninstall).
            var uninstallExe = Path.Combine(installDir, "uninstall.exe");
            var self = Environment.ProcessPath;
            if (self != null && !self.Equals(uninstallExe, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, uninstallExe, overwrite: true);

            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
            key.SetValue("DisplayName",     AppName);
            key.SetValue("DisplayVersion",  AppVersion);
            key.SetValue("Publisher",       Publisher);
            key.SetValue("DisplayIcon",     exePath);
            key.SetValue("InstallLocation", installDir);
            key.SetValue("UninstallString", $"\"{uninstallExe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstallExe}\" --uninstall --silent");
            key.SetValue("URLInfoAbout",    "https://github.com/engionite/AnchorLauncher");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 170_000, RegistryValueKind.DWord);  // ~170 MB, in KB
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }
        catch (Exception ex) { Debug.WriteLine($"[Setup] RegisterUninstaller failed: {ex.Message}"); }
    }

    // ── Uninstall ────────────────────────────────────────────────────────────────

    public static void Uninstall(bool deleteData)
    {
        var installDir = string.Empty;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            installDir = key?.GetValue("InstallLocation") as string ?? string.Empty;
        }
        catch { /* fall through */ }
        if (string.IsNullOrEmpty(installDir))
            installDir = DefaultInstallDir;

        // Shortcuts
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"));

        // Registry: Run + Uninstall
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            run?.DeleteValue(RegRunName, throwOnMissingValue: false);
        }
        catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }

        // Optionally wipe user data (worlds, accounts, settings)
        if (deleteData)
            try { if (Directory.Exists(AnchorDataDir)) Directory.Delete(AnchorDataDir, recursive: true); } catch { }

        // Remove everything in the install dir except the running uninstaller, then have a
        // detached shell delete the leftover exe + now-empty folder once we exit.
        var self = Environment.ProcessPath ?? string.Empty;
        try
        {
            foreach (var f in Directory.EnumerateFiles(installDir))
                if (!f.Equals(self, StringComparison.OrdinalIgnoreCase)) TryDelete(f);
            foreach (var d in Directory.EnumerateDirectories(installDir))
                try { Directory.Delete(d, recursive: true); } catch { }
        }
        catch { }

        ScheduleSelfDelete(self, installDir);
    }

    private static void ScheduleSelfDelete(string self, string installDir)
    {
        try
        {
            // ping-based delay, then delete the uninstaller and prune the empty folder.
            var cmd = $"/c ping 127.0.0.1 -n 2 > nul & del /q \"{self}\" & rmdir \"{installDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd)
            {
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            });
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
