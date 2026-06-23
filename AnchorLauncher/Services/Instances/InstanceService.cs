using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Instances;

/// <summary>
/// Owns the lifecycle of sandboxed instances on disk. Every instance is a folder
/// under <see cref="LauncherStorageService.InstancesRoot"/> containing an
/// <c>instance.json</c> descriptor plus the live <c>mods/</c>, <c>saves/</c>,
/// <c>config/</c>, etc. There is no central index — the folder set IS the source of
/// truth, which makes import (drop a folder) and export (zip a folder) lossless.
/// </summary>
public class InstanceService
{
    private const string DescriptorName = "instance.json";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<List<MinecraftInstance>> LoadAllAsync()
    {
        var results = new List<MinecraftInstance>();
        try
        {
            var root = LauncherStorageService.InstancesRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return results;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var descriptor = Path.Combine(dir, DescriptorName);
                if (!File.Exists(descriptor)) continue;

                try
                {
                    var json = await File.ReadAllTextAsync(descriptor);
                    var inst = JsonSerializer.Deserialize<MinecraftInstance>(json, _json);
                    if (inst == null) continue;

                    // Trust the on-disk location over any stale stored path
                    inst.GameDir = dir;
                    inst.Status  = InstanceStatus.Idle;
                    results.Add(inst);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[InstanceService] Skipped '{dir}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceService] LoadAllAsync failed: {ex}");
        }

        return results.OrderByDescending(i => i.LastPlayed ?? i.Created).ToList();
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<MinecraftInstance> CreateAsync(
        string name, string version, string versionType,
        ModLoaderType loader, string? loaderVersion)
    {
        var folder = ReserveFolder(name);
        Directory.CreateDirectory(folder);
        foreach (var sub in new[] { "mods", "saves", "config", "resourcepacks", "shaderpacks" })
            Directory.CreateDirectory(Path.Combine(folder, sub));

        var inst = new MinecraftInstance
        {
            Id               = Guid.NewGuid(),
            Name             = name.Trim(),
            Version          = version,
            VersionType      = versionType,
            ModLoader        = loader,
            ModLoaderVersion = loaderVersion,
            GameDir          = folder,
            Created          = DateTime.UtcNow
        };

        await SaveAsync(inst);
        Debug.WriteLine($"[InstanceService] Created '{inst.Name}' at {folder}");
        return inst;
    }

    public async Task SaveAsync(MinecraftInstance instance)
    {
        try
        {
            Directory.CreateDirectory(instance.GameDir);
            var descriptor = Path.Combine(instance.GameDir, DescriptorName);
            var json = JsonSerializer.Serialize(instance, _json);
            await File.WriteAllTextAsync(descriptor, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceService] SaveAsync failed: {ex}");
        }
    }

    // ── Clone (deep file copy) ──────────────────────────────────────────────────

    public async Task<MinecraftInstance> CloneAsync(MinecraftInstance source, string? newName = null)
    {
        var cloneName = newName?.Trim();
        if (string.IsNullOrWhiteSpace(cloneName))
            cloneName = $"{source.Name} (Copy)";

        var destFolder = ReserveFolder(cloneName);

        await Task.Run(() => CopyDirectory(source.GameDir, destFolder));

        var clone = new MinecraftInstance
        {
            Id               = Guid.NewGuid(),
            Name             = cloneName,
            Version          = source.Version,
            VersionType      = source.VersionType,
            ModLoader        = source.ModLoader,
            ModLoaderVersion = source.ModLoaderVersion,
            GameDir          = destFolder,
            ThumbnailPath    = source.ThumbnailPath,
            Created          = DateTime.UtcNow
        };

        await SaveAsync(clone); // overwrites the copied descriptor with fresh identity
        Debug.WriteLine($"[InstanceService] Cloned '{source.Name}' → '{clone.Name}'");
        return clone;
    }

    // ── Delete ──────────────────────────────────────────────────────────────────

    public Task DeleteAsync(MinecraftInstance instance) => Task.Run(() =>
    {
        try
        {
            if (Directory.Exists(instance.GameDir))
                Directory.Delete(instance.GameDir, recursive: true);
            Debug.WriteLine($"[InstanceService] Deleted '{instance.Name}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceService] DeleteAsync failed: {ex}");
        }
    });

    // ── Export / Import (.zip) ──────────────────────────────────────────────────

    public Task ExportAsync(MinecraftInstance instance, string zipPath) => Task.Run(() =>
    {
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(
                instance.GameDir, zipPath,
                CompressionLevel.Optimal, includeBaseDirectory: false);
            Debug.WriteLine($"[InstanceService] Exported '{instance.Name}' → {zipPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstanceService] ExportAsync failed: {ex}");
            throw;
        }
    });

    public async Task<MinecraftInstance> ImportAsync(string zipPath)
    {
        var temp = Path.Combine(Path.GetTempPath(), "anchor_import_" + Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, temp));

            // The descriptor may be at the zip root or one level down
            var descriptor = Path.Combine(temp, DescriptorName);
            if (!File.Exists(descriptor))
                descriptor = Directory.EnumerateFiles(temp, DescriptorName, SearchOption.AllDirectories)
                                      .FirstOrDefault()
                             ?? throw new InvalidOperationException(
                                 "Archive does not contain an instance.json descriptor.");

            var sourceFolder = Path.GetDirectoryName(descriptor)!;
            var json = await File.ReadAllTextAsync(descriptor);
            var inst = JsonSerializer.Deserialize<MinecraftInstance>(json, _json)
                       ?? throw new InvalidOperationException("Malformed instance descriptor.");

            var destFolder = ReserveFolder(inst.Name);
            await Task.Run(() => CopyDirectory(sourceFolder, destFolder));

            inst.Id      = Guid.NewGuid();
            inst.GameDir = destFolder;
            inst.Created = DateTime.UtcNow;
            await SaveAsync(inst);

            Debug.WriteLine($"[InstanceService] Imported '{inst.Name}' from {zipPath}");
            return inst;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Returns a unique, not-yet-created folder path for the given name.</summary>
    private static string ReserveFolder(string name)
    {
        var root = LauncherStorageService.InstancesRoot;
        Directory.CreateDirectory(root);

        var safe = Sanitize(name);
        var candidate = Path.Combine(root, safe);
        int n = 2;
        while (Directory.Exists(candidate))
            candidate = Path.Combine(root, $"{safe}_{n++}");
        return candidate;
    }

    private static string Sanitize(string name)
    {
        var cleaned = new string(name
            .Trim()
            .Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Instance" : cleaned;
    }

    private static void CopyDirectory(string sourceDir, string destDir, Func<string, bool>? includeRelative = null)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            if (includeRelative != null && !includeRelative(rel)) continue;
            var target = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Imports another launcher's instance by deep-copying its game directory into a fresh Anchor
    /// instance. <paramref name="includeRelative"/> can exclude heavy/non-portable folders — used for
    /// the official .minecraft, which holds shared versions/libraries/assets that don't belong to one
    /// instance.
    /// </summary>
    public async Task<MinecraftInstance> ImportFromGameDirAsync(
        string sourceGameDir, string name, string version, string versionType,
        ModLoaderType loader, string? loaderVersion, Func<string, bool>? includeRelative = null)
    {
        var destFolder = ReserveFolder(name);
        await Task.Run(() => CopyDirectory(sourceGameDir, destFolder, includeRelative));
        foreach (var sub in new[] { "mods", "saves", "config", "resourcepacks", "shaderpacks" })
            Directory.CreateDirectory(Path.Combine(destFolder, sub));

        var inst = new MinecraftInstance
        {
            Id               = Guid.NewGuid(),
            Name             = name.Trim(),
            Version          = version,
            VersionType      = versionType,
            ModLoader        = loader,
            ModLoaderVersion = loaderVersion,
            GameDir          = destFolder,
            Created          = DateTime.UtcNow
        };
        await SaveAsync(inst);
        Debug.WriteLine($"[InstanceService] Imported game dir '{sourceGameDir}' → '{inst.Name}'");
        return inst;
    }
}
