using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AnchorLauncher.Models.Instances;

namespace AnchorLauncher.Services.Instances;

/// <summary>A game instance discovered in another launcher, ready to import into Anchor.</summary>
public class DiscoveredInstance
{
    public string Launcher        { get; set; } = string.Empty;   // "Prism Launcher", "CurseForge", …
    public string Name            { get; set; } = string.Empty;
    public string GameDir         { get; set; } = string.Empty;   // the .minecraft-equivalent to copy
    public string Version         { get; set; } = string.Empty;   // best-effort; empty → latest release at import
    public string VersionType     { get; set; } = "release";
    public ModLoaderType Loader   { get; set; } = ModLoaderType.Vanilla;
    public bool   OfficialVanilla { get; set; }                   // exclude shared versions/libraries/assets
    public bool   IsSelected      { get; set; } = true;           // checkbox state in the import dialog

    /// <summary>Display label for the import dialog, e.g. "Fabric · 1.20.1".</summary>
    public string Detail =>
        $"{(Loader == ModLoaderType.Vanilla ? "Vanilla" : Loader.ToString())}"
        + (string.IsNullOrWhiteSpace(Version) ? string.Empty : $" · {Version}");
}

/// <summary>
/// Scans the machine for other launchers' instances (official Minecraft, Prism, PolyMC, MultiMC,
/// CurseForge, Modrinth App) and imports them into Anchor by copying their game directory. Accounts
/// are never imported — they're DPAPI-encrypted per launcher and can't be moved.
/// </summary>
public class LauncherMigrationService
{
    private readonly InstanceService       _instances = new();
    private readonly MojangManifestService _manifest  = new();
    private string? _latestRelease;

    public List<DiscoveredInstance> Scan()
    {
        var found = new List<DiscoveredInstance>();
        Try(() => ScanOfficial(found),  "official");
        Try(() => ScanPrismLike(found), "prism");
        Try(() => ScanCurseForge(found),"curseforge");
        Try(() => ScanModrinth(found),  "modrinth");
        return found;

        static void Try(Action a, string tag) { try { a(); } catch (Exception ex) { Debug.WriteLine($"[Migrate] {tag}: {ex.Message}"); } }
    }

    public async Task<MinecraftInstance> ImportAsync(
        DiscoveredInstance d, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        progress?.Report(DownloadProgress.At(5, d.Name));

        var version = d.Version;
        if (string.IsNullOrWhiteSpace(version))
            version = await LatestReleaseAsync() ?? "1.20.1";

        Func<string, bool>? filter = null;
        if (d.OfficialVanilla)
            filter = rel =>
            {
                var top = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0].ToLowerInvariant();
                return top is not ("versions" or "libraries" or "assets" or "logs" or "crash-reports");
            };

        ct.ThrowIfCancellationRequested();
        var inst = await _instances.ImportFromGameDirAsync(
            d.GameDir, $"{d.Name} ({d.Launcher})", version, d.VersionType, d.Loader, null, filter, progress, ct);

