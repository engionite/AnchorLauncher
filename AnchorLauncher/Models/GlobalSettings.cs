namespace AnchorLauncher.Models;

/// <summary>
/// Launcher-wide defaults that instances inherit from. Persisted to settings.json.
/// </summary>
public enum CloudProvider   { OneDrive, GoogleDrive, Custom }
public enum ThemeMode       { Dark, OledBlack }
public enum LaunchPerfMode  { Standard, Optimized }
public enum ProxyMode       { None, Http, Socks5 }
public enum InstanceSort    { LastPlayed, Name }
public enum InstanceView    { Grid, List }
public enum LaunchAction    { KeepOpen, Minimize, Close }
public enum GameCloseAction { Reopen, KeepMinimized, Nothing }
public enum AnimationSpeed  { None, Reduced, Normal, Fast }
public enum ElyPatchMode    { Always, ElyOnly, Never }

public class GlobalSettings
{
    // ── Java / launch defaults ──
    public int     MemoryMB     { get; set; } = 2048;
    public string? JavaPath     { get; set; }            // null = auto-detect
    public int     WindowWidth  { get; set; } = 854;
    public int     WindowHeight { get; set; } = 480;
    public bool    Fullscreen   { get; set; } = false;
    public string  JvmArgs      { get; set; } = "-XX:+UseG1GC";

    // ── General ──
    public InstanceSort    InstanceSort        { get; set; } = InstanceSort.LastPlayed;
    public InstanceView    InstanceView        { get; set; } = InstanceView.Grid;
    public bool            ShowPlaytimeOnCards { get; set; } = true;
    public bool            RecordPlaytime      { get; set; } = true;
    public LaunchAction    OnLaunchAction      { get; set; } = LaunchAction.KeepOpen;
    public GameCloseAction OnGameCloseAction   { get; set; } = GameCloseAction.Reopen;

    // ── Appearance (was "Launcher") ──
    public ThemeMode       Theme                 { get; set; } = ThemeMode.Dark;
    public int             UiScalePercent        { get; set; } = 100;     // 90/100/110/125
    public string          Language              { get; set; } = "English";
    public bool            ShowInstanceNames     { get; set; } = true;
    public bool            MinimizeToTray        { get; set; } = false;
    public bool            StartWithWindows      { get; set; } = false;
    public bool            CheckUpdatesOnStartup { get; set; } = true;
    public string          ConsoleFont           { get; set; } = "Consolas";
    public int             ConsoleFontSize       { get; set; } = 13;      // 8–16
    public AnimationSpeed  AnimationSpeed        { get; set; } = AnimationSpeed.Normal;
    public bool            ShowAnimations        { get; set; } = true;

    // ── Minecraft ──
    public ElyPatchMode ElyPatchMode          { get; set; } = ElyPatchMode.ElyOnly;
    public bool         StartMaximized        { get; set; } = false;
    public bool         ShowConsoleOnCrash    { get; set; } = true;
    public bool         HideConsoleOnGameClose{ get; set; } = false;

    // ── Services ── (properties, not fields, so they persist to settings.json)
    public string LogPasteService { get; set; } = "https://api.mclo.gs/1/log";
    public string MetadataServer  { get; set; } = "https://elyprismlauncher.github.io/meta/v1";
    public string AssetsServer    { get; set; } = "https://resources.download.minecraft.net";
    // Optional user-supplied CurseForge key. The shipping key is injected at build time into
    // CurseForgeClient (AssemblyMetadata) and must NEVER be hardcoded here — this is a public repo.
    public string CurseForgeApiKey { get; set; } = "";
    public string ModrinthApiUrl  { get; set; } = "https://api.modrinth.com/v2";

    // ── Minecraft launch behavior ──
    public LaunchPerfMode LaunchPerfMode      { get; set; } = LaunchPerfMode.Standard;
    public bool           CloseOnLaunch       { get; set; } = false;
    public bool           KeepOpenMinimized   { get; set; } = false;
    public bool           ShowConsoleOnLaunch { get; set; } = true;
    public string?        PreLaunchScript     { get; set; }

    // ── Network ──
    public int       DownloadThreads    { get; set; } = 16;   // 4–32
    public bool      RetryDownloads     { get; set; } = true;
    public int       RetryAttempts      { get; set; } = 3;
    public int       HttpTimeoutSeconds { get; set; } = 60;
    public ProxyMode ProxyMode          { get; set; } = ProxyMode.None;
    public string?   ProxyHost          { get; set; }
    public int       ProxyPort          { get; set; } = 8080;

    // ── Cloud sync (file-copy into an already-mounted Drive/OneDrive folder — no OAuth) ──
    public CloudProvider CloudProvider   { get; set; } = CloudProvider.OneDrive;
    public string?       CloudCustomPath { get; set; }
    public bool          SyncWorlds      { get; set; } = true;
    public bool          SyncScreenshots { get; set; } = true;
    public bool          SyncOptions     { get; set; } = true;
    public bool          SyncControls    { get; set; } = false;
    public bool          AutoSyncOnClose { get; set; } = false;

    /// <summary>The G1GC performance flag set applied when LaunchPerfMode = Optimized.</summary>
    public static readonly string[] OptimizedFlags =
    {
        "-XX:+UseG1GC",
        "-XX:+UnlockExperimentalVMOptions",
        "-XX:G1NewSizePercent=20",
        "-XX:G1ReservePercent=20",
        "-XX:MaxGCPauseMillis=50",
        "-XX:G1HeapRegionSize=32M",
        "-Dfml.ignorePatchDiscrepancies=true"
    };
}
