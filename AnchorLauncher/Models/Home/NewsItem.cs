namespace AnchorLauncher.Models.Home;

/// <summary>A single Anchor News card. Static today; structured so a real feed/API can replace it.</summary>
public class NewsItem
{
    public string Date        { get; set; } = string.Empty;
    public string Title       { get; set; } = string.Empty;
    public string Summary     { get; set; } = string.Empty;
}
