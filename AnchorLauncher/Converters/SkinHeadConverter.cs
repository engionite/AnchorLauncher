using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Services.Skins;

namespace AnchorLauncher.Converters;

/// <summary>
/// Binds an <see cref="ILauncherAccount"/> to its skin head (32×32). Returns the cached head
/// synchronously when available; otherwise returns a transparent <see cref="WriteableBitmap"/>
/// that <see cref="SkinHeadService"/> fills in place once the async fetch (or Steve fallback)
/// completes. Use with <c>RenderOptions.BitmapScalingMode="NearestNeighbor"</c>.
/// </summary>
public class SkinHeadConverter : IValueConverter
{
    private static readonly SkinHeadService _service = new();
    private const int Out = 32;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ILauncherAccount account) return null;

        if (_service.TryGetCached(account, out var cached) && cached != null)
            return cached;

        // Placeholder filled in place when the head resolves.
        var wb = new WriteableBitmap(Out, Out, 96, 96, PixelFormats.Bgra32, null);
        _ = FillAsync(account, wb);
        return wb;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static async Task FillAsync(ILauncherAccount account, WriteableBitmap target)
    {
        try
        {
            var head = await _service.GetHeadBitmapAsync(account);   // never null (Steve fallback)

            var px = new byte[Out * Out * 4];
            var converted = head.PixelWidth == Out && head.PixelHeight == Out
                ? head
                : new TransformedBitmap(head, new ScaleTransform((double)Out / head.PixelWidth, (double)Out / head.PixelHeight));
            converted.CopyPixels(px, Out * 4, 0);

            target.Dispatcher.Invoke(() =>
            {
                try { target.WritePixels(new Int32Rect(0, 0, Out, Out), px, Out * 4, 0); }
                catch (Exception ex) { Debug.WriteLine($"[SkinHeadConv] write failed: {ex.Message}"); }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinHeadConv] fill failed: {ex.Message}");
        }
    }
}
