using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Launch;
using AnchorLauncher.Services.Net;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Installs mod-loader profiles into the shared versions store and stamps the instance's
/// <see cref="MinecraftInstance.LaunchVersionId"/>. Fabric/Quilt are resolved from their
/// meta servers (a ready-made version profile that inheritsFrom vanilla); Forge/NeoForge
/// download the official installer and run it headlessly.
/// </summary>
public class ModLoaderService
{
    private const string FabricMeta = "https://meta.fabricmc.net/v2";
    private const string QuiltMeta  = "https://meta.quiltmc.org/v3";
    private const string ForgeMaven = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private const string ForgeMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string ForgePromos = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string NeoMaven   = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    private const string NeoVersions = "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge";

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private static string VersionsRoot => Path.Combine(LauncherStorageService.AppDataRoot, "versions");

    private readonly JavaRuntimeService _java = new();

    /// <summary>Resolves and installs the loader for the instance. No-op for Vanilla.</summary>
    public async Task InstallAsync(
        MinecraftInstance instance, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        switch (instance.ModLoader)
        {
            case ModLoaderType.Vanilla:
                instance.LaunchVersionId = instance.Version;
                return;

            case ModLoaderType.Fabric:
                await InstallMetaProfileAsync(instance, FabricMeta, "Fabric", progress, ct);
                break;

            case ModLoaderType.Quilt:
                await InstallMetaProfileAsync(instance, QuiltMeta, "Quilt", progress, ct);
                break;

            case ModLoaderType.Forge:
                await InstallForgeFamilyAsync(instance, isNeo: false, progress, ct);
                break;

            case ModLoaderType.NeoForge:
                await InstallForgeFamilyAsync(instance, isNeo: true, progress, ct);
                break;
        }
    }

    // ── Available loader versions (for the create dialog) ───────────────────────

