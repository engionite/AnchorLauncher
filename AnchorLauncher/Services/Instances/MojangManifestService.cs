using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Fetches and caches Mojang's version manifest. Network result is written to disk so
/// the version picker still populates when offline.
/// </summary>
public class MojangManifestService
{
    private const string ManifestUrl =
        "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "AnchorLauncher/1.0" } }
    };

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private MojangVersionManifest? _cached;

    private static string CachePath =>
        Path.Combine(LauncherStorageService.AppDataRoot, "version_manifest.json");

    /// <summary>
    /// Returns the manifest, preferring a live fetch. Falls back to the on-disk cache
    /// when the network is unavailable. Result is memoized for the session.
    /// </summary>
    public async Task<MojangVersionManifest?> GetManifestAsync(
        bool forceRefresh = false, CancellationToken ct = default)
    {
        if (_cached != null && !forceRefresh) return _cached;

        // 1) Try the network
        try
        {
            var body = await _http.GetStringAsync(ManifestUrl, ct);
            _cached = JsonSerializer.Deserialize<MojangVersionManifest>(body, _json);

            try { await File.WriteAllTextAsync(CachePath, body, ct); }
            catch (Exception ex) { Debug.WriteLine($"[Manifest] cache write failed: {ex.Message}"); }

            Debug.WriteLine($"[Manifest] Fetched {_cached?.Versions.Count ?? 0} versions (live).");
            return _cached;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Manifest] Live fetch failed ({ex.Message}); trying cache.");
        }

        // 2) Fall back to cache
        try
        {
            if (File.Exists(CachePath))
            {
                var body = await File.ReadAllTextAsync(CachePath, ct);
                _cached = JsonSerializer.Deserialize<MojangVersionManifest>(body, _json);
                Debug.WriteLine($"[Manifest] Loaded {_cached?.Versions.Count ?? 0} versions (cache).");
                return _cached;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Manifest] Cache read failed: {ex.Message}");
        }

        return null;
    }
}
