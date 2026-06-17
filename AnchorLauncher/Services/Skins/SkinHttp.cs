using System.Net.Http;
using System.Text;

namespace AnchorLauncher.Services.Skins;

/// <summary>
/// Shared HTTP for skin fetching that follows redirects <b>manually</b>, including the
/// HTTPS→HTTP downgrade that ely.by uses (skinsystem.ely.by/skins/{user}.png 301-redirects to
/// http://ely.by/storage/skins/{hash}.png). The built-in <see cref="HttpClient"/> auto-redirect
/// refuses HTTPS→HTTP for security, which made every Ely.by skin/head fetch throw a 301.
/// </summary>
internal static class SkinHttp
{
    private static readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "AnchorLauncher/1.0" } }
    };

    public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
    {
        for (int hop = 0; hop < 6; hop++)
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            int code = (int)resp.StatusCode;
            if (code is 301 or 302 or 303 or 307 or 308 && resp.Headers.Location is { } loc)
            {
                url = loc.IsAbsoluteUri ? loc.ToString() : new Uri(new Uri(url), loc).ToString();
                continue;   // re-issue the GET ourselves → HTTPS→HTTP downgrade is allowed
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        throw new HttpRequestException("Too many redirects while fetching the skin URL.");
    }

    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
        => Encoding.UTF8.GetString(await GetBytesAsync(url, ct));
}
