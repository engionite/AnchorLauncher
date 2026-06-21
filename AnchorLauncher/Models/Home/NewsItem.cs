namespace AnchorLauncher.Models.Home;

/// <summary>A single Anchor News card. Static today; structured so a real feed/API can replace it.</summary>
public class NewsItem
{
    public string  Date       { get; set; } = string.Empty;
    public string  Title      { get; set; } = string.Empty;
    public string  Summary    { get; set; } = string.Empty;
    /// <summary>GitHub release tag this post announces (e.g. "v1.0.7"). When set, the detail
    /// dialog shows a "View this release" link; otherwise it just links to the repository.</summary>
    public string? ReleaseTag { get; set; }
}
