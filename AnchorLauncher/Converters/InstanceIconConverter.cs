using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AnchorLauncher.Converters;

/// <summary>The built-in instance-icon keys offered by the picker, plus the custom-icon folder.</summary>
public static class InstanceIconCatalog
{
    public const string Default = "grass";

    /// <summary>Built-in icon keys (each has an "III_{key}" resource in Themes/InstanceIcons.xaml).</summary>
    public static IReadOnlyList<string> BuiltIn { get; } = new[]
    {
        "grass", "diamond", "gold", "iron", "emerald", "lapis", "copper", "amethyst",
        "redstone", "netherite", "creeper", "star", "heart", "chest", "crafting", "enchanting", "anchor",
    };

    /// <summary>Folder where user-uploaded custom icons are stored.</summary>
    public static string IconsDir
    {
        get
        {
            var root = Services.Storage.LauncherStorageService.AppDataRoot;
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnchorLauncher");
            return Path.Combine(root, "icons");
        }
    }

    public static bool IsCustom(string? iconId) =>
        !string.IsNullOrWhiteSpace(iconId) && (iconId.Contains('\\') || iconId.Contains('/'));
}

/// <summary>Resolves <see cref="Models.Instances.MinecraftInstance.IconId"/> to an ImageSource —
/// a custom image file, a built-in "III_{key}" resource, or the default ("grass").</summary>
public class InstanceIconConverter : IValueConverter
{
    private static readonly Dictionary<string, BitmapImage> _fileCache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var id = value as string;

        // Custom uploaded image (stored as an absolute path)
        if (InstanceIconCatalog.IsCustom(id) && File.Exists(id!))
            return LoadFile(id!);

        var key = "III_" + (string.IsNullOrWhiteSpace(id) ? InstanceIconCatalog.Default : id);
        return Application.Current?.TryFindResource(key) as ImageSource
            ?? Application.Current?.TryFindResource("III_" + InstanceIconCatalog.Default) as ImageSource;
    }

    internal static BitmapImage LoadFile(string path)
    {
        if (_fileCache.TryGetValue(path, out var cached)) return cached;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption   = BitmapCacheOption.OnLoad;   // don't keep the file locked
        bmp.DecodePixelWidth = 128;                     // icons are small; save memory
        bmp.UriSource     = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        _fileCache[path] = bmp;
        return bmp;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
