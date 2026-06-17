using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Models.Marketplace;
using AnchorLauncher.Services.Marketplace;
using AnchorLauncher.Services.Net;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Instance version switching: plans per-mod compatibility against the Modrinth hash index,
/// executes the switch (update / disable / re-resolve loader profile), and supports a full
/// atomic undo from a recorded version snapshot.
/// </summary>
public class VersionSwitchService
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly ModrinthClient   _modrinth  = new();
    private readonly SnapshotService  _snapshots = new();
    private readonly ModLoaderService _loaders   = new();
    private readonly InstanceService  _instances = new();

    private static string SnapshotDir(MinecraftInstance i) => Path.Combine(i.GameDir, "version_snapshots");

    // ── Plan ────────────────────────────────────────────────────────────────────

    /// <summary>Builds the per-mod plan for moving the instance to <paramref name="newVersion"/>.</summary>
    public async Task<List<SwitchPlanEntry>> PlanAsync(
        MinecraftInstance instance, string newVersion,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var plan    = new List<SwitchPlanEntry>();
        var modsDir = Path.Combine(instance.GameDir, "mods");
        if (!Directory.Exists(modsDir)) return plan;

        var loader = LoaderName(instance.ModLoader);
        var jars   = Directory.EnumerateFiles(modsDir, "*.jar").ToList();

        for (int i = 0; i < jars.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var jar   = jars[i];
            var entry = new SwitchPlanEntry { FileName = Path.GetFileName(jar) };
            plan.Add(entry);

            progress?.Report(DownloadProgress.At(
                100.0 * i / Math.Max(1, jars.Count), $"Checking {entry.FileName} ({i + 1}/{jars.Count})…"));

            try
            {
                entry.Sha1      = await DownloadHelper.ComputeSha1Async(jar, ct);
                entry.ProjectId = await _modrinth.LookupProjectIdByHashAsync(entry.Sha1, ct);

                if (entry.ProjectId == null)
                {
                    entry.Status = SwitchModStatus.Unknown;
                    continue;
                }

                var file = await _modrinth.ResolveDownloadAsync(
                    entry.ProjectId, newVersion, loader, ProjectType.Mod, ct);

                // ResolveDownloadAsync falls back to "any version" — only count an exact
                // game-version match as compatible, so query strictly first:
                var strict = await ResolveStrictAsync(entry.ProjectId, newVersion, loader, ct);
                if (strict != null)
                {
                    entry.Status      = SwitchModStatus.UpdateAvailable;
                    entry.NewFileName = strict.Value.FileName;
                    entry.NewUrl      = strict.Value.Url;
                }
                else
                {
                    entry.Status = SwitchModStatus.Incompatible;
                    _ = file; // (unfiltered fallback intentionally not used for switching)
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VersionSwitch] plan failed for {entry.FileName}: {ex.Message}");
                entry.Status = SwitchModStatus.Unknown;
            }
        }

        progress?.Report(DownloadProgress.At(100, "Compatibility check complete."));
        return plan;
    }

    /// <summary>Strict resolve: only succeeds when a file exists for exactly this game version.</summary>
    private async Task<(string FileName, string Url)?> ResolveStrictAsync(
        string projectId, string gameVersion, string? loader, CancellationToken ct)
    {
        try
        {
            var qs = "game_versions=" + Uri.EscapeDataString($"[\"{gameVersion}\"]");
            if (!string.IsNullOrEmpty(loader)) qs += "&loaders=" + Uri.EscapeDataString($"[\"{loader}\"]");

            var body = await DownloadHelper.GetStringAsync(
                $"https://api.modrinth.com/v2/project/{projectId}/version?{qs}", ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var files = doc.RootElement[0].GetProperty("files");
            if (files.GetArrayLength() == 0) return null;

            var chosen = files[0];
            foreach (var f in files.EnumerateArray())
                if (f.TryGetProperty("primary", out var p) && p.GetBoolean()) { chosen = f; break; }

            return (chosen.GetProperty("filename").GetString()!, chosen.GetProperty("url").GetString()!);
        }
        catch { return null; }
    }

    // ── Execute ─────────────────────────────────────────────────────────────────

    public async Task ExecuteAsync(
        MinecraftInstance instance, string newVersion, List<SwitchPlanEntry> plan,
        Action<string> onLog, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var modsDir = Path.Combine(instance.GameDir, "mods");
        Directory.CreateDirectory(modsDir);

        // 1) Automatic mod snapshot (rollback history) …
        onLog($"[Anchor] Switching {instance.Name}: {instance.Version} → {newVersion}");
        await _snapshots.TakeSnapshotAsync(instance, $"Before version switch to {newVersion}", ct);

        // 2) … plus the dedicated version snapshot for Undo Last Switch
        var versionSnap = new VersionSnapshot
        {
            PreviousVersion         = instance.Version,
            PreviousLoaderVersion   = instance.ModLoaderVersion,
            PreviousLaunchVersionId = instance.LaunchVersionId,
            PreviousVersionType     = instance.VersionType
        };
        foreach (var entry in plan)
            versionSnap.ModStates.Add(new SnapshotModState
            { FileName = entry.FileName, Sha1 = entry.Sha1, Enabled = true });

        Directory.CreateDirectory(SnapshotDir(instance));
        var snapPath = Path.Combine(SnapshotDir(instance), $"{versionSnap.Timestamp:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(snapPath, JsonSerializer.Serialize(versionSnap, _json), ct);

        // 3) Apply the plan
        var updates = plan.Where(p => p.Status == SwitchModStatus.UpdateAvailable).ToList();
        for (int i = 0; i < updates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var u = updates[i];
            progress?.Report(DownloadProgress.At(
                70.0 * i / Math.Max(1, updates.Count), $"Updating {u.FileName}…"));
            onLog($"[Anchor] Updating {u.FileName} → {u.NewFileName}");

            await DownloadHelper.DownloadFileAsync(u.NewUrl!, Path.Combine(modsDir, u.NewFileName!), null, ct);

            var old = Path.Combine(modsDir, u.FileName);
            if (!string.Equals(u.FileName, u.NewFileName, OIC) && File.Exists(old))
                File.Delete(old);
        }

        foreach (var d in plan.Where(p => p.Status != SwitchModStatus.UpdateAvailable))
        {
            var path = Path.Combine(modsDir, d.FileName);
            if (File.Exists(path))
            {
                var disabled = path + ".disabled";
                if (File.Exists(disabled)) File.Delete(disabled);
                File.Move(path, disabled);
                onLog($"[Anchor] Disabled {d.FileName} (no compatible build for {newVersion}).");
            }
        }

        // 4) Re-target the instance + re-resolve the loader profile for the new version
        progress?.Report(DownloadProgress.At(75, "Reinstalling mod loader for the new version…"));
        instance.Version          = newVersion;
        instance.VersionType      = "release";
        instance.ModLoaderVersion = null;   // re-resolve latest stable for the new MC version
        await _loaders.InstallAsync(instance, progress, ct);

        await _instances.SaveAsync(instance);
        RefreshSwitchBadge(instance);
        progress?.Report(DownloadProgress.At(100, "Version switch complete."));
        onLog($"[Anchor] {instance.Name} is now on {newVersion}.");
    }

    // ── Undo ────────────────────────────────────────────────────────────────────

    public VersionSnapshot? GetLastSwitch(MinecraftInstance instance)
    {
        try
        {
            var dir = SnapshotDir(instance);
            if (!Directory.Exists(dir)) return null;

            var latest = Directory.GetFiles(dir, "*.json").OrderByDescending(f => f).FirstOrDefault();
            if (latest == null) return null;
            return JsonSerializer.Deserialize<VersionSnapshot>(File.ReadAllText(latest), _json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VersionSwitch] GetLastSwitch failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Restores version, loader profile and the recorded mod set atomically.</summary>
    public async Task<string> UndoLastSwitchAsync(MinecraftInstance instance, Action<string> onLog, CancellationToken ct = default)
    {
        var snap = GetLastSwitch(instance);
        if (snap == null) return "No version switch to undo.";

        onLog($"[Anchor] Undoing switch: {instance.Version} → {snap.PreviousVersion}");

        // Mods back to the recorded state (content cache recovers replaced/deleted files)
        var modSnapshot = new ModSnapshot { Mods = snap.ModStates, Reason = "Version switch undo" };
        var result = await _snapshots.RestoreSnapshotAsync(instance, modSnapshot);
        onLog($"[Anchor] {result}");

        instance.Version          = snap.PreviousVersion;
        instance.VersionType      = snap.PreviousVersionType;
        instance.ModLoaderVersion = snap.PreviousLoaderVersion;
        instance.LaunchVersionId  = snap.PreviousLaunchVersionId;
        await _instances.SaveAsync(instance);

        // The undone snapshot is consumed
        try
        {
            var latest = Directory.GetFiles(SnapshotDir(instance), "*.json").OrderByDescending(f => f).FirstOrDefault();
            if (latest != null) File.Delete(latest);
        }
        catch (Exception ex) { Debug.WriteLine($"[VersionSwitch] snapshot cleanup failed: {ex.Message}"); }

        RefreshSwitchBadge(instance);
        onLog($"[Anchor] {instance.Name} restored to {snap.PreviousVersion}.");
        return $"Restored to {snap.PreviousVersion}.";
    }

    // ── Badge ───────────────────────────────────────────────────────────────────

    /// <summary>Updates the card's "Switched from X" badge state from disk.</summary>
    public void RefreshSwitchBadge(MinecraftInstance instance)
    {
        var snap = GetLastSwitch(instance);
        instance.HasSwitchSnapshot = snap != null;
        instance.SwitchedFromLabel = snap != null ? $"Switched from {snap.PreviousVersion}" : null;
    }

    private static string? LoaderName(ModLoaderType loader) => loader switch
    {
        ModLoaderType.Fabric   => "fabric",
        ModLoaderType.Forge    => "forge",
        ModLoaderType.NeoForge => "neoforge",
        ModLoaderType.Quilt    => "quilt",
        _                      => null
    };
}
