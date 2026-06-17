using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AnchorLauncher.Models.Diagnostics;

namespace AnchorLauncher.Services.Diagnostics;

/// <summary>
/// Translates a launch failure into plain English by scanning the captured console output
/// plus the most recent file in the instance's crash-reports/ folder. Where the cause is
/// unambiguous it also attaches a one-click <see cref="CrashFixKind"/> the VM can auto-apply.
/// </summary>
public class CrashAnalyzerService
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

    public CrashAnalysis Analyze(string gameDir, string consoleLog)
    {
        var sb = new StringBuilder(consoleLog);
        string? reportPath = null;

        try
        {
            var dir = Path.Combine(gameDir, "crash-reports");
            if (Directory.Exists(dir))
            {
                var latest = new DirectoryInfo(dir).EnumerateFiles("*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                // Only trust a very recent report (this launch)
                if (latest != null && (DateTime.UtcNow - latest.LastWriteTimeUtc).TotalMinutes < 10)
                {
                    reportPath = latest.FullName;
                    sb.AppendLine().Append(File.ReadAllText(latest.FullName));
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Crash] report scan failed: {ex.Message}"); }

        var analysis = Classify(sb.ToString(), gameDir);
        analysis.ReportPath = reportPath;
        return analysis;
    }

    private static CrashAnalysis Classify(string t, string gameDir)
    {
        if (Has(t, "OutOfMemoryError") || Has(t, "GC overhead limit exceeded") ||
            Has(t, "unable to create new native thread"))
            return new CrashAnalysis
            {
                Title       = "Out of memory",
                Explanation = "Minecraft ran out of the RAM allocated to it. Anchor can add 2 GB to the allocation and launch again automatically.",
                Fix         = CrashFixKind.IncreaseMemory,
                FixLabel    = "Add 2 GB & relaunch"
            };

        // Wrong Java — the class-file version pins exactly which JRE is needed (52=8, 61=17, 65=21…)
        var requiredJava = MatchRequiredJava(t);
        if (requiredJava > 0)
            return new CrashAnalysis
            {
                Title             = "Wrong Java version",
                Explanation       = $"This version needs Java {requiredJava}, but an older runtime was used. Anchor can download the correct Java {requiredJava} runtime and relaunch automatically.",
                Fix               = CrashFixKind.DownloadJava,
                FixLabel          = $"Install Java {requiredJava} & relaunch",
                RequiredJavaMajor = requiredJava
            };

        var dependency = MatchDependency(t);
        if (dependency != null)
            return new CrashAnalysis
            {
                Title          = "Missing mod dependency",
                Explanation    = dependency.Value.Explanation,
                Fix            = CrashFixKind.InstallDependency,
                FixLabel       = $"Find & install ‘{dependency.Value.Name}’",
                DependencyName = dependency.Value.Name
            };

        if (Has(t, "Incompatible mod set") || Has(t, "requires a different version") ||
            Has(t, "is built for") || Has(t, "NoClassDefFoundError") || Has(t, "NoSuchMethodError") ||
            Has(t, "AbstractMethodError") || Has(t, "Mixin apply failed") || Has(t, "MixinApplyError"))
        {
            var culprit = FindCulpritMod(gameDir, t);
            if (culprit != null)
                return new CrashAnalysis
                {
                    Title              = "Conflicting mod",
                    Explanation        = $"‘{culprit}’ doesn't match this Minecraft/loader version and crashed the game. Anchor can disable it and relaunch automatically.",
                    Fix                = CrashFixKind.DisableConflictingMod,
                    FixLabel           = "Disable mod & relaunch",
                    ConflictingModFile = culprit
                };

            return new CrashAnalysis
            {
                Title       = "Incompatible mods",
                Explanation = "One or more mods don't match this Minecraft or loader version. Open the mods folder to remove or update the offending mod.",
                Fix         = CrashFixKind.OpenModsFolder,
                FixLabel    = "Open Mods Folder"
            };
        }

        if (Has(t, "Pixel format not accelerated") || Has(t, "Failed to create window") ||
            Has(t, "GLFW error") || Has(t, "WGL: ") || Has(t, "Couldn't create context"))
            return new CrashAnalysis
            {
                Title       = "Graphics driver issue",
                Explanation = "The game couldn't initialize graphics. Update your GPU drivers and make sure Minecraft is using your dedicated GPU."
            };

        var line = FirstErrorLine(t);
        return new CrashAnalysis
        {
            Title       = "Game crashed",
            Explanation = line ?? "Minecraft exited unexpectedly. Open the console log (Copy All) for the full details."
        };
    }

    /// <summary>
    /// Reads "Unsupported ... class file version N.0" and maps it to the Java major that can run it.
    /// Class-file major 52 → Java 8, 53 → 9 … 61 → 17, 65 → 21 (major = javaVersion + 44).
    /// </summary>
    private static int MatchRequiredJava(string t)
    {
        if (!Has(t, "UnsupportedClassVersionError")) return 0;

        var m = Regex.Match(t, @"class file version\s+(\d+)(?:\.\d+)?", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var classMajor) && classMajor >= 52)
            return classMajor - 44;

        // Fallback: message mentions a specific Java release ("requires Java 17")
        var jm = Regex.Match(t, @"requires?\s+Java\s+(\d+)", RegexOptions.IgnoreCase);
        if (jm.Success && int.TryParse(jm.Groups[1].Value, out var major) && major >= 8)
            return major;

        // Known-bad but version-less → assume the modern LTS the launcher ships
        return 17;
    }

    private static (string Name, string Explanation)? MatchDependency(string t)
    {
        // Fabric: "requires version x of fabric-api, which is missing!"
        var m = Regex.Match(t, @"requires[^\n]*?of\s+'?([A-Za-z0-9_\-\. ]+?)'?,?\s*which is missing", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var name = CleanModName(m.Groups[1].Value);
            return (name, $"A mod requires ‘{name}’, which isn't installed. Anchor can open the marketplace filtered to it so you can install it, then relaunch.");
        }

        // Forge: "Mod X requires dependency Y" style
        var fm = Regex.Match(t, @"requires\s+'?([A-Za-z0-9_\-\. ]+?)'?\s+(?:version|to be installed|but it is missing)", RegexOptions.IgnoreCase);
        if (fm.Success)
        {
            var name = CleanModName(fm.Groups[1].Value);
            return (name, $"A mod requires ‘{name}’, which isn't installed. Anchor can open the marketplace filtered to it so you can install it, then relaunch.");
        }

        if (Has(t, "Missing or unsupported mandatory dependencies"))
        {
            // Forge lists each missing dep as "Mod ID: 'fabric' ..." — pull the first id
            var id = Regex.Match(t, @"Mod ID:\s*'([A-Za-z0-9_\-]+)'", RegexOptions.IgnoreCase);
            var name = id.Success ? CleanModName(id.Groups[1].Value) : "the required library";
            return (name, $"Forge reported a missing mandatory dependency (‘{name}’). Anchor can open the marketplace so you can install it, then relaunch.");
        }

        return null;
    }

    /// <summary>
    /// Correlates the crash text with the files actually in mods/: returns the jar filename that is
    /// named in the report (Forge "Mod File:" lines or any *.jar token), preferring one near an error.
    /// </summary>
    private static string? FindCulpritMod(string gameDir, string t)
    {
        try
        {
            var modsDir = Path.Combine(gameDir, "mods");
            if (!Directory.Exists(modsDir)) return null;

            var present = Directory.EnumerateFiles(modsDir, "*.jar")
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList()!;
            if (present.Count == 0) return null;

            // 1) Forge crash reports point straight at the failing jar.
            foreach (Match mf in Regex.Matches(t, @"Mod File:\s*.*?([^\\/\r\n]+\.jar)", RegexOptions.IgnoreCase))
            {
                var name = mf.Groups[1].Value.Trim();
                var hit = present.FirstOrDefault(p => string.Equals(p, name, OIC));
                if (hit != null) return hit;
            }

            // 2) Any jar filename mentioned anywhere in the log that exists in mods/.
            foreach (Match jm in Regex.Matches(t, @"([^\\/\s""']+\.jar)", RegexOptions.IgnoreCase))
            {
                var name = jm.Groups[1].Value.Trim();
                var hit = present.FirstOrDefault(p => string.Equals(p, name, OIC));
                if (hit != null) return hit;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Crash] FindCulpritMod failed: {ex.Message}"); }

        return null;
    }

    private static string CleanModName(string raw)
    {
        var name = raw.Trim().Trim('\'', '"', '.', ',');
        // Friendlier display for the common API ids
        return name.Replace("fabric-api", "Fabric API", OIC)
                   .Replace("fabricloader", "Fabric Loader", OIC);
    }

    private static string? FirstErrorLine(string t)
    {
        foreach (var raw in t.Split('\n'))
        {
            var l = raw.Trim();
            if (l.Length is > 0 and < 240 &&
                (l.Contains("Exception", OIC) || l.Contains("Caused by", OIC) ||
                 (l.Contains("Error", OIC) && !l.Contains("ErrorReporter", OIC))))
                return l;
        }
        return null;
    }

    private static bool Has(string t, string token) => t.IndexOf(token, OIC) >= 0;
}
