namespace AnchorLauncher.Models.Skins;

/// <summary>A curated marketplace skin. Textures resolve from mc-heads.net by player name.</summary>
public class SkinMarketItem
{
    public string Name    { get; set; } = string.Empty;
    public string SkinUrl { get; set; } = string.Empty;   // full 64×64 skin texture
    public string HeadUrl { get; set; } = string.Empty;   // 2D head avatar for the grid tile
}
