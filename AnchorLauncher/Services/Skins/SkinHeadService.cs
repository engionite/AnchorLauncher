using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnchorLauncher.Models.Auth;

namespace AnchorLauncher.Services.Skins;

/// <summary>
/// Resolves a Minecraft account's skin head as a crisp 32×32 <see cref="BitmapSource"/>:
/// downloads the skin, crops the 8×8 face at (8,8) plus the hat overlay at (40,8), and
/// nearest-neighbour-upscales 4×. Results are cached in memory by account identity; any failure
/// falls back to the bundled Steve head.
/// </summary>
public class SkinHeadService
{
    private const int Face = 8, Scale = 4, Out = Face * Scale;   // 32×32

    // Skin fetching goes through SkinHttp (manual redirect-follow for ely.by's HTTPS→HTTP 301).
    private static readonly ConcurrentDictionary<string, BitmapSource> _cache = new();
    private static readonly ConcurrentDictionary<string, byte> _forceFresh = new(StringComparer.OrdinalIgnoreCase);
    private static BitmapSource? _steveHead;

    /// <summary>Raised when any cached head is invalidated, so account-avatar bindings can re-render.</summary>
    public static event Action? CacheInvalidated;

    private static string KeyFor(ILauncherAccount a) => $"{a.AccountType}:{a.Username}";

    public bool TryGetCached(ILauncherAccount account, out BitmapSource? head)
        => _cache.TryGetValue(KeyFor(account), out head);

    /// <summary>Drops the cached head(s) for a username so the next request re-downloads.</summary>
    public static void InvalidateCache(string username)
    {
        if (string.IsNullOrEmpty(username)) return;
        foreach (var key in _cache.Keys)
            if (key.EndsWith(":" + username, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
        _forceFresh[username] = 1;   // make the next fetch cache-bust the CDN
        CacheInvalidated?.Invoke();
    }

    public static void InvalidateAll()
    {
        _cache.Clear();
        CacheInvalidated?.Invoke();
    }

    public async Task<BitmapSource> GetHeadBitmapAsync(ILauncherAccount account, bool forceFresh = false)
    {
        var key = KeyFor(account);
        // A pending invalidation forces this fetch to be fresh even though the binding re-ran normally.
        if (_forceFresh.TryRemove(account.Username, out _)) forceFresh = true;
        if (!forceFresh && _cache.TryGetValue(key, out var cached)) return cached;

        BitmapSource head;
        try
        {
            var url = ResolveSkinUrl(account);
            if (string.IsNullOrWhiteSpace(url)) { head = SteveHead(); }
            else
            {
                if (forceFresh)   // bust any CDN cache so a just-changed skin is picked up
                    url += (url.Contains('?') ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var bytes = await SkinHttp.GetBytesAsync(url);   // follows ely.by's HTTPS→HTTP 301
                head = await Task.Run(() => HeadFromPng(bytes)) ?? SteveHead();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinHead] {account.Username} failed: {ex.Message} — using Steve.");
            head = SteveHead();
        }

        _cache[key] = head;
        return head;
    }

    private static string? ResolveSkinUrl(ILauncherAccount account) => account.AccountType switch
    {
        AccountType.ElyBy => $"https://skinsystem.ely.by/skins/{Uri.EscapeDataString(account.Username)}.png",
        _ => string.IsNullOrWhiteSpace(account.SkinUrl) ? null : account.SkinUrl
    };

    /// <summary>The bundled Steve skin's head, built once.</summary>
    public BitmapSource SteveHead()
    {
        if (_steveHead != null) return _steveHead;
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/steve_skin.png");
            var info = Application.GetResourceStream(uri);
            if (info != null)
            {
                using var s = info.Stream;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                _steveHead = HeadFromPng(ms.ToArray());
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[SkinHead] Steve load failed: {ex.Message}"); }

        // Last-ditch: a flat skin-tone square so the UI never shows nothing.
        _steveHead ??= SolidHead(Color.FromRgb(0x9B, 0x6E, 0x4F));
        return _steveHead;
    }

    private static BitmapSource SolidHead(Color c)
    {
        var px = new byte[Out * Out * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        var bmp = BitmapSource.Create(Out, Out, 96, 96, PixelFormats.Bgra32, null, px, Out * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Crops the face (8,8) + hat (40,8) layers and 4×-upscales → a frozen 32×32 BitmapSource.</summary>
    public static BitmapSource? HeadFromPng(byte[] png)
    {
        try
        {
            using var ms = new MemoryStream(png);
            var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var src = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            int sw = src.PixelWidth, sh = src.PixelHeight;
            if (sw < 64) return null;

            var srcPixels = new byte[sw * sh * 4];
            src.CopyPixels(srcPixels, sw * 4, 0);

            byte[] At(int x, int y)
            {
                int i = (y * sw + x) * 4;
                return new[] { srcPixels[i], srcPixels[i + 1], srcPixels[i + 2], srcPixels[i + 3] };
            }

            var outBuf = new byte[Out * Out * 4];
            for (int fy = 0; fy < Face; fy++)
            for (int fx = 0; fx < Face; fx++)
            {
                var face = At(8 + fx, 8 + fy);
                var hat  = At(40 + fx, 8 + fy);
                byte[] px;
                if (hat[3] > 0)
                {
                    double a = hat[3] / 255.0;
                    px = new byte[]
                    {
                        (byte)(hat[0] * a + face[0] * (1 - a)),
                        (byte)(hat[1] * a + face[1] * (1 - a)),
                        (byte)(hat[2] * a + face[2] * (1 - a)),
                        Math.Max(face[3], hat[3])
                    };
                }
                else px = face;

                for (int dy = 0; dy < Scale; dy++)
                for (int dx = 0; dx < Scale; dx++)
                {
                    int oi = ((fy * Scale + dy) * Out + (fx * Scale + dx)) * 4;
                    outBuf[oi] = px[0]; outBuf[oi + 1] = px[1]; outBuf[oi + 2] = px[2]; outBuf[oi + 3] = px[3];
                }
            }

            var bmp = BitmapSource.Create(Out, Out, 96, 96, PixelFormats.Bgra32, null, outBuf, Out * 4);
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinHead] decode failed: {ex.Message}");
            return null;
        }
    }
}
