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

    /// <summary>
    /// Given a local jar's SHA-1, asks Modrinth for the newest version compatible with the
    /// instance (POST /version_file/{hash}/update). Returns the new primary file's name, URL and
    /// SHA-1, or null when the mod is unknown or already current. The caller compares the returned
    /// SHA-1 against the input to decide whether it's actually an update.
    /// </summary>
    public async Task<(string FileName, string Url, string Sha1)?> GetUpdateByHashAsync(
        string sha1, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        try
        {
            var loaders = string.IsNullOrEmpty(loader)      ? "" : $"\"{loader}\"";
            var gvs     = string.IsNullOrEmpty(gameVersion) ? "" : $"\"{gameVersion}\"";
            var payload = $"{{\"loaders\":[{loaders}],\"game_versions\":[{gvs}]}}";
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            using var resp = await _http.PostAsync($"{Base}/version_file/{sha1}/update?algorithm=sha1", content, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("files", out var files) || files.GetArrayLength() == 0)
                return null;

            var chosen = files[0];
            foreach (var f in files.EnumerateArray())
                if (f.TryGetProperty("primary", out var p) && p.GetBoolean()) { chosen = f; break; }

            var fileName = chosen.GetProperty("filename").GetString();
            var url      = chosen.GetProperty("url").GetString();
            var newSha1  = chosen.TryGetProperty("hashes", out var h) && h.TryGetProperty("sha1", out var s)
                ? s.GetString() : null;

            return fileName != null && url != null && newSha1 != null ? (fileName, url, newSha1) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Modrinth] update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Required-dependency project ids of the version that best matches the instance
    /// (used by the dependency resolver to auto-offer e.g. Fabric API). Empty on any failure.</summary>
    public async Task<List<string>> GetRequiredDependenciesAsync(
        string projectId, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        var deps = new List<string>();
        try
        {
            var qs = new List<string>();
            if (!string.IsNullOrEmpty(gameVersion)) qs.Add("game_versions=" + Uri.EscapeDataString($"[\"{gameVersion}\"]"));
            if (!string.IsNullOrEmpty(loader))      qs.Add("loaders=" + Uri.EscapeDataString($"[\"{loader}\"]"));
            var url = qs.Count > 0 ? $"{Base}/project/{projectId}/version?{string.Join("&", qs)}"
                                   : $"{Base}/project/{projectId}/version";

            var body = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return deps;

            if (doc.RootElement[0].TryGetProperty("dependencies", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var d in arr.EnumerateArray())
                {
                    var type = d.TryGetProperty("dependency_type", out var dt) ? dt.GetString() : null;
                    var pid  = d.TryGetProperty("project_id", out var pp) ? pp.GetString() : null;
                    if (type == "required" && !string.IsNullOrEmpty(pid) && !deps.Contains(pid!)) deps.Add(pid!);
                }
        }
        catch (Exception ex) { Debug.WriteLine($"[Modrinth] dependency lookup failed: {ex.Message}"); }
        return deps;
    }

    /// <summary>Project title for a project id (for the dependency prompt). Null on failure.</summary>
    public async Task<string?> GetProjectNameAsync(string projectId, CancellationToken ct = default)
    {
        try
        {
            var body = await _http.GetStringAsync($"{Base}/project/{projectId}", ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Modrinth] project name lookup failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Resolves a local jar's SHA-1 to its Modrinth CDN file (url + hashes + size) so a
    /// .mrpack export can reference it as a download instead of bundling it. Null when unknown.</summary>
    public async Task<(string FileName, string Url, string Sha1, string Sha512, long FileSize)?> GetFileByHashAsync(
        string sha1, CancellationToken ct = default)
    {
        try
        {
            var body = await _http.GetStringAsync($"{Base}/version_file/{sha1}?algorithm=sha1", ct);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("files", out var files)) return null;

            foreach (var f in files.EnumerateArray())
            {
                if (!f.TryGetProperty("hashes", out var h)) continue;
                var fileSha1 = h.TryGetProperty("sha1", out var s1) ? s1.GetString() : null;
                if (!string.Equals(fileSha1, sha1, StringComparison.OrdinalIgnoreCase)) continue;

                var url    = f.GetProperty("url").GetString();
                var name   = f.GetProperty("filename").GetString();
                var sha512 = h.TryGetProperty("sha512", out var s5) ? s5.GetString() : null;
                var size   = f.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;

                if (url != null && name != null && sha512 != null)
                    return (name, url, sha1, sha512, size);
            }
            return null;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
        catch (Exception ex) { Debug.WriteLine($"[Modrinth] file-by-hash failed: {ex.Message}"); return null; }
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
