using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AnchorLauncher.Models;

namespace AnchorLauncher.Services.Storage;

public static class LauncherStorageService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented    = true,
        PropertyNameCaseInsensitive = true
    };

    // Serializes settings writes so debounced auto-saves can never overlap (torn file).
    private static readonly SemaphoreSlim _settingsWriteLock = new(1, 1);

    public static string AppDataRoot   { get; private set; } = string.Empty;
    public static string InstancesRoot { get; private set; } = string.Empty;
    public static string AssetsRoot    { get; private set; } = string.Empty;
    public static string JavaRoot      { get; private set; } = string.Empty;
    public static string LogsRoot      { get; private set; } = string.Empty;
    private static string ConfigPath   => Path.Combine(AppDataRoot, "config.json");

    // Auth-related paths
    public static string AccountsPath         => Path.Combine(AppDataRoot, "accounts.json");
    public static string MsalCachePath        => Path.Combine(AppDataRoot, "msal_cache.bin");
    public static string AuthlibInjectorPath  => Path.Combine(JavaRoot,    "authlib-injector.jar");
    public static string SettingsPath         => Path.Combine(AppDataRoot, "settings.json");

    public static LauncherConfig? CurrentConfig { get; private set; }

    public static async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                AppDataRoot   = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AnchorLauncher");
                InstancesRoot = Path.Combine(AppDataRoot, "instances");
                AssetsRoot    = Path.Combine(AppDataRoot, "assets");
                JavaRoot      = Path.Combine(AppDataRoot, "java");
                LogsRoot      = Path.Combine(AppDataRoot, "logs");

                foreach (var dir in new[] { AppDataRoot, InstancesRoot, AssetsRoot, JavaRoot, LogsRoot })
                    Directory.CreateDirectory(dir);

                Debug.WriteLine($"[Storage] Initialized at: {AppDataRoot}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Storage] InitializeAsync failed: {ex}");
            }
        });
    }

    public static async Task<LauncherConfig?> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return null;

            var json = await File.ReadAllTextAsync(ConfigPath);
            var config = JsonSerializer.Deserialize<LauncherConfig>(json, _jsonOptions);
            CurrentConfig = config;
            Debug.WriteLine($"[Storage] Config loaded: Mode={config?.Mode}, FirstRun={config?.FirstRunComplete}");
            return config;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Storage] LoadConfigAsync failed: {ex}");
            return null;
        }
    }

    public static async Task SaveConfigAsync(LauncherConfig config)
    {
        try
        {
            if (string.IsNullOrEmpty(AppDataRoot))
                await InitializeAsync();

            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(ConfigPath, json);
            CurrentConfig = config;
            Debug.WriteLine($"[Storage] Config saved: Mode={config.Mode}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Storage] SaveConfigAsync failed: {ex}");
        }
    }

    // ── Global launch settings ──────────────────────────────────────────────

    public static async Task<GlobalSettings> LoadGlobalSettingsAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(AppDataRoot))
                await InitializeAsync();

            if (!File.Exists(SettingsPath))
                return new GlobalSettings();

            var json = await File.ReadAllTextAsync(SettingsPath);
            return JsonSerializer.Deserialize<GlobalSettings>(json, _jsonOptions) ?? new GlobalSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Storage] LoadGlobalSettingsAsync failed: {ex}");
            return new GlobalSettings();
        }
    }

    public static async Task SaveGlobalSettingsAsync(GlobalSettings settings)
    {
        await _settingsWriteLock.WaitAsync();
        try
        {
            if (string.IsNullOrEmpty(AppDataRoot))
                await InitializeAsync();

            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await File.WriteAllTextAsync(SettingsPath, json);
            Debug.WriteLine("[Storage] Global settings saved.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Storage] SaveGlobalSettingsAsync failed: {ex}");
        }
        finally { _settingsWriteLock.Release(); }
    }
}
