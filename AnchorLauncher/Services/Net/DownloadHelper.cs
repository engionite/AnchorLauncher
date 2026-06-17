using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;

namespace AnchorLauncher.Services.Net;

/// <summary>
/// Shared HTTP download primitives with SHA-1 verification, resume-skip, and a Mojang-mirror
/// fallback for 404s. All file I/O is async and runs off the UI thread.
/// </summary>
public static class DownloadHelper
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    /// <summary>Optional sink for user-visible download diagnostics (wired to the console drawer).</summary>
    public static Action<string>? LogSink { get; set; }

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", "AnchorLauncher/1.0" } }
    };

    public static Task<string> GetStringAsync(string url, CancellationToken ct = default)
        => _http.GetStringAsync(url, ct);

    public static Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
        => _http.GetByteArrayAsync(url, ct);

    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destPath"/>.
    /// <list type="bullet">
    /// <item>A file matching <paramref name="expectedSha1"/> is reused as-is.</item>
    /// <item>When there is no checksum, an existing non-empty file is also reused — this is how
    /// loader-installer-produced artifacts (e.g. the Forge <c>forge-{ver}-client.jar</c>, which
    /// exists on no remote Maven) are picked up instead of being re-downloaded and 404-ing.</item>
    /// <item>A 404 falls back through the Forge / NeoForge / Maven-Central / Mojang mirrors.</item>
    /// </list>
    /// </summary>
    public static async Task DownloadFileAsync(
        string url, string destPath, string? expectedSha1 = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        if (File.Exists(destPath))
        {
            if (expectedSha1 != null)
            {
                if (string.Equals(await ComputeSha1Async(destPath, ct), expectedSha1, OIC))
                    return; // verified copy already present
            }
            else if (new FileInfo(destPath).Length > 0)
            {
                // No checksum to verify against → trust an existing non-empty file. This is the
                // root-cause fix for Forge/NeoForge: the installer produces the patched client jar
                // locally, and it must not be re-downloaded (it exists on no Maven server).
                return;
            }
        }

        var fileName = Path.GetFileName(destPath);

        try
        {
            await DownloadWithRetryAsync(url, destPath, ct);
            return;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            Log($"[Download] 404 for {fileName}: {url}");

            var mirrors = BuildMirrorUrls(url);
            if (mirrors.Count == 0)
                throw new InvalidOperationException(
                    $"Download failed for '{fileName}': the file was not found at {url} (404) and no mirror is available.");

            foreach (var mirror in mirrors)
            {
                Log($"[Download] Trying mirror: {mirror}");
                try
                {
                    await DownloadCoreAsync(mirror, destPath, ct);
                    Log($"[Download] Mirror succeeded for {fileName}.");
                    return;
                }
                catch (HttpRequestException me) when (me.StatusCode == HttpStatusCode.NotFound)
                {
                    // try the next mirror
                }
                catch (Exception me)
                {
                    Log($"[Download] Mirror error for {fileName}: {me.Message}");
                }
            }

            throw new InvalidOperationException(
                $"Download failed for '{fileName}': not found at {url} or any of {mirrors.Count} mirror(s). " +
                "If this is a Forge/NeoForge client jar, it is produced locally by the loader installer — " +
                "re-create the instance so the installer regenerates it.");
        }
    }

    /// <summary>Retries transient failures per NetworkConfig; 404 is re-thrown for the mirror fallback.</summary>
    private static async Task DownloadWithRetryAsync(string url, string destPath, CancellationToken ct)
    {
        int attempts = NetworkConfig.RetryDownloads ? Math.Max(1, NetworkConfig.RetryAttempts) : 1;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await DownloadCoreAsync(url, destPath, ct);
                return;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw; // let DownloadFileAsync apply the Mojang-mirror fallback
            }
            catch (Exception ex) when (attempt < attempts && !ct.IsCancellationRequested)
            {
                Log($"[Download] attempt {attempt}/{attempts} failed for {Path.GetFileName(destPath)}: {ex.Message} — retrying");
                try { await Task.Delay(400 * attempt, ct); } catch { throw; }
            }
        }
    }

    private static async Task DownloadCoreAsync(string url, string destPath, CancellationToken ct)
    {
        // Configurable timeout applies to the connect/response-header phase only, so large
        // bodies on slow links aren't killed mid-transfer.
        HttpResponseMessage resp;
        using (var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            headerCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, NetworkConfig.TimeoutSeconds)));
            resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
        }

        using (resp)
        {
            resp.EnsureSuccessStatusCode();

            var tmp = destPath + ".part";
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp))
            {
                await src.CopyToAsync(dst, ct);
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmp, destPath);
        }
    }

    /// <summary>
    /// Builds an ordered list of mirror URLs to try for a maven-style library that 404'd.
    /// Forge/NeoForge paths are tried on the loader mavens first; every jar also gets the
    /// general Forge → Maven-Central → Mojang fallbacks. Returns empty for non-maven URLs
    /// (e.g. asset objects). The original URL is excluded.
    /// </summary>
    private static List<string> BuildMirrorUrls(string url)
    {
        var mirrors = new List<string>();
        try
        {
            var uri = new Uri(url);

            // Reduce to the bare maven coordinate path: group/artifact/version/file.jar
            var path = uri.AbsolutePath.TrimStart('/');
            foreach (var prefix in new[] { "maven/", "releases/" })
                if (path.StartsWith(prefix, OIC)) path = path[prefix.Length..];

            if (!path.EndsWith(".jar", OIC)) return mirrors; // only maven jars have cross-mirror layout

            bool isForge = path.Contains("minecraftforge", OIC) || path.Contains("/forge/", OIC);
            bool isNeo   = path.Contains("neoforged", OIC) || path.Contains("/neoforge/", OIC);

            void Add(string baseUrl)
            {
                var full = baseUrl.TrimEnd('/') + "/" + path;
                if (!string.Equals(full, url, OIC) && !mirrors.Contains(full, StringComparer.OrdinalIgnoreCase))
                    mirrors.Add(full);
            }

            // Loader-specific mirrors first when the path looks like Forge/NeoForge
            if (isForge || isNeo)
            {
                Add("https://maven.minecraftforge.net");
                Add("https://maven.neoforged.net/releases");
                Add("https://files.minecraftforge.net/maven");
            }

            // General mirrors for ANY library that 404s
            Add("https://maven.minecraftforge.net");
            Add("https://repo1.maven.org/maven2");
            Add("https://libraries.minecraft.net");
        }
        catch
        {
            // malformed URL → no mirrors
        }
        return mirrors;
    }

    private static void Log(string message)
    {
        Debug.WriteLine(message);
        try { LogSink?.Invoke(message); } catch { }
    }

    public static async Task<string> ComputeSha1Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA1.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
