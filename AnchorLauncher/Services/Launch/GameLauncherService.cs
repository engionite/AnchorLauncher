using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AnchorLauncher.Models;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Auth;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Net;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Launch;

/// <summary>
/// Implements the Mojang launch protocol: resolve (and merge inherited) version metadata,
/// download the client jar + libraries + natives + assets, build the JVM/game argument
/// vector with placeholder substitution and OS/feature rule evaluation, then start the
/// process with STDOUT/STDERR redirected to a live callback. Ely.by accounts get the
/// authlib-injector javaagent prepended.
/// </summary>
public class GameLauncherService
{
    private const string AssetBaseUrl   = "https://resources.download.minecraft.net";
    private const string VanillaLibBase = "https://libraries.minecraft.net";
    private const string ElyAuthlibApi  = "https://authserver.ely.by/api/authlib-injector";

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private readonly MojangManifestService _manifest = new();
    private readonly JavaRuntimeService    _java     = new();

    private static string VersionsRoot  => Path.Combine(LauncherStorageService.AppDataRoot, "versions");
    private static string LibrariesRoot => Path.Combine(LauncherStorageService.AppDataRoot, "libraries");
    private static string AssetsRoot    => Path.Combine(LauncherStorageService.AppDataRoot, "assets");

    private static readonly string CurrentOsName = "windows";

    /// <summary>
    /// Prepares all files then starts the game. The returned <see cref="Process"/> has begun
    /// async output reads; <paramref name="onLogLine"/> receives every STDOUT/STDERR line.
    /// </summary>
    public async Task<Process> PrepareAndLaunchAsync(
        MinecraftInstance instance,
        LaunchAuth auth,
        EffectiveLaunchOptions options,
        IProgress<DownloadProgress>? progress,
        Action<string> onLogLine,
        CancellationToken ct = default)
    {
        // Pipe download diagnostics (404 fallbacks etc.) into the live console
        DownloadHelper.LogSink = line => onLogLine($"[Anchor] {line}");

        Report(progress, onLogLine, 2, "Resolving version metadata…");
        var chain        = await ResolveVersionChainAsync(instance, ct);
        var launchId     = chain[0].Id;
        var clientJarId  = chain[^1].Id;

        var mainClass    = FirstNonNull(chain, v => v.MainClass)
                           ?? throw new InvalidOperationException("Version metadata has no mainClass.");
        var assetIndex   = FirstNonNull(chain, v => v.AssetIndex)
                           ?? throw new InvalidOperationException("Version metadata has no assetIndex.");
        var clientDl     = FirstNonNull(chain, v => v.Downloads?.Client)
                           ?? throw new InvalidOperationException("Version metadata has no client download.");
        var javaMajor    = FirstNonNull(chain, v => v.JavaVersion)?.MajorVersion ?? GuessJavaMajor(instance.Version);

        // 1) Client jar
        Report(progress, onLogLine, 8, "Downloading game client…");
        var clientJar = Path.Combine(VersionsRoot, clientJarId, clientJarId + ".jar");
        await DownloadHelper.DownloadFileAsync(clientDl.Url, clientJar, clientDl.Sha1, ct);

        // 2) Libraries + natives
        Report(progress, onLogLine, 18, "Downloading libraries…");
        var nativesDir = Path.Combine(VersionsRoot, launchId, "natives");
        Directory.CreateDirectory(nativesDir);
        var classpath = await DownloadLibrariesAsync(chain, nativesDir, progress, onLogLine, ct);
        classpath.Add(clientJar);

        // 3) Assets (parallel)
        Report(progress, onLogLine, 55, "Downloading assets…");
        await DownloadAssetsAsync(assetIndex, progress, onLogLine, ct);

        // 4) Java runtime — honor an explicit override, else auto-resolve by required major
        string javaExe;
        if (!string.IsNullOrWhiteSpace(options.JavaPath) && File.Exists(options.JavaPath))
        {
            javaExe = options.JavaPath!;
            Report(progress, onLogLine, 85, "Using configured Java runtime…");
        }
        else
        {
            Report(progress, onLogLine, 85, $"Resolving Java {javaMajor}…");
            javaExe = await _java.ResolveJavaAsync(javaMajor, progress, ct)
                      ?? throw new InvalidOperationException($"Could not resolve a Java {javaMajor} runtime.");
        }

        // 5) Apply fullscreen preference into options.txt before the game reads it
        ApplyFullscreenPreference(instance.GameDir, options.Fullscreen);

        // 6) Argument vector
        Report(progress, onLogLine, 96, "Building launch arguments…");
        var args = await BuildArgumentVectorAsync(
            chain, instance, auth, options, launchId, assetIndex.Id,
            string.Join(';', classpath), nativesDir, ct);

        // 7) Launch
        Report(progress, onLogLine, 100, "Launching…");
        return StartProcess(javaExe, args, instance.GameDir, onLogLine);
    }

