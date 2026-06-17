using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Git-style snapshot history for an instance's mods/ directory. Each snapshot is a
/// lightweight JSON manifest under <c>[instance]\snapshots\</c>; the actual jar bytes are
/// kept once in a content-addressed cache (<c>%APPDATA%\AnchorLauncher\modcache\{sha1}.jar</c>)
/// so Restore can resurrect files that were deleted since. Capped at 20 snapshots (FIFO).
/// </summary>
public class SnapshotService
{
    private const int MaxSnapshots = 20;
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static string ModCacheRoot => Path.Combine(LauncherStorageService.AppDataRoot, "modcache");
    private static string SnapshotsDir(MinecraftInstance i) => Path.Combine(i.GameDir, "snapshots");

    // ── Capture ─────────────────────────────────────────────────────────────────

    /// <summary>Records the current mods/ state. Skips silently when nothing changed since the last snapshot.</summary>
    public async Task<ModSnapshot?> TakeSnapshotAsync(MinecraftInstance instance, string reason, CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(async () =>
            {
                var modsDir = Path.Combine(instance.GameDir, "mods");
                var snapshot = new ModSnapshot { Reason = reason };

                if (Directory.Exists(modsDir))
                {
                    foreach (var file in Directory.EnumerateFiles(modsDir)
                                 .Where(f => f.EndsWith(".jar", OIC) || f.EndsWith(".jar.disabled", OIC)))
                    {
                        ct.ThrowIfCancellationRequested();
                        var enabled  = file.EndsWith(".jar", OIC);
                        var canonical = Path.GetFileName(file).Replace(".disabled", string.Empty, OIC);
                        var info     = new FileInfo(file);
                        var sha1     = await Net.DownloadHelper.ComputeSha1Async(file, ct);

                        snapshot.Mods.Add(new SnapshotModState
                        {
                            FileName = canonical, Size = info.Length, Sha1 = sha1, Enabled = enabled
                        });

                        // Content-addressed cache copy (idempotent) so Restore can recover deletions
                        Directory.CreateDirectory(ModCacheRoot);
                        var cached = Path.Combine(ModCacheRoot, sha1 + ".jar");
                        if (!File.Exists(cached))
                            File.Copy(file, cached);
                    }
                }

                // Skip no-op snapshots (identical to the most recent one)
                var existing = ListSnapshots(instance);
                if (existing.Count > 0 && SameState(existing[0], snapshot))
                    return null;

                var dir = SnapshotsDir(instance);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{snapshot.Timestamp:yyyyMMdd-HHmmss}-{snapshot.Id:N}.json");
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, _json), ct);

                // FIFO rotation
                var all = Directory.GetFiles(dir, "*.json").OrderBy(f => f).ToList();
                while (all.Count > MaxSnapshots)
                {
                    try { File.Delete(all[0]); } catch { }
                    all.RemoveAt(0);
                }

                Debug.WriteLine($"[Snapshot] '{reason}' captured ({snapshot.Mods.Count} mods).");
                return snapshot;
            }, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Snapshot] TakeSnapshotAsync failed: {ex.Message}");
            return null;
        }
    }

    // ── List ────────────────────────────────────────────────────────────────────

    /// <summary>Newest-first list with human change summaries computed against each predecessor.</summary>
    public List<ModSnapshot> ListSnapshots(MinecraftInstance instance)
    {
        var result = new List<ModSnapshot>();
        try
        {
            var dir = SnapshotsDir(instance);
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
            {
                try
                {
                    var snap = JsonSerializer.Deserialize<ModSnapshot>(File.ReadAllText(file), _json);
                    if (snap != null) result.Add(snap);
                }
                catch (Exception ex) { Debug.WriteLine($"[Snapshot] skipped '{file}': {ex.Message}"); }
            }

            // Summaries: diff each snapshot against the one before it (chronological order)
            for (int i = 0; i < result.Count; i++)
                result[i].ChangeSummary = Summarize(i > 0 ? result[i - 1] : null, result[i]);

            result.Reverse(); // newest first for the UI
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Snapshot] ListSnapshots failed: {ex.Message}");
        }
        return result;
    }

    // ── Restore ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-applies a recorded mods/ state: recovers missing files from the content cache,
    /// fixes enabled/disabled names, and disables files that weren't in the snapshot.
    /// Returns a plain-language result line.
    /// </summary>
    public Task<string> RestoreSnapshotAsync(MinecraftInstance instance, ModSnapshot snapshot) => Task.Run(() =>
    {
        int restored = 0, renamed = 0, disabledExtras = 0, missing = 0;
        try
        {
            var modsDir = Path.Combine(instance.GameDir, "mods");
            Directory.CreateDirectory(modsDir);

            var wanted = snapshot.Mods.ToDictionary(m => m.FileName, m => m, StringComparer.OrdinalIgnoreCase);

            // Pass 1: make every recorded mod exist with the right name
            foreach (var mod in snapshot.Mods)
            {
                var enabledPath  = Path.Combine(modsDir, mod.FileName);
                var disabledPath = enabledPath + ".disabled";
                var target       = mod.Enabled ? enabledPath : disabledPath;
                var other        = mod.Enabled ? disabledPath : enabledPath;

                if (File.Exists(target)) continue;

                if (File.Exists(other))
                {
                    File.Move(other, target);           // just the wrong enabled-state
                    renamed++;
                }
                else
                {
                    var cached = Path.Combine(ModCacheRoot, mod.Sha1 + ".jar");
                    if (File.Exists(cached))
                    {
                        File.Copy(cached, target);      // recovered from content cache
                        restored++;
                    }
                    else
                    {
                        missing++;                       // not recoverable locally
                        Debug.WriteLine($"[Snapshot] no cached copy for {mod.FileName} ({mod.Sha1}).");
                    }
                }
            }

            // Pass 2: disable anything present that the snapshot doesn't know about
            foreach (var file in Directory.EnumerateFiles(modsDir, "*.jar"))
            {
                var name = Path.GetFileName(file);
                if (!wanted.ContainsKey(name))
                {
                    var disabled = file + ".disabled";
                    if (File.Exists(disabled)) File.Delete(disabled);
                    File.Move(file, disabled);
                    disabledExtras++;
                }
            }

            var parts = new List<string>();
            if (restored > 0)       parts.Add($"{restored} recovered");
            if (renamed > 0)        parts.Add($"{renamed} re-toggled");
            if (disabledExtras > 0) parts.Add($"{disabledExtras} extras disabled");
            if (missing > 0)        parts.Add($"{missing} not recoverable (no cached copy)");
            return parts.Count == 0 ? "Already matches this snapshot." : $"Restored: {string.Join(", ", parts)}.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Snapshot] RestoreSnapshotAsync failed: {ex}");
            return $"Restore failed: {ex.Message}";
        }
    });

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static bool SameState(ModSnapshot a, ModSnapshot b)
    {
        if (a.Mods.Count != b.Mods.Count) return false;
        var byName = a.Mods.ToDictionary(m => m.FileName, StringComparer.OrdinalIgnoreCase);
        return b.Mods.All(m => byName.TryGetValue(m.FileName, out var o) &&
                               o.Sha1 == m.Sha1 && o.Enabled == m.Enabled);
    }

    private static string Summarize(ModSnapshot? previous, ModSnapshot current)
    {
        if (previous == null)
            return $"Initial state — {current.Mods.Count} mods";

        var prev = previous.Mods.ToDictionary(m => m.FileName, StringComparer.OrdinalIgnoreCase);
        var cur  = current.Mods.ToDictionary(m => m.FileName, StringComparer.OrdinalIgnoreCase);

        int added   = cur.Keys.Count(k => !prev.ContainsKey(k));
        int removed = prev.Keys.Count(k => !cur.ContainsKey(k));
        int toggled = cur.Count(kv => prev.TryGetValue(kv.Key, out var o) && o.Enabled != kv.Value.Enabled);
        int updated = cur.Count(kv => prev.TryGetValue(kv.Key, out var o) && o.Sha1 != kv.Value.Sha1);

        var parts = new List<string>();
        if (added > 0)   parts.Add($"{added} added");
        if (removed > 0) parts.Add($"{removed} removed");
        if (updated > 0) parts.Add($"{updated} updated");
        if (toggled > 0) parts.Add($"{toggled} toggled");
        return parts.Count == 0 ? "No mod changes" : string.Join(", ", parts);
    }
}
