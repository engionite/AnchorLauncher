using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Skins;

/// <summary>
/// Skin texture handling: download, flat front/back composition from the standard 64×64
/// layout, Microsoft profile upload, and a local last-5 history.
/// </summary>
public class SkinService
{
    private const string MsSkinEndpoint = "https://api.minecraftservices.com/minecraft/profile/skins";
    private const int MaxHistory = 5;

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "AnchorLauncher/1.0" } }
    };

    private static string HistoryDir => Path.Combine(LauncherStorageService.AppDataRoot, "skin_history");

    // ── Fetch + compose ─────────────────────────────────────────────────────────

    public async Task<byte[]?> DownloadSkinAsync(string skinUrl, CancellationToken ct = default)
    {
        try { return await SkinHttp.GetBytesAsync(skinUrl, ct); }   // follows ely.by's HTTPS→HTTP 301
        catch (Exception ex)
        {
            Debug.WriteLine($"[Skin] download failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads the Ely.by texture metadata to tell whether the account's skin uses the slim (Alex)
    /// model — the 3D viewer can't reliably detect this from the pixels alone.
    /// </summary>
    public async Task<bool> IsElySlimAsync(string username, CancellationToken ct = default)
    {
        try
        {
            var json = await SkinHttp.GetStringAsync(
                $"https://skinsystem.ely.by/textures/{Uri.EscapeDataString(username)}", ct);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("SKIN", out var skin) &&
                skin.TryGetProperty("metadata", out var meta) &&
                meta.TryGetProperty("model", out var model))
                return string.Equals(model.GetString(), "slim", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) { Debug.WriteLine($"[Skin] ely model lookup failed: {ex.Message}"); }
        return false;
    }

    /// <summary>Composes a flat front (or back) view from the standard skin layout.</summary>
    public ImageSource? ComposeView(byte[] pngBytes, bool front)
    {
        try
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(pngBytes))
            {
                bmp.BeginInit();
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();

            bool legacy = bmp.PixelHeight == 32;   // pre-1.8 64×32 textures
            const int S = 12;                       // upscale factor

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // (sx, sy, w, h) source regions; destination grid is 16×32 logical px
                void Draw(int sx, int sy, int w, int h, int dx, int dy, bool mirror = false)
                {
                    ImageSource part = new CroppedBitmap(bmp, new Int32Rect(sx, sy, w, h));
                    if (mirror)
                    {
                        var tb = new TransformedBitmap((CroppedBitmap)part, new ScaleTransform(-1, 1));
                        part = tb;
                    }
                    dc.DrawImage(part, new Rect(dx * S, dy * S, w * S, h * S));
                }

                if (front)
                {
                    Draw(8, 8, 8, 8, 4, 0);                      // head
                    Draw(20, 20, 8, 12, 4, 8);                   // body
                    Draw(44, 20, 4, 12, 0, 8);                   // right arm
                    if (legacy) Draw(44, 20, 4, 12, 12, 8, mirror: true);
                    else        Draw(36, 52, 4, 12, 12, 8);      // left arm
                    Draw(4, 20, 4, 12, 4, 20);                   // right leg
                    if (legacy) Draw(4, 20, 4, 12, 8, 20, mirror: true);
                    else        Draw(20, 52, 4, 12, 8, 20);      // left leg
                }
                else
                {
                    Draw(24, 8, 8, 8, 4, 0);                     // head back
                    Draw(32, 20, 8, 12, 4, 8);                   // body back
                    Draw(52, 20, 4, 12, 12, 8);                  // right arm back
                    if (legacy) Draw(52, 20, 4, 12, 0, 8, mirror: true);
                    else        Draw(44, 52, 4, 12, 0, 8);       // left arm back
                    Draw(12, 20, 4, 12, 8, 20);                  // right leg back
                    if (legacy) Draw(12, 20, 4, 12, 4, 20, mirror: true);
                    else        Draw(28, 52, 4, 12, 4, 20);      // left leg back
                }
            }

            var target = new RenderTargetBitmap(16 * S, 32 * S, 96, 96, PixelFormats.Pbgra32);
            target.Render(dv);
            target.Freeze();
            return target;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Skin] compose failed: {ex.Message}");
            return null;
        }
    }

    // ── Upload (Microsoft accounts) ─────────────────────────────────────────────

    /// <summary>Uploads a .png as the new skin via the Minecraft services API.</summary>
    public async Task UploadMicrosoftSkinAsync(string bearerToken, string pngPath, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("classic"), "variant");

        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(pngPath, ct));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", Path.GetFileName(pngPath));

        using var req = new HttpRequestMessage(HttpMethod.Post, MsSkinEndpoint) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            Debug.WriteLine($"[Skin] upload failed {(int)resp.StatusCode}: {body}");
            throw new InvalidOperationException($"Skin upload was rejected (HTTP {(int)resp.StatusCode}).");
        }
        Debug.WriteLine("[Skin] uploaded via Minecraft services API.");
    }

    // ── History (last 5, hash-deduplicated) ─────────────────────────────────────

    public void RecordHistory(byte[] pngBytes)
    {
        try
        {
            Directory.CreateDirectory(HistoryDir);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(pngBytes))[..16];

            if (Directory.EnumerateFiles(HistoryDir, $"*-{hash}.png").Any())
                return; // already recorded

            var path = Path.Combine(HistoryDir, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{hash}.png");
            File.WriteAllBytes(path, pngBytes);

            var all = Directory.GetFiles(HistoryDir, "*.png").OrderBy(f => f).ToList();
            while (all.Count > MaxHistory)
            {
                try { File.Delete(all[0]); } catch { }
                all.RemoveAt(0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Skin] history record failed: {ex.Message}");
        }
    }

    public List<string> ListHistory()
    {
        try
        {
            return Directory.Exists(HistoryDir)
                ? Directory.GetFiles(HistoryDir, "*.png").OrderByDescending(f => f).ToList()
                : new List<string>();
        }
        catch { return new List<string>(); }
    }
}
