using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnchorLauncher.Models.Diagnostics;
using AnchorLauncher.Models.Instances;

namespace AnchorLauncher.Services.Diagnostics;

/// <summary>
/// Pre-launch conflict and load-order check. Reads each enabled mod jar's metadata
/// (fabric.mod.json / quilt.mod.json / META-INF/mods.toml), builds the dependency graph,
/// and reports missing dependencies, version mismatches and declared incompatibilities in
/// plain English. Results are cached per mods-folder state hash, so the scan only re-runs
/// when the mod set actually changes.
/// </summary>
public class ModScannerService
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    // Loader/platform ids that are provided by the runtime, not by jars in mods/
    private static readonly HashSet<string> BuiltinIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "forge", "neoforge", "fabricloader", "fabric-loader",
        "quilt_loader", "quilt_base", "mixin", "mixinextras"
    };

    private static readonly Dictionary<string, List<ModConflict>> _cache = new();

    private sealed class ModMeta
    {
        public string FileName = string.Empty;
        public string ModId    = string.Empty;
        public string Version  = string.Empty;
        public Dictionary<string, string> Depends = new(StringComparer.OrdinalIgnoreCase); // id → version constraint
        public List<string> Breaks = new();
    }

    public Task<List<ModConflict>> ScanAsync(MinecraftInstance instance) => Task.Run(() =>
    {
        try
        {
            var modsDir = Path.Combine(instance.GameDir, "mods");
            if (!Directory.Exists(modsDir)) return new List<ModConflict>();

            var jars = Directory.EnumerateFiles(modsDir, "*.jar").OrderBy(f => f).ToList();
            if (jars.Count == 0) return new List<ModConflict>();

            // Cache: keyed by instance + a fingerprint of the enabled jar set
            var key = instance.Id + "|" + StateHash(jars);
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var cached)) return cached;
            }

            var metas = jars.Select(ReadMeta).Where(m => m != null).Cast<ModMeta>().ToList();
            var provided = new Dictionary<string, ModMeta>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in metas.Where(m => !string.IsNullOrEmpty(m.ModId)))
                provided.TryAdd(m.ModId, m);

            // Dependents map: how many installed mods depend on each mod id (drives Auto-Fix choice)
            var dependents = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in metas)
                foreach (var depId in m.Depends.Keys)
                    if (!BuiltinIds.Contains(depId))
                        dependents[depId] = dependents.GetValueOrDefault(depId) + 1;

            int DependentsOf(ModMeta m) => dependents.GetValueOrDefault(m.ModId);

            var conflicts = new List<ModConflict>();

            foreach (var mod in metas)
            {
                // Missing dependencies + version mismatches
                foreach (var (depId, constraint) in mod.Depends)
                {
                    if (BuiltinIds.Contains(depId)) continue;

                    if (!provided.TryGetValue(depId, out var dep))
                    {
                        // "fabric" historically aliases fabric-api
                        if (depId.Equals("fabric", OIC) && provided.ContainsKey("fabric-api")) continue;

                        conflicts.Add(new ModConflict
                        {
                            Severity = ConflictSeverity.Error,
                            Kind     = ConflictKind.MissingDependency,
                            PrimaryFile = mod.FileName,
                            PrimaryDependents = DependentsOf(mod),
                            Message  = $"{Display(mod)} requires '{depId}'" +
                                       (IsAnyVersion(constraint) ? "" : $" version {constraint}") +
                                       ", which is not installed."
                        });
                    }
                    else if (!IsAnyVersion(constraint) && !VersionSatisfies(dep.Version, constraint))
                    {
                        conflicts.Add(new ModConflict
                        {
                            Severity = ConflictSeverity.Warning,
                            Kind     = ConflictKind.VersionMismatch,
                            PrimaryFile = mod.FileName,
                            PrimaryDependents = DependentsOf(mod),
                            Message  = $"{Display(mod)} requires '{depId}' version {constraint} (you have {dep.Version})."
                        });
                    }
                }

                // Declared incompatibilities
                foreach (var brokenId in mod.Breaks)
                {
                    if (provided.TryGetValue(brokenId, out var victim))
                        conflicts.Add(new ModConflict
                        {
                            Severity = ConflictSeverity.Error,
                            Kind     = ConflictKind.Incompatible,
                            PrimaryFile         = mod.FileName,
                            PrimaryDependents   = DependentsOf(mod),
                            SecondaryFile       = victim.FileName,
                            SecondaryDependents = DependentsOf(victim),
                            Message  = $"{Display(mod)} is incompatible with {Display(victim)} — they cannot run together."
                        });
                }
            }

            lock (_cache) { _cache[key] = conflicts; }
            Debug.WriteLine($"[ModScan] {jars.Count} jars scanned, {conflicts.Count} findings.");
            return conflicts;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModScan] ScanAsync failed: {ex}");
            return new List<ModConflict>(); // never block a launch on a scanner failure
        }
    });

    // ── Metadata extraction ─────────────────────────────────────────────────────

    private static ModMeta? ReadMeta(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);

            var fabric = zip.GetEntry("fabric.mod.json") ?? zip.GetEntry("quilt.mod.json");
            if (fabric != null)
                return ParseFabricJson(ReadEntry(fabric), Path.GetFileName(jarPath));

            var toml = zip.GetEntry("META-INF/mods.toml") ?? zip.GetEntry("META-INF/neoforge.mods.toml");
            if (toml != null)
                return ParseModsToml(ReadEntry(toml), Path.GetFileName(jarPath));

            return null; // not a recognized mod format (e.g. plain library jar) — skip silently
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModScan] could not read {Path.GetFileName(jarPath)}: {ex.Message}");
            return null;
        }
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static ModMeta? ParseFabricJson(string json, string fileName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;

            // quilt.mod.json nests under quilt_loader
            if (root.TryGetProperty("quilt_loader", out var quilt)) root = quilt;

            var meta = new ModMeta
            {
                FileName = fileName,
                ModId    = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                Version  = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : ""
            };

            if (root.TryGetProperty("depends", out var dep))
                ReadDependsElement(dep, meta.Depends);

            if (root.TryGetProperty("breaks", out var brk))
            {
                if (brk.ValueKind == JsonValueKind.Object)
                    foreach (var p in brk.EnumerateObject()) meta.Breaks.Add(p.Name);
                else if (brk.ValueKind == JsonValueKind.Array)
                    foreach (var e in brk.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var bid))
                            meta.Breaks.Add(bid.GetString() ?? "");
            }

            return string.IsNullOrEmpty(meta.ModId) ? null : meta;
        }
        catch { return null; }
    }

    private static void ReadDependsElement(JsonElement dep, Dictionary<string, string> into)
    {
        if (dep.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in dep.EnumerateObject())
            {
                var constraint = p.Value.ValueKind switch
                {
                    JsonValueKind.String => p.Value.GetString() ?? "*",
                    JsonValueKind.Array  => p.Value.EnumerateArray().FirstOrDefault().GetString() ?? "*",
                    _                    => "*"
                };
                into.TryAdd(p.Name, constraint);
            }
        }
        else if (dep.ValueKind == JsonValueKind.Array) // quilt style: [{ "id": "...", "versions": "..." }]
        {
            foreach (var e in dep.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String) { into.TryAdd(e.GetString() ?? "", "*"); continue; }
                if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("id", out var id)) continue;
                var versions = e.TryGetProperty("versions", out var ver) && ver.ValueKind == JsonValueKind.String
                    ? ver.GetString() ?? "*" : "*";
                var optional = e.TryGetProperty("optional", out var opt) && opt.ValueKind == JsonValueKind.True;
                if (!optional) into.TryAdd(id.GetString() ?? "", versions);
            }
        }
    }

    private static ModMeta? ParseModsToml(string toml, string fileName)
    {
        try
        {
            // First [[mods]] block gives the mod's own identity
            var idMatch  = Regex.Match(toml, @"\[\[mods\]\][^\[]*?modId\s*=\s*""([^""]+)""", RegexOptions.Singleline);
            var verMatch = Regex.Match(toml, @"\[\[mods\]\][^\[]*?version\s*=\s*""([^""]+)""", RegexOptions.Singleline);
            if (!idMatch.Success) return null;

            var meta = new ModMeta
            {
                FileName = fileName,
                ModId    = idMatch.Groups[1].Value,
                Version  = verMatch.Success ? verMatch.Groups[1].Value : ""
            };
            if (meta.Version.Contains("${")) meta.Version = ""; // unsubstituted gradle token

            // [[dependencies.<ownId>]] blocks
            foreach (Match block in Regex.Matches(toml,
                @"\[\[dependencies\.[^\]]+\]\](?<body>(?:(?!\[\[).)*)", RegexOptions.Singleline))
            {
                var body = block.Groups["body"].Value;
                var depId = Regex.Match(body, @"modId\s*=\s*""([^""]+)""");
                if (!depId.Success) continue;

                var mandatory = !Regex.IsMatch(body, @"mandatory\s*=\s*false") &&
                                !Regex.IsMatch(body, @"type\s*=\s*""optional""");
                if (!mandatory) continue;

                var range = Regex.Match(body, @"versionRange\s*=\s*""([^""]+)""");
                meta.Depends.TryAdd(depId.Groups[1].Value, range.Success ? range.Groups[1].Value : "*");
            }

            return meta;
        }
        catch { return null; }
    }

    // ── Version constraint evaluation (best-effort semver / maven ranges) ───────

    private static bool IsAnyVersion(string c) =>
        string.IsNullOrWhiteSpace(c) || c == "*" || c.Equals("any", OIC);

    private static bool VersionSatisfies(string actual, string constraint)
    {
        if (string.IsNullOrEmpty(actual)) return true; // unknown actual — don't cry wolf

        try
        {
            constraint = constraint.Trim();

            // Maven range: [1.0,2.0) / [1.0,) / (,2.0]
            if (constraint.StartsWith('[') || constraint.StartsWith('('))
            {
                var inner = constraint.TrimStart('[', '(').TrimEnd(']', ')');
                var parts = inner.Split(',');
                var lowOk  = parts.Length < 1 || string.IsNullOrEmpty(parts[0]) ||
                             Compare(actual, parts[0]) >= (constraint.StartsWith('[') ? 0 : 1);
                var highOk = parts.Length < 2 || string.IsNullOrEmpty(parts[1].Trim()) ||
                             Compare(actual, parts[1].Trim()) <= (constraint.EndsWith(']') ? 0 : -1);
                return lowOk && highOk;
            }

            // Semver-style prefixes
            if (constraint.StartsWith(">=")) return Compare(actual, constraint[2..]) >= 0;
            if (constraint.StartsWith("<=")) return Compare(actual, constraint[2..]) <= 0;
            if (constraint.StartsWith('>'))  return Compare(actual, constraint[1..]) > 0;
            if (constraint.StartsWith('<'))  return Compare(actual, constraint[1..]) < 0;
            if (constraint.StartsWith('^') || constraint.StartsWith('~'))
                return Compare(actual, constraint[1..]) >= 0;   // tolerant: at-least semantics
            if (constraint.EndsWith(".x") || constraint.EndsWith(".X"))
                return actual.StartsWith(constraint[..^1], OIC);

            return Compare(actual, constraint.TrimStart('=')) == 0;
        }
        catch
        {
            return true; // unparseable constraint — assume satisfied rather than false-alarm
        }
    }

    /// <summary>Numeric-segment comparison; non-numeric tails compared ordinally.</summary>
    private static int Compare(string a, string b)
    {
        static int[] Nums(string s) => Regex.Matches(s, @"\d+").Take(4)
            .Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToArray();

        var x = Nums(a); var y = Nums(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var xi = i < x.Length ? x[i] : 0;
            var yi = i < y.Length ? y[i] : 0;
            if (xi != yi) return xi.CompareTo(yi);
        }
        return 0;
    }

    private static string Display(ModMeta m) =>
        string.IsNullOrEmpty(m.Version) ? $"'{m.ModId}' ({m.FileName})" : $"'{m.ModId}' {m.Version}";

    private static string StateHash(List<string> jars)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var f in jars)
        {
            var fi = new FileInfo(f);
            sb.Append(fi.Name).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
        }
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }
}
