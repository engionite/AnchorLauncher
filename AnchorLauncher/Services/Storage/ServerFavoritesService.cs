using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AnchorLauncher.Models.Home;

namespace AnchorLauncher.Services.Storage;

/// <summary>Persists Home-page server favorites to <c>server_favorites.json</c> in the app data root.</summary>
public class ServerFavoritesService
{
    private static string FilePath => Path.Combine(LauncherStorageService.AppDataRoot, "server_favorites.json");
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public List<ServerFavorite> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<ServerFavorite>();
            return JsonSerializer.Deserialize<List<ServerFavorite>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServerFav] load failed: {ex.Message}");
            return new List<ServerFavorite>();
        }
    }

    public void Save(IEnumerable<ServerFavorite> favorites)
    {
        try
        {
            Directory.CreateDirectory(LauncherStorageService.AppDataRoot);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(favorites.ToList(), _json));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ServerFav] save failed: {ex.Message}");
        }
    }
}
