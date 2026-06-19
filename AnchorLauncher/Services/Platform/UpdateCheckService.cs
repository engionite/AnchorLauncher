using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace AnchorLauncher.Services.Platform;

public class UpdateCheckResult
{
    public bool    UpdateAvailable { get; set; }
    public string  Latest          { get; set; } = string.Empty;
    public string? Url             { get; set; }
    public string  Notes           { get; set; } = string.Empty;
    public string  Message         { get; set; } = string.Empty;
}

/// <summary>Compares the running version against a hosted version.json feed.</summary>
public class UpdateCheckService
{
    public const string CurrentVersion = "1.0.2";
    private const string VersionUrl = "https://engionite.github.io/AnchorLauncher/version.json";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "AnchorLauncher/1.0" } }
    };

    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            var body = await _http.GetStringAsync(VersionUrl);
            using var doc = JsonDocument.Parse(body);
            var latest = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? CurrentVersion : CurrentVersion;
            var url    = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
            var notes  = doc.RootElement.TryGetProperty("notes", out var nt) ? nt.GetString() ?? string.Empty : string.Empty;

            var newer = CompareVersions(latest, CurrentVersion) > 0;
            return new UpdateCheckResult
            {
                UpdateAvailable = newer,
                Latest          = latest,
                Url             = url,
                Notes           = notes,
                Message         = newer ? $"Update available: v{latest}" : "You're on the latest version (v" + CurrentVersion + ")."
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Update] CheckAsync failed: {ex.Message}");
            return new UpdateCheckResult { Message = "Could not check for updates (offline or no release feed published yet)." };
        }
    }

    private static int CompareVersions(string a, string b)
    {
        int[] pa = Parse(a), pb = Parse(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] Parse(string s) =>
        s.TrimStart('v', 'V').Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
}
