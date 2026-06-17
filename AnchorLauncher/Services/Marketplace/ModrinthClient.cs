using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using AnchorLauncher.Models.Marketplace;

namespace AnchorLauncher.Services.Marketplace;

/// <summary>Modrinth API v2 — search and download-URL resolution.</summary>
public class ModrinthClient
{
    private const string Base = "https://api.modrinth.com/v2";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "AnchorLauncher/1.0 (anchor-launcher)" } }
    };

    // ── Search ──────────────────────────────────────────────────────────────────

    public async Task<(List<MarketplaceItem> Items, int Total)> SearchAsync(
        string query, ProjectType type, string? gameVersion, string? loader,
        SortMode sort, int offset, int limit, CancellationToken ct)
    {
        var items = new List<MarketplaceItem>();
        try
        {
            var facets = new List<string> { $"[\"project_type:{FacetType(type)}\"]" };
            if (!string.IsNullOrEmpty(gameVersion)) facets.Add($"[\"versions:{gameVersion}\"]");
            if (!string.IsNullOrEmpty(loader) && type == ProjectType.Mod) facets.Add($"[\"categories:{loader}\"]");

            var facetParam = "[" + string.Join(",", facets) + "]";
            var url = $"{Base}/search?query={Uri.EscapeDataString(query)}" +
                      $"&facets={Uri.EscapeDataString(facetParam)}" +
                      $"&offset={offset}&limit={limit}&index={SortIndex(sort)}";

            var body = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(body);
            var root  = doc.RootElement;
            var total = root.TryGetProperty("total_hits", out var th) ? th.GetInt32() : 0;

            foreach (var hit in root.GetProperty("hits").EnumerateArray())
            {
                items.Add(new MarketplaceItem
                {
                    Source      = ModSource.Modrinth,
                    Type        = type,
                    ProjectId   = Str(hit, "project_id"),
                    Name        = Str(hit, "title"),
                    Author      = Str(hit, "author"),
                    Description = Str(hit, "description"),
                    IconUrl     = hit.TryGetProperty("icon_url", out var ic) ? ic.GetString() : null,
                    Downloads   = hit.TryGetProperty("downloads", out var dl) ? dl.GetInt64() : 0,
                    Versions    = SummarizeVersions(hit),
                    DateCreated = ParseDate(hit, "date_created"),
                    DateUpdated = ParseDate(hit, "date_modified")
                });
            }

            return (items, total);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Modrinth] search failed: {ex.Message}");
            return (items, 0);
        }
    }

    // ── Download resolution ─────────────────────────────────────────────────────

    public async Task<(string FileName, string Url)?> ResolveDownloadAsync(
        string projectId, string? gameVersion, string? loader, ProjectType type, CancellationToken ct)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(gameVersion))
            qs.Add("game_versions=" + Uri.EscapeDataString($"[\"{gameVersion}\"]"));
        if (!string.IsNullOrEmpty(loader) && type == ProjectType.Mod)
            qs.Add("loaders=" + Uri.EscapeDataString($"[\"{loader}\"]"));

        var filtered = qs.Count > 0 ? $"{Base}/project/{projectId}/version?{string.Join("&", qs)}" : null;
        var unfiltered = $"{Base}/project/{projectId}/version";

        // Prefer a version matching the instance; fall back to the project's newest file
        if (filtered != null)
        {
            var hit = await TryResolveAsync(filtered, ct);
            if (hit != null) return hit;
        }
        return await TryResolveAsync(unfiltered, ct);
    }

    private async Task<(string, string)?> TryResolveAsync(string url, CancellationToken ct)
    {
        try
        {
            var body = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var files = doc.RootElement[0].GetProperty("files");
            if (files.GetArrayLength() == 0) return null;

            var chosen = files[0];
            foreach (var f in files.EnumerateArray())
                if (f.TryGetProperty("primary", out var p) && p.GetBoolean()) { chosen = f; break; }

            var fileName = chosen.GetProperty("filename").GetString();
            var fileUrl  = chosen.GetProperty("url").GetString();
            return fileName != null && fileUrl != null ? (fileName, fileUrl) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Modrinth] resolve failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Identifies a local jar by SHA-1 via /version_file/{hash}. Null when unknown to Modrinth.</summary>
    public async Task<string?> LookupProjectIdByHashAsync(string sha1, CancellationToken ct = default)
    {
        try
        {
            var body = await _http.GetStringAsync($"{Base}/version_file/{sha1}?algorithm=sha1", ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("project_id", out var id) ? id.GetString() : null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // not indexed by Modrinth (CurseForge-only or local mod)
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Modrinth] hash lookup failed: {ex.Message}");
            return null;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Modrinth has no ascending sorts — Least/Oldest use the closest index and are
    /// re-ordered client-side after the merge.</summary>
    private static string SortIndex(SortMode sort) => sort switch
    {
        SortMode.MostDownloaded  => "downloads",
        SortMode.LeastDownloaded => "downloads",
        SortMode.RecentlyUpdated => "updated",
        SortMode.Oldest          => "newest",
        SortMode.NewestRelease   => "newest",
        _                        => "relevance"
    };

    private static DateTime ParseDate(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
           DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : DateTime.MinValue;

    private static string FacetType(ProjectType t) => t switch
    {
        ProjectType.Modpack      => "modpack",
        ProjectType.ResourcePack => "resourcepack",
        ProjectType.Shader       => "shader",
        _                        => "mod"
    };

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    private static string SummarizeVersions(JsonElement hit)
    {
        if (!hit.TryGetProperty("versions", out var v) || v.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var list = v.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList();
        if (list.Count == 0) return string.Empty;
        return list.Count <= 3 ? string.Join(", ", list) : $"{string.Join(", ", list.Take(3))} +{list.Count - 3}";
    }
}
