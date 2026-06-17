using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using AnchorLauncher.Models.Instances;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Reads and mutates the on-disk content of a single instance: mods (enable/disable via the
/// .disabled suffix), resource packs (list/reorder/delete), and worlds (list/backup/delete).
/// </summary>
public class InstanceContentService
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    // ── Mods ────────────────────────────────────────────────────────────────────

    public List<ModEntry> ListMods(string gameDir)
    {
        var dir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(dir)) return new List<ModEntry>();

        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".jar", OIC) || f.EndsWith(".jar.disabled", OIC))
            .Select(f => new ModEntry
            {
                FullPath  = f,
                FileName  = Path.GetFileName(f).Replace(".disabled", string.Empty, OIC),
                IsEnabled = f.EndsWith(".jar", OIC)
            })
            .OrderBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Renames a mod file to add/remove the .disabled suffix and updates the entry.</summary>
    public void SetModEnabled(ModEntry mod, bool enabled)
    {
        try
        {
            var current      = mod.FullPath;
            var enabledPath  = current.EndsWith(".disabled", OIC) ? current[..^".disabled".Length] : current;
            var disabledPath = enabledPath + ".disabled";
            var target       = enabled ? enabledPath : disabledPath;

            if (!string.Equals(current, target, OIC) && File.Exists(current))
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(current, target);
            }

            mod.FullPath  = target;
            mod.IsEnabled = enabled;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Content] SetModEnabled failed: {ex.Message}");
        }
    }

    public void DeleteMod(ModEntry mod)
    {
        try { if (File.Exists(mod.FullPath)) File.Delete(mod.FullPath); }
        catch (Exception ex) { Debug.WriteLine($"[Content] DeleteMod failed: {ex.Message}"); }
    }

    // ── Resource packs ──────────────────────────────────────────────────────────

    public List<ResourcePackEntry> ListResourcePacks(string gameDir)
    {
        var dir = Path.Combine(gameDir, "resourcepacks");
        if (!Directory.Exists(dir)) return new List<ResourcePackEntry>();

        var packs = new List<ResourcePackEntry>();
        foreach (var folder in Directory.EnumerateDirectories(dir))
            packs.Add(new ResourcePackEntry { Name = Path.GetFileName(folder), FullPath = folder, IsFolder = true });
        foreach (var zip in Directory.EnumerateFiles(dir, "*.zip"))
            packs.Add(new ResourcePackEntry { Name = Path.GetFileName(zip), FullPath = zip, IsFolder = false });

        return packs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void DeleteResourcePack(ResourcePackEntry pack)
    {
        try
        {
            if (pack.IsFolder && Directory.Exists(pack.FullPath)) Directory.Delete(pack.FullPath, true);
            else if (File.Exists(pack.FullPath)) File.Delete(pack.FullPath);
        }
        catch (Exception ex) { Debug.WriteLine($"[Content] DeleteResourcePack failed: {ex.Message}"); }
    }

    /// <summary>Persists the priority order to options.txt (top of the list = highest priority in-game).</summary>
    public void SaveResourcePackOrder(string gameDir, IEnumerable<ResourcePackEntry> orderedTopFirst)
    {
        try
        {
            // options.txt expects lowest-priority first, "vanilla" always at the bottom-most base
            var entries = orderedTopFirst.Reverse().Select(p => $"\"file/{p.Name}\"");
            var value   = "resourcePacks:[\"vanilla\"," + string.Join(",", entries) + "]";
            // (if no packs, still write a clean vanilla-only line)
            if (!orderedTopFirst.Any()) value = "resourcePacks:[\"vanilla\"]";

            var path  = Path.Combine(gameDir, "options.txt");
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            var idx   = lines.FindIndex(l => l.StartsWith("resourcePacks:", OIC));
            if (idx >= 0) lines[idx] = value; else lines.Add(value);
            File.WriteAllLines(path, lines);
        }
        catch (Exception ex) { Debug.WriteLine($"[Content] SaveResourcePackOrder failed: {ex.Message}"); }
    }

    // ── Shaderpacks ─────────────────────────────────────────────────────────────

    /// <summary>Lists shaderpacks (zips or folders); .disabled suffix marks disabled packs.</summary>
    public List<ModEntry> ListShaderpacks(string gameDir)
    {
        var dir = Path.Combine(gameDir, "shaderpacks");
        if (!Directory.Exists(dir)) return new List<ModEntry>();

        var entries = new List<ModEntry>();

        foreach (var f in Directory.EnumerateFiles(dir)
                     .Where(f => f.EndsWith(".zip", OIC) || f.EndsWith(".zip.disabled", OIC)))
        {
            entries.Add(new ModEntry
            {
                FullPath  = f,
                FileName  = Path.GetFileName(f).Replace(".disabled", string.Empty, OIC),
                IsEnabled = !f.EndsWith(".disabled", OIC)
            });
        }
        foreach (var d in Directory.EnumerateDirectories(dir))
        {
            entries.Add(new ModEntry
            {
                FullPath  = d,
                FileName  = Path.GetFileName(d).Replace(".disabled", string.Empty, OIC),
                IsEnabled = !d.EndsWith(".disabled", OIC)
            });
        }

        return entries.OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Renames a shaderpack (file or folder) to add/remove the .disabled suffix.</summary>
    public void SetShaderpackEnabled(ModEntry pack, bool enabled)
    {
        try
        {
            var current      = pack.FullPath;
            var enabledPath  = current.EndsWith(".disabled", OIC) ? current[..^".disabled".Length] : current;
            var disabledPath = enabledPath + ".disabled";
            var target       = enabled ? enabledPath : disabledPath;
            if (string.Equals(current, target, OIC)) { pack.IsEnabled = enabled; return; }

            if (File.Exists(current))
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(current, target);
            }
            else if (Directory.Exists(current))
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(current, target);
            }

            pack.FullPath  = target;
            pack.IsEnabled = enabled;
        }
        catch (Exception ex) { Debug.WriteLine($"[Content] SetShaderpackEnabled failed: {ex.Message}"); }
    }

    /// <summary>Copies a dropped .zip into the instance's shaderpacks/ folder.</summary>
    public void InstallShaderpack(string gameDir, string sourceZip)
    {
        var dir = Path.Combine(gameDir, "shaderpacks");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Path.GetFileName(sourceZip));
        File.Copy(sourceZip, dest, overwrite: true);
        Debug.WriteLine($"[Content] Shaderpack installed: {dest}");
    }

    public void DeleteShaderpack(ModEntry pack)
    {
        try
        {
            if (File.Exists(pack.FullPath)) File.Delete(pack.FullPath);
            else if (Directory.Exists(pack.FullPath)) Directory.Delete(pack.FullPath, true);
        }
        catch (Exception ex) { Debug.WriteLine($"[Content] DeleteShaderpack failed: {ex.Message}"); }
    }

    // ── Worlds ──────────────────────────────────────────────────────────────────

    public List<WorldSaveEntry> ListSaves(string gameDir)
    {
        var dir = Path.Combine(gameDir, "saves");
        if (!Directory.Exists(dir)) return new List<WorldSaveEntry>();

        return Directory.EnumerateDirectories(dir).Select(d =>
        {
            var icon = Path.Combine(d, "icon.png");
            return new WorldSaveEntry
            {
                Name     = Path.GetFileName(d),
                FullPath = d,
                IconPath = File.Exists(icon) ? icon : null
            };
        })
        .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    public Task BackupWorldAsync(WorldSaveEntry world, string gameDir) => Task.Run(() =>
    {
        try
        {
            var backups = Path.Combine(gameDir, "backups");
            Directory.CreateDirectory(backups);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var zip   = Path.Combine(backups, $"{world.Name}-{stamp}.zip");
            ZipFile.CreateFromDirectory(world.FullPath, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
            Debug.WriteLine($"[Content] World '{world.Name}' backed up → {zip}");
        }
        catch (Exception ex) { Debug.WriteLine($"[Content] BackupWorldAsync failed: {ex.Message}"); }
    });

    public void DeleteWorld(WorldSaveEntry world)
    {
        try { if (Directory.Exists(world.FullPath)) Directory.Delete(world.FullPath, true); }
        catch (Exception ex) { Debug.WriteLine($"[Content] DeleteWorld failed: {ex.Message}"); }
    }

    // ── Shared ──────────────────────────────────────────────────────────────────

    public void OpenInExplorer(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Debug.WriteLine($"[Content] OpenInExplorer failed: {ex.Message}"); }
    }
}
