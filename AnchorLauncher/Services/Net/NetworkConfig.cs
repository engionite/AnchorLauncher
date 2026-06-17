namespace AnchorLauncher.Services.Net;

/// <summary>
/// Process-wide network knobs populated from GlobalSettings at startup. Static so the
/// existing static HttpClients and the launch pipeline can read them without a refactor.
/// </summary>
public static class NetworkConfig
{
    public static int  DownloadThreads { get; set; } = 16;   // 4–32
    public static bool RetryDownloads  { get; set; } = true;
    public static int  RetryAttempts   { get; set; } = 3;
    public static int  TimeoutSeconds  { get; set; } = 60;
}
