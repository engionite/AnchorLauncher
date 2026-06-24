using System.IO;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Models.Marketplace;
using AnchorLauncher.Services.Net;

namespace AnchorLauncher.Services.Marketplace;

/// <summary>Result of a single aggregated search page.</summary>
public class MarketplaceSearchResult
{
    public List<MarketplaceItem> Items        { get; set; } = new();
    public int                   ModrinthTotal { get; set; }
    public int                   CurseTotal    { get; set; }
}

/// <summary>
/// Aggregates Modrinth + CurseForge into one search/result/install surface. Search hits
/// both providers in parallel and merges by popularity; install resolves the right file
/// for the target instance and drops it into the matching subfolder.
/// </summary>
public class MarketplaceService
{
    private readonly ModrinthClient   _modrinth = new();
    private readonly CurseForgeClient _curse    = new();

    /// <param name="gameVersion">The version-filter dropdown value; null = all versions.</param>
    /// <param name="compatibleOnly">When true, additionally constrains results to the target
    /// instance's mod loader so only explicitly-compatible mods are returned.</param>
    public async Task<MarketplaceSearchResult> SearchAsync(
        string query, ProjectType type, MinecraftInstance? target,
        string? gameVersion, bool compatibleOnly, SortMode sort,
        int modrinthOffset, int curseIndex, int pageSize, CancellationToken ct)
    {
        var loaderName = compatibleOnly ? LoaderName(target?.ModLoader) : null;
        var loaderInt  = compatibleOnly ? LoaderInt(target?.ModLoader) : 0;

        var modrinthTask = _modrinth.SearchAsync(query, type, gameVersion, loaderName, sort, modrinthOffset, pageSize, ct);
        var curseTask    = _curse.SearchAsync(query, type, gameVersion, loaderInt, sort, curseIndex, pageSize, ct);
        await Task.WhenAll(modrinthTask, curseTask);

        var (mItems, mTotal) = modrinthTask.Result;
        var (cItems, cTotal) = curseTask.Result;

        // Merge the two provider pages under the requested ordering
        var merged = Order(mItems.Concat(cItems), sort).ToList();

        return new MarketplaceSearchResult { Items = merged, ModrinthTotal = mTotal, CurseTotal = cTotal };
    }

    private static IEnumerable<MarketplaceItem> Order(IEnumerable<MarketplaceItem> items, SortMode sort) => sort switch
    {
        SortMode.MostDownloaded  => items.OrderByDescending(i => i.Downloads),
        SortMode.LeastDownloaded => items.OrderBy(i => i.Downloads),
        SortMode.RecentlyUpdated => items.OrderByDescending(i => i.DateUpdated),
        SortMode.Oldest          => items.OrderBy(i => i.DateCreated == DateTime.MinValue ? DateTime.MaxValue : i.DateCreated),
        SortMode.NewestRelease   => items.OrderByDescending(i => i.DateCreated),
        _                        => items.OrderByDescending(i => i.Downloads)
    };

    /// <summary>Resolves the best file for the instance and downloads it into the right subfolder.</summary>
    public async Task<string> InstallAsync(MarketplaceItem item, MinecraftInstance instance, CancellationToken ct = default)
    {
        var (fileName, url) = item.Source == ModSource.Modrinth
            ? await _modrinth.ResolveDownloadAsync(item.ProjectId, instance.Version, LoaderName(instance.ModLoader), item.Type, ct)
              ?? throw new InvalidOperationException("No compatible Modrinth file for this instance.")
            : await _curse.ResolveDownloadAsync(item.ProjectId, instance.Version, LoaderInt(instance.ModLoader), item.Type, ct)
              ?? throw new InvalidOperationException("No compatible CurseForge file for this instance.");

        var dest = Path.Combine(instance.GameDir, FolderFor(item.Type), fileName);
        await DownloadHelper.DownloadFileAsync(url, dest, null, ct);
        return dest;
    }

    // ── Dependency resolution (Modrinth) ─────────────────────────────────────────

    /// <summary>A required dependency of a mod that isn't yet in the instance's mods folder.</summary>
    public record PendingDependency(string ProjectId, string Name, string FileName, string Url);

    /// <summary>
    /// Resolves the required Modrinth dependencies of a mod (e.g. Fabric API) that aren't already
    /// present in the instance, so the caller can offer to install them. Returns an empty list for
    /// CurseForge items, non-mods, or when every dependency is already installed.
    /// </summary>
    public async Task<List<PendingDependency>> ResolveMissingDependenciesAsync(
        MarketplaceItem item, MinecraftInstance instance, CancellationToken ct = default)
    {
        var missing = new List<PendingDependency>();
        if (item.Source != ModSource.Modrinth || item.Type != ProjectType.Mod) return missing;

        var loader  = LoaderName(instance.ModLoader);
        var modsDir = Path.Combine(instance.GameDir, "mods");

        // Walk the dependency graph breadth-first (transitive: deps of deps, e.g. a mod needs
        // Architectury which needs Cloth Config). The 'seen' set dedups and breaks cycles.
        var seen  = new HashSet<string> { item.ProjectId };
        var queue = new Queue<string>();
        foreach (var d in await _modrinth.GetRequiredDependenciesAsync(item.ProjectId, instance.Version, loader, ct))
            if (seen.Add(d)) queue.Enqueue(d);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var depId    = queue.Dequeue();
            var resolved = await _modrinth.ResolveDownloadAsync(depId, instance.Version, loader, ProjectType.Mod, ct);
            if (resolved is not { } r) continue;

            // Pull in this dependency's own required dependencies too.
            foreach (var sub in await _modrinth.GetRequiredDependenciesAsync(depId, instance.Version, loader, ct))
                if (seen.Add(sub)) queue.Enqueue(sub);

            // Already installed (enabled or disabled) → nothing to offer.
            if (File.Exists(Path.Combine(modsDir, r.FileName)) ||
                File.Exists(Path.Combine(modsDir, r.FileName + ".disabled")))
                continue;

            var name = await _modrinth.GetProjectNameAsync(depId, ct) ?? r.FileName;
            missing.Add(new PendingDependency(depId, name, r.FileName, r.Url));
        }
        return missing;
    }

    /// <summary>Downloads one resolved dependency jar into the instance's mods folder.</summary>
    public async Task InstallDependencyAsync(PendingDependency dep, MinecraftInstance instance, CancellationToken ct = default)
    {
        var dest = Path.Combine(instance.GameDir, "mods", dep.FileName);
        await DownloadHelper.DownloadFileAsync(dep.Url, dest, null, ct);
    }

    // ── Mappings ────────────────────────────────────────────────────────────────

    private static string FolderFor(ProjectType t) => t switch
    {
        ProjectType.ResourcePack => "resourcepacks",
        ProjectType.Shader       => "shaderpacks",
        ProjectType.Modpack      => "modpacks",
        _                        => "mods"
    };

    private static string? LoaderName(ModLoaderType? loader) => loader switch
    {
        ModLoaderType.Fabric   => "fabric",
        ModLoaderType.Forge    => "forge",
        ModLoaderType.NeoForge => "neoforge",
        ModLoaderType.Quilt    => "quilt",
        _                      => null
    };

    private static int LoaderInt(ModLoaderType? loader) => loader switch
    {
        ModLoaderType.Forge    => 1,
        ModLoaderType.Fabric   => 4,
        ModLoaderType.Quilt    => 5,
        ModLoaderType.NeoForge => 6,
        _                      => 0
    };
}
