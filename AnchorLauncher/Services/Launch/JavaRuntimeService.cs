using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Net;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Launch;

/// <summary>
/// Resolves a <c>java.exe</c> matching a required major version (8 / 17 / 21). Probes
/// Anchor-managed runtimes, JAVA_HOME, PATH and common install roots; if none match it
/// downloads a headless Temurin JRE from Adoptium into the launcher's java folder.
/// </summary>
public class JavaRuntimeService
{
    /// <summary>
    /// Returns the path to a java.exe whose major version equals <paramref name="majorVersion"/>,
    /// downloading one if necessary. Returns null only if acquisition fails entirely.
    /// </summary>
    public async Task<string?> ResolveJavaAsync(
        int majorVersion, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        // 1) Anchor-managed runtime from a previous download
        var managed = ManagedJavaPath(majorVersion);
        if (managed != null && JavaMajorOf(managed) == majorVersion)
            return managed;

        // 2) Probe the system for an already-installed matching JDK/JRE
        foreach (var candidate in EnumerateCandidateJavaPaths())
        {
            ct.ThrowIfCancellationRequested();
            if (JavaMajorOf(candidate) == majorVersion)
            {
                Debug.WriteLine($"[Java] Using system runtime: {candidate}");
                return candidate;
            }
        }

        // 3) Download a headless Temurin JRE from Adoptium
        Debug.WriteLine($"[Java] No Java {majorVersion} found — fetching from Adoptium.");
        return await DownloadTemurinAsync(majorVersion, progress, ct);
    }

    /// <summary>Detected Java runtime: full path to java.exe + its major version.</summary>
    public record JavaInstall(string Path, int Major);

    /// <summary>Enumerates installed Java runtimes (system + Anchor-managed) with their major versions.</summary>
    public Task<List<JavaInstall>> ListDetectedJavasAsync() => Task.Run(() =>
    {
        var found = new List<JavaInstall>();

        foreach (var major in new[] { 8, 17, 21, 16 })
        {
            var managed = ManagedJavaPath(major);
            if (managed != null && JavaMajorOf(managed) == major)
                found.Add(new JavaInstall(managed, major));
        }

        foreach (var path in EnumerateCandidateJavaPaths())
        {
            var major = JavaMajorOf(path);
            if (major > 0) found.Add(new JavaInstall(path, major));
        }

        return found
            .GroupBy(j => j.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(j => j.Major)
            .ToList();
    });

    // ── Managed runtimes ────────────────────────────────────────────────────────

    private static string? ManagedJavaPath(int major)
    {
        var root = Path.Combine(LauncherStorageService.JavaRoot, $"temurin-{major}");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    private async Task<string?> DownloadTemurinAsync(
        int major, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        try
        {
            progress?.Report(DownloadProgress.At(5, $"Downloading Java {major} runtime…"));

            // Adoptium redirects this to the concrete archive asset
            var url = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/jre/hotspot/normal/eclipse";

            var targetRoot = Path.Combine(LauncherStorageService.JavaRoot, $"temurin-{major}");
            Directory.CreateDirectory(targetRoot);
            var zipPath = Path.Combine(targetRoot, "jre.zip");

            await DownloadHelper.DownloadFileAsync(url, zipPath, null, ct);

            progress?.Report(DownloadProgress.At(70, $"Extracting Java {major} runtime…"));
            await Task.Run(() =>
            {
                foreach (var entry in Directory.EnumerateDirectories(targetRoot))
                    Directory.Delete(entry, true); // clear any prior partial extract
                ZipFile.ExtractToDirectory(zipPath, targetRoot, overwriteFiles: true);
            }, ct);

            try { File.Delete(zipPath); } catch { }

            var java = Directory.EnumerateFiles(targetRoot, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
            progress?.Report(DownloadProgress.At(100, java != null
                ? $"Java {major} ready."
                : $"Java {major} extraction incomplete."));

            Debug.WriteLine($"[Java] Temurin {major} resolved: {java}");
            return java;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Java] DownloadTemurinAsync({major}) failed: {ex.Message}");
            return null;
        }
    }

    // ── System probing ──────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateCandidateJavaPaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? Add(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return seen.Add(path) ? path : null;
        }

        // JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var p = Add(Path.Combine(javaHome, "bin", "java.exe"));
            if (p != null) yield return p;
        }

        // PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = Path.Combine(dir.Trim(), "java.exe"); } catch { continue; }
            var p = Add(candidate);
            if (p != null) yield return p;
        }

        // Common vendor install roots
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        var vendors = new[] { "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "Zulu", "AdoptOpenJDK", "Amazon Corretto" };

        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
        foreach (var vendor in vendors)
        {
            var vendorDir = Path.Combine(root, vendor);
            if (!Directory.Exists(vendorDir)) continue;

            IEnumerable<string> javas;
            try { javas = Directory.EnumerateFiles(vendorDir, "java.exe", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var java in javas)
            {
                var p = Add(java);
                if (p != null) yield return p;
            }
        }
    }

    /// <summary>Runs <c>java -version</c> and parses the major version (8, 17, 21…), or -1.</summary>
    private static int JavaMajorOf(string javaExe)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return -1;

            // `java -version` prints to stderr
            var output = proc.StandardError.ReadToEnd() + proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            return ParseMajor(output);
        }
        catch
        {
            return -1;
        }
    }

    private static int ParseMajor(string versionOutput)
    {
        // Examples: version "1.8.0_392"  → 8 ;  version "17.0.10"  → 17
        var match = System.Text.RegularExpressions.Regex.Match(versionOutput, "version \"([0-9]+)(?:\\.([0-9]+))?");
        if (!match.Success) return -1;

        var first = int.Parse(match.Groups[1].Value);
        if (first == 1 && match.Groups[2].Success)
            return int.Parse(match.Groups[2].Value); // 1.8 → 8
        return first;
    }
}