    // ── Version metadata resolution (inheritsFrom chain) ────────────────────────

    private async Task<List<VersionJson>> ResolveVersionChainAsync(MinecraftInstance instance, CancellationToken ct)
    {
        var launchId = string.IsNullOrEmpty(instance.LaunchVersionId) ? instance.Version : instance.LaunchVersionId!;
        var chain    = new List<VersionJson>();

        var current = await LoadVersionJsonAsync(launchId, ct);
        chain.Add(current);

        var guard = 0;
        while (!string.IsNullOrEmpty(current.InheritsFrom) && guard++ < 10)
        {
            current = await LoadVersionJsonAsync(current.InheritsFrom!, ct);
            chain.Add(current);
        }

        return chain; // [0] = most-derived (launch profile), [^1] = base vanilla
    }

    /// <summary>Loads versions/&lt;id&gt;/&lt;id&gt;.json from disk, or downloads a vanilla one.</summary>
    private async Task<VersionJson> LoadVersionJsonAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(VersionsRoot, id, id + ".json");
        if (File.Exists(path))
        {
            var local = JsonSerializer.Deserialize<VersionJson>(await File.ReadAllTextAsync(path, ct), _json);
            if (local != null) return local;
        }

        // Vanilla: look it up in the manifest and cache the metadata
        var manifest = await _manifest.GetManifestAsync(ct: ct)
                       ?? throw new InvalidOperationException("Could not load the Mojang manifest.");
        var entry = manifest.Versions.FirstOrDefault(v => v.Id == id)
                    ?? throw new InvalidOperationException($"Version '{id}' is not installed and not in the manifest.");

        var body = await DownloadHelper.GetStringAsync(entry.Url, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, body, ct);

