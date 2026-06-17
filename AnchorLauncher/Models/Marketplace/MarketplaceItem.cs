using CommunityToolkit.Mvvm.ComponentModel;

namespace AnchorLauncher.Models.Marketplace;

public enum ModSource { Modrinth, CurseForge }

/// <summary>The four searchable project kinds. Maps to Modrinth facets / CurseForge classIds.</summary>
public enum ProjectType { Mod, Modpack, ResourcePack, Shader }

public enum InstallState { Idle, Installing, Installed, Failed }

/// <summary>Marketplace result ordering. Mapped to each provider's native sort where possible.</summary>
public enum SortMode { MostDownloaded, LeastDownloaded, RecentlyUpdated, Oldest, NewestRelease }

/// <summary>A unified search result from either Modrinth or CurseForge.</summary>
public partial class MarketplaceItem : ObservableObject
{
    public ModSource   Source      { get; set; }
    public ProjectType Type        { get; set; }
    /// <summary>Modrinth project_id, or CurseForge mod id (as string).</summary>
    public string      ProjectId   { get; set; } = string.Empty;
    public string      Name        { get; set; } = string.Empty;
    public string      Author      { get; set; } = string.Empty;
    public string      Description { get; set; } = string.Empty;
    public string?     IconUrl     { get; set; }
    public long        Downloads   { get; set; }
    public string      Versions    { get; set; } = string.Empty;
    public DateTime    DateCreated { get; set; }
    public DateTime    DateUpdated { get; set; }

    /// <summary>Per-card install progress, driven by the ViewModel.</summary>
    [ObservableProperty] private InstallState _state = InstallState.Idle;

    public string SourceLabel      => Source == ModSource.Modrinth ? "Modrinth" : "CurseForge";
    public string DownloadsDisplay => FormatCount(Downloads);

    public static string FormatCount(long n) =>
        n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M" :
        n >= 1_000     ? $"{n / 1_000.0:0.#}K" :
        n.ToString();
}