        progress?.Report(DownloadProgress.At(100, d.Name));
        return inst;
    }

    private async Task<string?> LatestReleaseAsync()
    {
        if (_latestRelease != null) return _latestRelease;
        try
        {
            // Cap the manifest fetch so a slow/blocked network can never hang the whole import.
            var task = _manifest.GetManifestAsync();
            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(8))) == task)
                _latestRelease = (await task)?.Versions.FirstOrDefault(v => v.IsRelease)?.Id;
        }
        catch (Exception ex) { Debug.WriteLine($"[Migrate] latest release: {ex.Message}"); }
        return _latestRelease;
    }

    // ── Scanners ──────────────────────────────────────────────────────────────

    private static string AppData     => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string MyDocuments => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private static void ScanOfficial(List<DiscoveredInstance> found)
    {
        var mc    = Path.Combine(AppData, ".minecraft");
        var saves = Path.Combine(mc, "saves");
        if (Directory.Exists(mc) && Directory.Exists(saves) && Directory.EnumerateDirectories(saves).Any())
            found.Add(new DiscoveredInstance
            {
                Launcher = "Minecraft", Name = "Singleplayer worlds", GameDir = mc, OfficialVanilla = true
            });
    }

    private static void ScanPrismLike(List<DiscoveredInstance> found)
    {
        foreach (var (launcher, root) in new[]
        {
            ("Prism Launcher", Path.Combine(AppData, "PrismLauncher", "instances")),
            ("PolyMC",         Path.Combine(AppData, "PolyMC", "instances")),
            ("MultiMC",        Path.Combine(AppData, "MultiMC", "instances")),
        })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(".") || name.Equals("_LAUNCHER_TEMP", StringComparison.OrdinalIgnoreCase)) continue;

                var gameDir = FirstExisting(Path.Combine(dir, ".minecraft"), Path.Combine(dir, "minecraft"));
                if (gameDir == null) continue;

                var d = new DiscoveredInstance { Launcher = launcher, Name = name, GameDir = gameDir };
                ParseMmcPack(Path.Combine(dir, "mmc-pack.json"), d);
                found.Add(d);
            }
        }
    }

    private static void ScanCurseForge(List<DiscoveredInstance> found)
    {
        foreach (var root in new[]
        {
            Path.Combine(UserProfile, "curseforge", "minecraft", "Instances"),
            Path.Combine(MyDocuments, "Curse", "Minecraft", "Instances"),
        })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var d = new DiscoveredInstance { Launcher = "CurseForge", Name = Path.GetFileName(dir), GameDir = dir };
                ParseCurseForge(Path.Combine(dir, "minecraftinstance.json"), d);
                found.Add(d);
            }
        }
    }

    private static void ScanModrinth(List<DiscoveredInstance> found)
    {
        foreach (var root in new[]
        {
            Path.Combine(AppData, "com.modrinth.theseus", "profiles"),
            Path.Combine(AppData, "ModrinthApp", "profiles"),
        })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var d = new DiscoveredInstance { Launcher = "Modrinth", Name = Path.GetFileName(dir), GameDir = dir };
                ParseModrinth(Path.Combine(dir, "profile.json"), d);
                found.Add(d);
            }
        }
    }

    // ── Metadata parsers (best-effort; unknowns fall back to latest release / Vanilla) ──────────

    private static void ParseMmcPack(string path, DiscoveredInstance d)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("components", out var comps)) return;
            foreach (var c in comps.EnumerateArray())
            {
                var uid = c.TryGetProperty("uid", out var u) ? u.GetString() : null;
                var ver = c.TryGetProperty("version", out var v) ? v.GetString() : null;
                switch (uid)
                {
                    case "net.minecraft":              d.Version = ver ?? d.Version;       break;
                    case "net.fabricmc.fabric-loader": d.Loader  = ModLoaderType.Fabric;   break;
                    case "org.quiltmc.quilt-loader":   d.Loader  = ModLoaderType.Quilt;    break;
                    case "net.minecraftforge":         d.Loader  = ModLoaderType.Forge;    break;
                    case "net.neoforged":              d.Loader  = ModLoaderType.NeoForge; break;
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Migrate] mmc-pack: {ex.Message}"); }
    }

    private static void ParseCurseForge(string path, DiscoveredInstance d)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("gameVersion", out var gv)) d.Version = gv.GetString() ?? d.Version;
            if (root.TryGetProperty("baseModLoader", out var ml) && ml.ValueKind == JsonValueKind.Object &&
                ml.TryGetProperty("name", out var mn))
                d.Loader = LoaderFromName(mn.GetString());
        }
        catch (Exception ex) { Debug.WriteLine($"[Migrate] cf json: {ex.Message}"); }
    }

    private static void ParseModrinth(string path, DiscoveredInstance d)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var meta = doc.RootElement;
            if (meta.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object) meta = m;
            if (meta.TryGetProperty("game_version", out var gv)) d.Version = gv.GetString() ?? d.Version;
            if (meta.TryGetProperty("loader", out var l))        d.Loader  = LoaderFromName(l.GetString());
        }
        catch (Exception ex) { Debug.WriteLine($"[Migrate] modrinth json: {ex.Message}"); }
    }

    private static ModLoaderType LoaderFromName(string? name)
    {
        var n = (name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("neoforge")) return ModLoaderType.NeoForge;
        if (n.Contains("forge"))    return ModLoaderType.Forge;
        if (n.Contains("fabric"))   return ModLoaderType.Fabric;
        if (n.Contains("quilt"))    return ModLoaderType.Quilt;
        return ModLoaderType.Vanilla;
    }

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(Directory.Exists);
}
