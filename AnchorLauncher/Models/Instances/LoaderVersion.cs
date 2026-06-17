namespace AnchorLauncher.Models.Instances;

/// <summary>An available mod-loader build for a given Minecraft version.</summary>
public record LoaderVersion(string Version, bool Stable);
