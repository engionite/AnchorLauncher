namespace AnchorLauncher.Models.Home;

/// <summary>A saved server for Home-page quick-connect.</summary>
public class ServerFavorite
{
    public string Name { get; set; } = string.Empty;
    public string Ip   { get; set; } = string.Empty;
    public int    Port { get; set; } = 25565;

    public string Display => string.IsNullOrWhiteSpace(Name) ? Ip : Name;
    public string Address => Port == 25565 ? Ip : $"{Ip}:{Port}";
}