    /// <summary>Lists available loader builds for the given Minecraft version, newest first.</summary>
    public async Task<List<LoaderVersion>> GetLoaderVersionsAsync(
        ModLoaderType loader, string mcVersion, CancellationToken ct = default)
    {
        // Hard 10s ceiling so a slow/unresponsive loader API can't hang the dialog
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeoutCts.Token;

        try
        {
            return loader switch
            {
                ModLoaderType.Fabric   => await FetchMetaLoaderListAsync(FabricMeta, mcVersion, token),
                ModLoaderType.Quilt    => await FetchMetaLoaderListAsync(QuiltMeta, mcVersion, token),
                ModLoaderType.Forge    => await FetchForgeListAsync(mcVersion, token),
                ModLoaderType.NeoForge => await FetchNeoForgeListAsync(mcVersion, token),
                _                       => new List<LoaderVersion>()
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // superseded by a newer selection — let the caller ignore it
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"[ModLoader] {loader} version fetch timed out for {mcVersion}.");
            return new List<LoaderVersion>(); // empty → dialog shows the "try older version" message
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModLoader] GetLoaderVersionsAsync({loader}, {mcVersion}) failed: {ex.Message}");
            return new List<LoaderVersion>();
        }
    }

    private static async Task<List<LoaderVersion>> FetchMetaLoaderListAsync(
        string metaBase, string mcVersion, CancellationToken ct)
    {
        var json = await DownloadHelper.GetStringAsync($"{metaBase}/versions/loader/{mcVersion}", ct);
        using var doc = JsonDocument.Parse(json);

        var list = new List<LoaderVersion>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object ||
                !el.TryGetProperty("loader", out var loaderEl) ||
                !loaderEl.TryGetProperty("version", out var verEl)) continue;

            var version = verEl.GetString();
            if (string.IsNullOrEmpty(version)) continue;

            var stable = loaderEl.TryGetProperty("stable", out var s) && s.ValueKind == JsonValueKind.True;
            list.Add(new LoaderVersion(version, stable)); // meta returns newest first
        }
        return list;
    }

    private static async Task<List<LoaderVersion>> FetchForgeListAsync(string mcVersion, CancellationToken ct)
    {
        var recommended = await TryGetForgeRecommendedAsync(mcVersion, ct);

        var xml = await DownloadHelper.GetStringAsync(ForgeMetadata, ct);
        var doc = XDocument.Parse(xml);

        var prefix = mcVersion + "-";
        var versions = doc.Descendants("version")
            .Select(e => e.Value)
            .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
            .Select(v => v.Substring(prefix.Length))
            .ToList();

        versions.Reverse(); // maven-metadata is ascending → newest first
        return versions.Select(v => new LoaderVersion(v, v == recommended)).ToList();
    }

    private static async Task<List<LoaderVersion>> FetchNeoForgeListAsync(string mcVersion, CancellationToken ct)
    {
        var prefix = NeoPrefixFor(mcVersion);

        var json = await DownloadHelper.GetStringAsync(NeoVersions, ct);
        using var doc = JsonDocument.Parse(json);

        var versions = doc.RootElement.GetProperty("versions").EnumerateArray()
            .Select(e => e.GetString()!)
            .Where(v => v != null && v.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        versions.Reverse(); // newest first
        return versions.Select(v => new LoaderVersion(v, !v.Contains("beta", StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private static async Task<string?> TryGetForgeRecommendedAsync(string mc, CancellationToken ct)
    {
        try
        {
            var promosJson = await DownloadHelper.GetStringAsync(ForgePromos, ct);
            using var doc = JsonDocument.Parse(promosJson);
            var promos = doc.RootElement.GetProperty("promos");
            if (promos.TryGetProperty($"{mc}-recommended", out var rec)) return rec.GetString();
            if (promos.TryGetProperty($"{mc}-latest", out var lat)) return lat.GetString();
        }
        catch (Exception ex) { Debug.WriteLine($"[ModLoader] forge promos failed: {ex.Message}"); }
        return null;
    }

    // ── Fabric / Quilt ──────────────────────────────────────────────────────────

    private async Task InstallMetaProfileAsync(
        MinecraftInstance instance, string metaBase, string label,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        progress?.Report(DownloadProgress.At(10, $"Resolving {label} loader…"));

        // Resolve a loader version (use the pinned one, else newest available)
        var loaderVersion = instance.ModLoaderVersion;
        if (string.IsNullOrEmpty(loaderVersion))
        {
            var listJson = await DownloadHelper.GetStringAsync($"{metaBase}/versions/loader/{instance.Version}", ct);
            using var doc = JsonDocument.Parse(listJson);
            var first = doc.RootElement.EnumerateArray().FirstOrDefault();
            loaderVersion = first.ValueKind == JsonValueKind.Object &&
                            first.TryGetProperty("loader", out var loaderEl) &&
                            loaderEl.TryGetProperty("version", out var verEl)
                ? verEl.GetString()
                : throw new InvalidOperationException($"No {label} loader available for {instance.Version}.");
        }

        progress?.Report(DownloadProgress.At(45, $"Downloading {label} profile…"));

        // The meta server returns a complete version profile (inheritsFrom vanilla)
        var profileJson = await DownloadHelper.GetStringAsync(
            $"{metaBase}/versions/loader/{instance.Version}/{loaderVersion}/profile/json", ct);

        using var profileDoc = JsonDocument.Parse(profileJson);
        var profileId = profileDoc.RootElement.GetProperty("id").GetString()
                        ?? throw new InvalidOperationException($"{label} profile has no id.");

        var dest = Path.Combine(VersionsRoot, profileId, profileId + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await File.WriteAllTextAsync(dest, profileJson, ct);

        instance.ModLoaderVersion = loaderVersion;
        instance.LaunchVersionId  = profileId;

        progress?.Report(DownloadProgress.At(100, $"{label} {loaderVersion} installed."));
        Debug.WriteLine($"[ModLoader] {label} profile '{profileId}' installed.");
    }

    // ── Forge / NeoForge ────────────────────────────────────────────────────────

    private async Task InstallForgeFamilyAsync(
        MinecraftInstance instance, bool isNeo, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var label = isNeo ? "NeoForge" : "Forge";
        progress?.Report(DownloadProgress.At(8, $"Resolving {label} version…"));

        string version;
        string installerUrl;
        if (!string.IsNullOrEmpty(instance.ModLoaderVersion))
        {
            // Honor the version the user picked in the create dialog
            version      = instance.ModLoaderVersion!;
            installerUrl = isNeo ? NeoInstallerUrl(version) : ForgeInstallerUrl(instance.Version, version);
        }
        else
        {
            (version, installerUrl) = isNeo
                ? await ResolveNeoForgeAsync(instance.Version, ct)
                : await ResolveForgeAsync(instance.Version, ct);
            instance.ModLoaderVersion = version;
        }

        // 1) Download the official installer
        progress?.Report(DownloadProgress.At(25, $"Downloading {label} installer…"));
        var installer = Path.Combine(Path.GetTempPath(), $"anchor_{label}_{version}.jar");
        await DownloadHelper.DownloadFileAsync(installerUrl, installer, null, ct);

        // 2) The installer needs a Java runtime and a launcher_profiles.json in the target dir
        progress?.Report(DownloadProgress.At(45, "Preparing installer runtime…"));
        var javaExe = await _java.ResolveJavaAsync(17, progress, ct)
                      ?? throw new InvalidOperationException(string.Format(Platform.Loc.I["ml_e_java17"], label));

        EnsureLauncherProfilesStub();
        var before = SnapshotVersionDirs();

        // 3) Run headless client install against the shared store (acts as .minecraft)
        progress?.Report(DownloadProgress.At(60, $"Running {label} installer…"));
        var (exit, log) = await RunProcessAsync(
            javaExe,
            new[] { "-jar", installer, "--installClient", LauncherStorageService.AppDataRoot },
            ct);

        try { File.Delete(installer); } catch { }

        if (exit != 0)
            throw new InvalidOperationException(
                string.Format(Platform.Loc.I["ml_e_installerfail"], label, instance.Version, exit) +
                (string.IsNullOrWhiteSpace(log) ? "" : $"\n\n{LastLines(log, 8)}"));

        // 4) Detect the profile the installer produced
        var profileId = DetectNewProfile(before, instance.Version, isNeo)
                        ?? throw new InvalidOperationException(
                            string.Format(Platform.Loc.I["ml_e_noprofile"], label, instance.Version) +
                            (string.IsNullOrWhiteSpace(log) ? "" : $"\n\n{LastLines(log, 8)}"));

        instance.LaunchVersionId = profileId;
        progress?.Report(DownloadProgress.At(100, $"{label} {version} installed."));
        Debug.WriteLine($"[ModLoader] {label} profile '{profileId}' installed.");
    }

    private static async Task<(string Version, string InstallerUrl)> ResolveForgeAsync(string mc, CancellationToken ct)
    {
        var promosJson = await DownloadHelper.GetStringAsync(ForgePromos, ct);
        using var doc = JsonDocument.Parse(promosJson);
        var promos = doc.RootElement.GetProperty("promos");

        string? forge = null;
        if (promos.TryGetProperty($"{mc}-recommended", out var rec)) forge = rec.GetString();
        else if (promos.TryGetProperty($"{mc}-latest", out var lat)) forge = lat.GetString();

        if (string.IsNullOrEmpty(forge))
            throw new InvalidOperationException(string.Format(Platform.Loc.I["ml_e_noforge"], mc));

        return (forge, ForgeInstallerUrl(mc, forge));
    }

    private static async Task<(string Version, string InstallerUrl)> ResolveNeoForgeAsync(string mc, CancellationToken ct)
    {
        var prefix = NeoPrefixFor(mc);

        var versionsJson = await DownloadHelper.GetStringAsync(NeoVersions, ct);
        using var doc = JsonDocument.Parse(versionsJson);
        var all = doc.RootElement.GetProperty("versions").EnumerateArray()
                     .Select(e => e.GetString()!).Where(v => v != null);

        var match = all.Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
                       .OrderBy(v => v, StringComparer.Ordinal)
                       .LastOrDefault()
                    ?? throw new InvalidOperationException(string.Format(Platform.Loc.I["ml_e_noneoforge"], mc));

        return (match, NeoInstallerUrl(match));
    }

    private static string ForgeInstallerUrl(string mc, string forge)
        => $"{ForgeMaven}/{mc}-{forge}/forge-{mc}-{forge}-installer.jar";

    private static string NeoInstallerUrl(string neo)
        => $"{NeoMaven}/{neo}/neoforge-{neo}-installer.jar";

    /// <summary>Maps a Minecraft version to the NeoForge version prefix (1.20.4 → "20.4.", 1.21 → "21.0.", 26.1.2 → "26.1.").</summary>
    private static string NeoPrefixFor(string mc)
    {
        // Standard MC scheme: strip the leading "1." → 1.20.4 → "20.4.", 1.21 → "21.0."
        var m = System.Text.RegularExpressions.Regex.Match(mc, "^1\\.(\\d+)(?:\\.(\\d+))?$");
        if (m.Success)
        {
            var minor = m.Groups[1].Value;
            var patch = m.Groups[2].Success ? m.Groups[2].Value : "0";
            return $"{minor}.{patch}.";
        }

        // Newer / non-standard ids (e.g. 26.1.2): use the first two numeric segments as-is
        var parts = mc.Split('.');
        if (parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            return $"{parts[0]}.{parts[1]}.";
        if (parts.Length >= 1 && int.TryParse(parts[0], out _))
            return $"{parts[0]}.";
        return mc; // last resort — filter will simply yield no matches
    }

    private static void EnsureLauncherProfilesStub()
    {
        var path = Path.Combine(LauncherStorageService.AppDataRoot, "launcher_profiles.json");
        if (!File.Exists(path))
            File.WriteAllText(path, "{\"profiles\":{},\"version\":3}");
    }

    private static HashSet<string> SnapshotVersionDirs()
    {
        if (!Directory.Exists(VersionsRoot)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateDirectories(VersionsRoot)
                        .Select(Path.GetFileName)
                        .Where(n => n != null)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }

    private static string? DetectNewProfile(HashSet<string> before, string mc, bool isNeo)
    {
        if (!Directory.Exists(VersionsRoot)) return null;

        var keyword = isNeo ? "neoforge" : "forge";
        var candidates = Directory.EnumerateDirectories(VersionsRoot)
            .Select(Path.GetFileName)
            .Where(n => n != null && !before.Contains(n!) &&
                        n!.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Prefer a profile that also references the MC version; else any new loader profile
        return candidates.FirstOrDefault(n => n!.Contains(mc, StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    /// <summary>Runs a process, capturing the tail of its combined output so a failure can report why.</summary>
    private static Task<(int Exit, string Output)> RunProcessAsync(string exe, string[] args, CancellationToken ct) => Task.Run(() =>
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var tail = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Capture(string? line)
        {
            if (line == null) return;
            Debug.WriteLine($"[Installer] {line}");
            tail.Enqueue(line);
            while (tail.Count > 40) tail.TryDequeue(out _);
        }

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("Failed to start installer process.");
        proc.OutputDataReceived += (_, e) => Capture(e.Data);
        proc.ErrorDataReceived  += (_, e) => Capture(e.Data);
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        while (!proc.WaitForExit(500))
        {
            if (ct.IsCancellationRequested)
            {
                try { proc.Kill(true); } catch { }
                throw new OperationCanceledException(ct);
            }
        }
        return (proc.ExitCode, string.Join("\n", tail));
    }, ct);

    private static string LastLines(string text, int n)
    {
        var lines = text.Split('\n');
        return lines.Length <= n ? text : string.Join("\n", lines[^n..]);
    }
}