        return JsonSerializer.Deserialize<VersionJson>(body, _json)
               ?? throw new InvalidOperationException($"Malformed version metadata for '{id}'.");
    }

    // ── Libraries & natives ─────────────────────────────────────────────────────

    private async Task<List<string>> DownloadLibrariesAsync(
        List<VersionJson> chain, string nativesDir,
        IProgress<DownloadProgress>? progress, Action<string> onLogLine, CancellationToken ct)
    {
        var classpath = new List<string>();
        var seen      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // child → parent so loader libraries take classpath precedence
        var libs = chain.SelectMany(v => v.Libraries).ToList();

        for (int i = 0; i < libs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var lib = libs[i];

            if (!EvaluateRules(lib.Rules)) continue;

            // Main artifact → classpath
            if (TryResolveArtifact(lib, out var relPath, out var url, out var sha1) &&
                seen.Add(LibraryKey(lib.Name)))
            {
                var dest = Path.Combine(LibrariesRoot, relPath);
                await DownloadHelper.DownloadFileAsync(url, dest, sha1, ct);
                classpath.Add(dest);
            }

            // Natives → extract into the natives dir
            await ExtractNativesIfAnyAsync(lib, nativesDir, ct);

            if (i % 6 == 0 || i == libs.Count - 1)
            {
                var n = i + 1;
                Report(progress, onLogLine,
                    18 + 37.0 * n / Math.Max(1, libs.Count),
                    $"Downloading libraries ({n}/{libs.Count})…");
            }
        }

        return classpath;
    }

    private static bool TryResolveArtifact(Library lib, out string relPath, out string url, out string? sha1)
    {
        relPath = string.Empty; url = string.Empty; sha1 = null;

        if (lib.Downloads?.Artifact is { } art && !string.IsNullOrEmpty(art.Url))
        {
            relPath = art.Path ?? MavenToPath(lib.Name);
            url     = art.Url;
            sha1    = art.Sha1;
            return true;
        }

        // Native-only libraries have no main artifact
        if (lib.Natives != null && lib.Downloads?.Artifact == null && lib.Url == null)
            return false;

        // Maven-style (Fabric/Quilt or vanilla fallback)
        relPath = MavenToPath(lib.Name);
        var baseUrl = string.IsNullOrEmpty(lib.Url) ? VanillaLibBase : lib.Url!.TrimEnd('/');
        url = $"{baseUrl}/{relPath}";
        return true;
    }

    private static async Task ExtractNativesIfAnyAsync(Library lib, string nativesDir, CancellationToken ct)
    {
        if (lib.Natives == null || !lib.Natives.TryGetValue(CurrentOsName, out var classifierTemplate))
            return;

        var classifier = classifierTemplate.Replace("${arch}", Environment.Is64BitProcess ? "64" : "32");
        if (lib.Downloads?.Classifiers == null ||
            !lib.Downloads.Classifiers.TryGetValue(classifier, out var art) ||
            string.IsNullOrEmpty(art.Url))
            return;

        var jar = Path.Combine(LibrariesRoot, art.Path ?? MavenToPath(lib.Name) + "-" + classifier);
        await DownloadHelper.DownloadFileAsync(art.Url, jar, art.Sha1, ct);

        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(jar);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;                   // directory
                if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;
                if (lib.Extract?.Exclude?.Any(ex => entry.FullName.StartsWith(ex, StringComparison.OrdinalIgnoreCase)) == true)
                    continue;

                var target = Path.Combine(nativesDir, entry.Name);
                try { entry.ExtractToFile(target, overwrite: true); } catch { }
            }
        }, ct);
    }

    // ── Assets ──────────────────────────────────────────────────────────────────

    private async Task DownloadAssetsAsync(
        AssetIndexRef assetIndex, IProgress<DownloadProgress>? progress, Action<string> onLogLine, CancellationToken ct)
    {
        var indexPath = Path.Combine(AssetsRoot, "indexes", assetIndex.Id + ".json");
        await DownloadHelper.DownloadFileAsync(assetIndex.Url, indexPath, assetIndex.Sha1, ct);

        var index = JsonSerializer.Deserialize<AssetIndexFile>(await File.ReadAllTextAsync(indexPath, ct), _json);
        if (index == null) return;

        var objects = index.Objects.Values.ToList();
        var total   = objects.Count;
        if (total == 0) return;

        // Parallelize: assets are thousands of tiny files — sequential is the bottleneck.
        // Concurrency is the user-configurable download-thread count (Settings → Network).
        using var gate = new SemaphoreSlim(Math.Clamp(Net.NetworkConfig.DownloadThreads, 4, 32));
        var done = 0;

        var tasks = objects.Select(async obj =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var hash   = obj.Hash;
                var prefix = hash[..2];
                var dest   = Path.Combine(AssetsRoot, "objects", prefix, hash);
                if (!File.Exists(dest))
                    await DownloadHelper.DownloadFileAsync($"{AssetBaseUrl}/{prefix}/{hash}", dest, hash, ct);
            }
            finally
            {
                gate.Release();
            }

            var n = Interlocked.Increment(ref done);
            if (n % 50 == 0 || n == total)
                Report(progress, onLogLine,
                    55 + 30.0 * n / total,
                    $"Downloading assets ({n}/{total})…");
        });

        await Task.WhenAll(tasks);
    }

    // ── Argument vector ─────────────────────────────────────────────────────────

    private async Task<List<string>> BuildArgumentVectorAsync(
        List<VersionJson> chain, MinecraftInstance instance, LaunchAuth auth, EffectiveLaunchOptions options,
        string versionId, string assetsId, string classpath, string nativesDir, CancellationToken ct)
    {
        var subs = new Dictionary<string, string>
        {
            ["auth_player_name"]    = auth.PlayerName,
            ["version_name"]        = versionId,
            ["game_directory"]      = instance.GameDir,
            ["assets_root"]         = AssetsRoot,
            ["game_assets"]         = AssetsRoot,
            ["assets_index_name"]   = assetsId,
            ["auth_uuid"]           = auth.Uuid,
            ["auth_access_token"]   = auth.AccessToken,
            ["auth_session"]        = $"token:{auth.AccessToken}:{auth.Uuid}",
            ["user_type"]           = auth.UserType,
            ["user_properties"]     = "{}",
            ["version_type"]        = instance.VersionType,
            ["natives_directory"]   = nativesDir,
            ["classpath"]           = classpath,
            ["launcher_name"]       = "AnchorLauncher",
            ["launcher_version"]    = "1.0",
            ["library_directory"]   = LibrariesRoot,
            ["classpath_separator"] = ";",
            ["clientid"]            = string.Empty,
            ["auth_xuid"]           = string.Empty
        };

        var jvm  = new List<string>();
        var game = new List<string>();

        // authlib-injector first so it patches the auth service before MC starts.
        // The "={ElyAuthlibApi}" suffix is the canonical form of the "=ely.by" shorthand — it
        // tells the injector which skin/auth server to use, so Ely.by skins render on servers.
        // Gated by the "Inject authlib" setting: Never → off; Always → every account;
        // ElyOnly (default) → only Ely.by accounts.
        var injectAuthlib = options.ElyPatch switch
        {
            ElyPatchMode.Never  => false,
            ElyPatchMode.Always => true,
            _                   => auth.IsElyBy,
        };
        if (injectAuthlib)
        {
            await ElyAuthService.EnsureAuthlibInjectorAsync(ct);
            var jar = LauncherStorageService.AuthlibInjectorPath;
            if (File.Exists(jar) && new FileInfo(jar).Length > 0)
            {
                jvm.Add($"-javaagent:{jar}={ElyAuthlibApi}");
                Debug.WriteLine($"[Launch] authlib-injector: {jar} ({new FileInfo(jar).Length} bytes) → {ElyAuthlibApi}");
            }
            else
            {
                Debug.WriteLine("[Launch] authlib-injector jar missing/empty — Ely.by skins will NOT render in-game.");
            }
        }

        // Heap sizing from the effective settings
        var xmx = Math.Max(512, options.MemoryMB);
        jvm.Add($"-Xms{Math.Min(512, xmx)}M");
        jvm.Add($"-Xmx{xmx}M");

        // User-supplied custom JVM flags
        foreach (var flag in SplitArgs(options.JvmArgs))
            jvm.Add(flag);

        // JVM args: parent → child (reverse chain). Fall back to the classic vector.
        var jvmProvided = false;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].Arguments?.Jvm is { } jvmEl)
            {
                AppendArgs(jvmEl, jvm, subs);
                jvmProvided = true;
            }
        }
        if (!jvmProvided)
        {
            jvm.Add($"-Djava.library.path={nativesDir}");
            jvm.Add("-cp");
            jvm.Add(classpath);
        }

        // Game args: modern structured first, else legacy minecraftArguments string
        var gameProvided = false;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].Arguments?.Game is { } gameEl)
            {
                AppendArgs(gameEl, game, subs);
                gameProvided = true;
            }
        }
        if (!gameProvided)
        {
            var legacy = FirstNonNull(chain, v => v.MinecraftArguments);
            if (!string.IsNullOrEmpty(legacy))
                foreach (var tok in legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    game.Add(Substitute(tok, subs));
        }

        // Window resolution (the game ignores these when launched fullscreen)
        if (!options.Fullscreen)
        {
            game.Add("--width");  game.Add(options.Width.ToString());
            game.Add("--height"); game.Add(options.Height.ToString());
        }

        // Quick-connect: join a server directly on launch (Home page server favorites)
        if (!string.IsNullOrWhiteSpace(options.ServerIp))
        {
            game.Add("--server"); game.Add(options.ServerIp!);
            if (options.ServerPort is int port) { game.Add("--port"); game.Add(port.ToString()); }
        }

        var result = new List<string>(jvm) { FirstNonNull(chain, v => v.MainClass)! };
        result.AddRange(game);
        return result;
    }

    /// <summary>Splits a JVM-flags string on whitespace (simple, quote-free).</summary>
    private static IEnumerable<string> SplitArgs(string? args)
        => string.IsNullOrWhiteSpace(args)
            ? Enumerable.Empty<string>()
            : args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Sets the fullscreen flag in the instance's options.txt without clobbering other settings.</summary>
    private static void ApplyFullscreenPreference(string gameDir, bool fullscreen)
    {
        try
        {
            var path  = Path.Combine(gameDir, "options.txt");
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            var entry = $"fullscreen:{(fullscreen ? "true" : "false")}";
            var idx   = lines.FindIndex(l => l.StartsWith("fullscreen:", StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) lines[idx] = entry; else lines.Add(entry);
            File.WriteAllLines(path, lines);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Launch] options.txt fullscreen write failed: {ex.Message}");
        }
    }

    /// <summary>Appends a modern arguments array (string | {rules,value}) with substitution.</summary>
    private static void AppendArgs(JsonElement arr, List<string> target, Dictionary<string, string> subs)
    {
        if (arr.ValueKind != JsonValueKind.Array) return;

        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                target.Add(Substitute(el.GetString()!, subs));
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                var rules = el.TryGetProperty("rules", out var r)
                    ? JsonSerializer.Deserialize<List<Rule>>(r.GetRawText(), _json)
                    : null;
                if (!EvaluateRules(rules)) continue;

                if (!el.TryGetProperty("value", out var val)) continue;
                if (val.ValueKind == JsonValueKind.String)
                    target.Add(Substitute(val.GetString()!, subs));
                else if (val.ValueKind == JsonValueKind.Array)
                    foreach (var v in val.EnumerateArray())
                        if (v.ValueKind == JsonValueKind.String)
                            target.Add(Substitute(v.GetString()!, subs));
            }
        }
    }

    // ── Process ─────────────────────────────────────────────────────────────────

    private static Process StartProcess(string javaExe, List<string> args, string workingDir, Action<string> onLogLine)
    {
        var psi = new ProcessStartInfo(javaExe)
        {
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();

        // Stream every line the instant the JVM writes it (no buffering until exit).
        _ = PumpStreamAsync(proc.StandardOutput, onLogLine);
        _ = PumpStreamAsync(proc.StandardError, onLogLine);

        Debug.WriteLine($"[Launch] Started {javaExe} (pid {proc.Id})");
        return proc;
    }

    private static async Task PumpStreamAsync(StreamReader reader, Action<string> onLogLine)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
                onLogLine(line);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Launch] stream pump ended: {ex.Message}");
        }
    }

    private static void Report(
        IProgress<DownloadProgress>? progress, Action<string>? onLogLine, double percent, string status)
    {
        progress?.Report(DownloadProgress.At(percent, status));
        onLogLine?.Invoke($"[Anchor] {status}");
    }

    // ── Rules / helpers ─────────────────────────────────────────────────────────

    private static bool EvaluateRules(List<Rule>? rules)
    {
        if (rules == null || rules.Count == 0) return true;

        var allowed = false; // default deny when rules are present
        foreach (var rule in rules)
        {
            if (!RuleMatches(rule)) continue;
            allowed = rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    private static bool RuleMatches(Rule rule)
    {
        // Feature rules: we enable no optional features, so any required feature fails the match
        if (rule.Features is { Count: > 0 })
            return false;

        if (rule.Os?.Name is { } osName && !osName.Equals(CurrentOsName, StringComparison.OrdinalIgnoreCase))
            return false;

        return true; // os matches (or unconditional)
    }

    private static string Substitute(string token, Dictionary<string, string> subs)
    {
        foreach (var kvp in subs)
            token = token.Replace("${" + kvp.Key + "}", kvp.Value);
        return token;
    }

    private static string MavenToPath(string name)
    {
        var parts      = name.Split(':');
        var group      = parts[0].Replace('.', '/');
        var artifact   = parts[1];
        var version    = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }

    private static string LibraryKey(string name)
    {
        var parts = name.Split(':');
        var classifier = parts.Length > 3 ? ":" + parts[3] : string.Empty;
        return $"{parts[0]}:{parts[1]}{classifier}";
    }

    private static T? FirstNonNull<T>(List<VersionJson> chain, Func<VersionJson, T?> selector) where T : class
    {
        foreach (var v in chain)
        {
            var value = selector(v);
            if (value != null) return value;
        }
        return null;
    }

    private static int GuessJavaMajor(string mcVersion)
    {
        // Best-effort when metadata omits javaVersion (older releases)
        var m = System.Text.RegularExpressions.Regex.Match(mcVersion, "^1\\.(\\d+)");
        if (!m.Success) return 8;
        var minor = int.Parse(m.Groups[1].Value);
        if (minor >= 20) return 21;
        if (minor >= 18) return 17;
        if (minor >= 17) return 16;
        return 8;
    }
}
