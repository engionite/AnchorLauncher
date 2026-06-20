using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using AnchorLauncher.Models.Marketplace;

namespace AnchorLauncher.Services.Marketplace;

/// <summary>CurseForge API v1 — search and download-URL resolution (gameId 432 = Minecraft).</summary>
public class CurseForgeClient
{
    private const string Base   = "https://api.curseforge.com/v1";
    private const int    GameId = 432;

    // The shipping CurseForge key is injected at build time from the ANCHOR_CF_KEY env var via
    // AssemblyMetadata (see AnchorLauncher.csproj) — it must NEVER be hardcoded (public repo).
    // A user can override it in Settings → Services (UserApiKey). With no key, CurseForge results
    // are simply skipped — Modrinth still works.
    private static readonly string _buildKey =
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "CurseForgeApiKey")?.Value
        ?? string.Empty;

    /// <summary>Optional user-supplied key (Settings → Services); overrides the build key when set.</summary>
    public static string? UserApiKey { get; set; }

    private static string EffectiveKey =>
        string.IsNullOrWhiteSpace(UserApiKey) ? _buildKey : UserApiKey!.Trim();

    public static bool HasApiKey => !string.IsNullOrWhiteSpace(EffectiveKey);

    private static readonly HttpClient _http = BuildClient();

    private static HttpClient BuildClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.Add("Accept", "application/json");
        c.DefaultRequestHeaders.Add("User-Agent", "AnchorLauncher/1.0");
        return c;
    }

    /// <summary>GET attaching the current effective key per request, so a key change takes effect immediately.</summary>
    private static async Task<string> GetWithKeyAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (HasApiKey) req.Headers.Add("x-api-key", EffectiveKey);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    // ── Search ──────────────────────────────────────────────────────────────────

    public async Task<(List<MarketplaceItem> Items, int Total)> SearchAsync(
        string query, ProjectType type, string? gameVersion, int loaderType,
        SortMode sort, int index, int pageSize, CancellationToken ct)
    {
        var items = new List<MarketplaceItem>();
        try
        {
            var (sortField, sortOrder) = SortParams(sort);
            var url = $"{Base}/mods/search?gameId={GameId}&classId={ClassId(type)}" +
                      $"&searchFilter={Uri.EscapeDataString(query)}" +
                      $"&sortField={sortField}&sortOrder={sortOrder}&index={index}&pageSize={pageSize}";
            if (!string.IsNullOrEmpty(gameVersion)) url += $"&gameVersion={Uri.EscapeDataString(gameVersion)}";
            if (loaderType > 0 && type == ProjectType.Mod) url += $"&modLoaderType={loaderType}";

            var body = await GetWithKeyAsync(url, ct);
            using var doc = JsonDocument.Parse(body);
            var root  = doc.RootElement;

            var total = root.TryGetProperty("pagination", out var pg) && pg.TryGetProperty("totalCount", out var tc)
                ? tc.GetInt32() : 0;

            foreach (var m in root.GetProperty("data").EnumerateArray())
            {
                items.Add(new MarketplaceItem
                {
                    Source      = ModSource.CurseForge,
                    Type        = type,
                    ProjectId   = m.GetProperty("id").GetInt64().ToString(),
                    Name        = Str(m, "name"),
                    Author      = FirstAuthor(m),
                    Description = Str(m, "summary"),
                    IconUrl     = m.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object
                                   ? (logo.TryGetProperty("url", out var lu) ? lu.GetString() : null) : null,
                    Downloads   = m.TryGetProperty("downloadCount", out var dc) ? (long)dc.GetDouble() : 0,
                    Versions    = SummarizeVersions(m),
                    DateCreated = ParseDate(m, "dateCreated"),
                    DateUpdated = ParseDate(m, "dateModified")
                });
            }

            // CurseForge caps total search results at 10,000
            return (items, Math.Min(total, 10_000));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CurseForge] search failed: {ex.Message}");
            return (items, 0);
        }
    }

    // ── Download resolution ─────────────────────────────────────────────────────

    public async Task<(string FileName, string Url)?> ResolveDownloadAsync(
        string modId, string? gameVersion, int loaderType, ProjectType type, CancellationToken ct)
    {
        var filtered = $"{Base}/mods/{modId}/files?pageSize=30";
        if (!string.IsNullOrEmpty(gameVersion)) filtered += $"&gameVersion={Uri.EscapeDataString(gameVersion)}";
        if (loaderType > 0 && type == ProjectType.Mod) filtered += $"&modLoaderType={loaderType}";

        var hit = await TryResolveAsync(filtered, ct);
        if (hit != null) return hit;

        // Fall back to the newest file regardless of version/loader
        return await TryResolveAsync($"{Base}/mods/{modId}/files?pageSize=30", ct);
    }

    private async Task<(string, string)?> TryResolveAsync(string url, CancellationToken ct)
    {
        try
        {
            var body = await GetWithKeyAsync(url, ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                return null;

            var file     = data[0]; // CurseForge returns newest first
            var fileName = file.GetProperty("fileName").GetString() ?? "mod.jar";
            var fileId   = file.GetProperty("id").GetInt64();

            var downloadUrl = file.TryGetProperty("downloadUrl", out var du) ? du.GetString() : null;
            // Author opted out of the download API (null downloadUrl) — reconstruct the
            // forgecdn edge path: /files/{fileId/1000}/{fileId%1000}/{filename}
            if (string.IsNullOrEmpty(downloadUrl))
            {
                downloadUrl = $"https://edge.forgecdn.net/files/{fileId / 1000}/{fileId % 1000}/{Uri.EscapeDataString(fileName)}";
                Debug.WriteLine($"[CurseForge] downloadUrl null for {fileName} — using edge fallback: {downloadUrl}");
            }

            return (fileName, downloadUrl);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CurseForge] resolve failed: {ex.Message}");
            return null;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>CurseForge ModsSearchSortField: 3=LastUpdated, 6=TotalDownloads, 11=ReleasedDate.</summary>
    private static (int Field, string Order) SortParams(SortMode sort) => sort switch
    {
        SortMode.MostDownloaded  => (6, "desc"),
        SortMode.LeastDownloaded => (6, "asc"),
        SortMode.RecentlyUpdated => (3, "desc"),
        SortMode.Oldest          => (11, "asc"),
        SortMode.NewestRelease   => (11, "desc"),
        _                        => (2, "desc")
    };

    private static DateTime ParseDate(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
           DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : DateTime.MinValue;

    private static int ClassId(ProjectType t) => t switch
    {
        ProjectType.Modpack      => 4471,
        ProjectType.ResourcePack => 12,
        ProjectType.Shader       => 6552,
        _                        => 6   // Mods
    };

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    private static string FirstAuthor(JsonElement m)
    {
        if (m.TryGetProperty("authors", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0)
            return a[0].TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        return string.Empty;
    }

    private static string SummarizeVersions(JsonElement m)
    {
        if (!m.TryGetProperty("latestFilesIndexes", out var idx) || idx.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var versions = idx.EnumerateArray()
            .Select(e => e.TryGetProperty("gameVersion", out var gv) ? gv.GetString() : null)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .ToList();

        if (versions.Count == 0) return string.Empty;
        return versions.Count <= 3 ? string.Join(", ", versions) : $"{string.Join(", ", versions.Take(3))} +{versions.Count - 3}";
    }
}
