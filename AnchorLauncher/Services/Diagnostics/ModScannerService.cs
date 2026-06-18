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
        public Dictionary<string, string> Depends  = new(StringComparer.OrdinalIgnoreCase); // id → version constraint
        public Dictionary<string, string> Breaks   = new(StringComparer.OrdinalIgnoreCase); // id → version constraint
        // Extra ids this jar makes available: its `provides` aliases plus every bundled
        // (nested) module — e.g. Fabric API ships fabric-resource-loader-v1 et al. as nested jars.
        public Dictionary<string, string> Provides = new(StringComparer.OrdinalIgnoreCase); // id → version
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

            // Every id that is actually available, → its version (or "" when unknown).
            // Includes each mod's own id, its `provides` aliases, and every bundled nested module.
            var providedVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // id → the top-level jar that supplies it (for naming the "victim" in a break).
            var providedBy = new Dictionary<string, ModMeta>(StringComparer.OrdinalIgnoreCase);

            foreach (var m in metas)
            {
                if (!string.IsNullOrEmpty(m.ModId))
                {
                    providedVersions[m.ModId] = m.Version;
                    providedBy.TryAdd(m.ModId, m);
                }
                foreach (var (pid, pver) in m.Provides)
                {
                    if (!providedVersions.ContainsKey(pid)) providedVersions[pid] = pver;
                    providedBy.TryAdd(pid, m);
                }
            }

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

                    if (!providedVersions.TryGetValue(depId, out var depVer))
                    {
                        // "fabric" historically aliases fabric-api
                        if (depId.Equals("fabric", OIC) && providedVersions.ContainsKey("fabric-api")) continue;

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
                    // Only flag a version mismatch when we actually know the installed version.
                    // Provided/bundled aliases often have no version → don't cry wolf.
                    else if (!IsAnyVersion(constraint) && !string.IsNullOrEmpty(depVer) &&
                             !VersionSatisfies(depVer, constraint))
                    {
                        conflicts.Add(new ModConflict
                        {
                            Severity = ConflictSeverity.Warning,
                            Kind     = ConflictKind.VersionMismatch,
                            PrimaryFile = mod.FileName,
                            PrimaryDependents = DependentsOf(mod),
                            Message  = $"{Display(mod)} requires '{depId}' version {constraint} (you have {depVer})."
                        });
                    }
                }

                // Declared incompatibilities — only when the installed version is actually in the
                // broken range. `breaks` usually means "breaks OLD versions of X"; ignoring the
                // range is what falsely flagged e.g. Sodium vs a compatible new Iris.
                foreach (var (brokenId, brkConstraint) in mod.Breaks)
                {
                    if (BuiltinIds.Contains(brokenId)) continue;
                    if (!providedVersions.TryGetValue(brokenId, out var victimVer)) continue;

                    var inBrokenRange = IsAnyVersion(brkConstraint)
                        || (!string.IsNullOrEmpty(victimVer) && VersionSatisfies(victimVer, brkConstraint));
                    if (!inBrokenRange) continue;

                    var victim = providedBy.TryGetValue(brokenId, out var vm) ? vm : null;
                    conflicts.Add(new ModConflict
                    {
                        Severity = ConflictSeverity.Error,
                        Kind     = ConflictKind.Incompatible,
                        PrimaryFile         = mod.FileName,
                        PrimaryDependents   = DependentsOf(mod),
                        SecondaryFile       = victim?.FileName ?? brokenId,
                        SecondaryDependents = victim != null ? DependentsOf(victim) : 0,
                        Message  = victim != null
                            ? $"{Display(mod)} is incompatible with {Display(victim)} — they cannot run together."
                            : $"{Display(mod)} is incompatible with '{brokenId}' — they cannot run together."
                    });
                }
            }

            lock (_cache) { _cache[key] = conflicts; }
            Debug.WriteLine($"[ModScan] {jars.Count} jars scanned, {providedVersions.Count} provided ids, {conflicts.Count} findings.");
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
            {
                var meta = ParseFabricJson(ReadEntry(fabric), Path.GetFileName(jarPath));
                if (meta != null) CollectNestedModules(zip, meta, depth: 0);
                return meta;
            }

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

    /// <summary>
    /// Registers every module bundled inside a jar (Fabric "jar-in-jar"). Fabric API ships its
    /// ~50 sub-modules (fabric-resource-loader-v1, fabric-screen-api-v1, …) this way, so without
    /// this every mod that depends on a Fabric API module looks like it has a missing dependency.
    /// </summary>
    private static void CollectNestedModules(ZipArchive zip, ModMeta into, int depth)
    {
        if (depth > 2) return; // Fabric API is one level deep; cap recursion defensively
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith(".jar", OIC)) continue;
            // bundled jars live under META-INF/jars/ (Fabric) or jars/ (Quilt)
            if (!entry.FullName.StartsWith("META-INF/jars/", OIC) &&
                !entry.FullName.StartsWith("jars/", OIC)) continue;

            try
            {
                using var nested = entry.Open();
                using var ms = new MemoryStream();
                nested.CopyTo(ms);
                ms.Position = 0;
                using var nestedZip = new ZipArchive(ms, ZipArchiveMode.Read);

                var fj = nestedZip.GetEntry("fabric.mod.json") ?? nestedZip.GetEntry("quilt.mod.json");
                if (fj == null) continue;

                var sub = ParseFabricJson(ReadEntry(fj), entry.Name);
                if (sub == null) continue;

                if (!string.IsNullOrEmpty(sub.ModId)) into.Provides.TryAdd(sub.ModId, sub.Version);
                foreach (var (pid, pver) in sub.Provides) into.Provides.TryAdd(pid, pver);

                CollectNestedModules(nestedZip, into, depth + 1); // a bundle can bundle further
            }
            catch { /* skip an unreadable nested jar */ }
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
                ReadConstraintElement(dep, meta.Depends);

            if (root.TryGetProperty("breaks", out var brk))
                ReadConstraintElement(brk, meta.Breaks);

            // `provides` — explicit aliases this mod satisfies
            if (root.TryGetProperty("provides", out var prov) && prov.ValueKind == JsonValueKind.Array)
                foreach (var e in prov.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && e.GetString() is { } pid && pid.Length > 0)
                        meta.Provides.TryAdd(pid, meta.Version);

            return string.IsNullOrEmpty(meta.ModId) ? null : meta;
        }
        catch { return null; }
    }

    /// <summary>Reads a fabric/quilt depends-or-breaks element (object id→constraint, or array) into a map.</summary>
    private static void ReadConstraintElement(JsonElement el, Dictionary<string, string> into)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject())
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
        else if (el.ValueKind == JsonValueKind.Array) // quilt style: [{ "id": "...", "versions": "..." }] or ["id", …]
        {
            foreach (var e in el.EnumerateArray())
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
