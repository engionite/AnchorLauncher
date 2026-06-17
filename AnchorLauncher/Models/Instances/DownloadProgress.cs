namespace AnchorLauncher.Models.Instances;

/// <summary>Progress payload pushed through <see cref="IProgress{T}"/> during installs/launches.</summary>
public class DownloadProgress
{
    public double Percent { get; set; }          // 0..100
    public string Status  { get; set; } = string.Empty;
    public long   BytesReceived { get; set; }
    public long   TotalBytes    { get; set; }

    public static DownloadProgress At(double percent, string status) =>
        new() { Percent = Math.Clamp(percent, 0, 100), Status = status };
}
