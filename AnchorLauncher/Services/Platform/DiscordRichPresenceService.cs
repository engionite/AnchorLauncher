using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnchorLauncher.Services.Platform;

/// <summary>
/// Discord Rich Presence over the local Discord IPC named pipe (<c>discord-ipc-0</c>…<c>9</c>).
/// No third-party dependency: it speaks Discord's documented framing directly — an 8-byte header
/// (little-endian int32 opcode + int32 length) followed by a UTF-8 JSON payload.
///
/// While the launcher is open this shows "Anchor Launcher · vX.Y.Z" on the user's Discord profile,
/// with the elapsed time and GitHub / Download buttons. It is entirely best-effort: if Discord
/// isn't running, the <see cref="ClientId"/> is unset, or any I/O fails, it silently no-ops — the
/// launcher never blocks on or depends on it. Disposing clears the presence.
/// </summary>
public sealed class DiscordRichPresenceService : IDisposable
{
    // ── REQUIRED: the Anchor Launcher Discord Application ID ──────────────────────
    // One-time setup (the app's identity, shared by every user — NOT a per-user secret):
    //   1. Go to https://discord.com/developers/applications and create an application
    //      named "Anchor Launcher".
    //   2. Copy its "Application ID" and paste it between the quotes below.
    //   3. Under Rich Presence ▸ Art Assets, upload a 512×512 logo named exactly
    //      "anchor_logo" (matches LargeImageKey below).
    // Left empty, Rich Presence is simply disabled and the launcher behaves normally.
    public const string ClientId = "1518266422361591818";

    private const string LargeImageKey = "anchor_logo";
    private const string AppName       = "Anchor Launcher";
    private const string Tagline       = "Premium Minecraft Launcher";
    private const string GitHubUrl     = "https://github.com/engionite/AnchorLauncher";
    private const string DownloadUrl   = "https://github.com/engionite/AnchorLauncher/releases/latest";

    private NamedPipeClientStream? _pipe;
    private readonly long _startedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private bool _disposed;

    /// <summary>Connects to Discord and publishes the activity. Fire-and-forget; never throws.</summary>
    public void Start()
    {
        if (string.IsNullOrWhiteSpace(ClientId)) return;   // feature disabled until an app id is set
        _ = Task.Run(ConnectAndSetAsync);
    }

    private async Task ConnectAndSetAsync()
    {
        // Discord exposes its IPC on the first free pipe ordinal (0-9 covers multiple clients).
        for (int i = 0; i < 10 && !_disposed; i++)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}",
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000);
                _pipe = pipe;

                // Handshake (opcode 0), then drain the READY frame, then set the activity (opcode 1).
                await WriteFrameAsync(0, $"{{\"v\":1,\"client_id\":\"{ClientId}\"}}");
                await DrainOneFrameAsync();
                await SendActivityAsync();
                return;
            }
            catch
            {
                try { _pipe?.Dispose(); } catch { }
                _pipe = null;   // try the next ordinal; Discord may not be running at all
            }
        }
    }

    private async Task SendActivityAsync()
    {
        if (_pipe is not { IsConnected: true }) return;

        var activity = new
        {
            details    = Tagline,
            state      = $"v{UpdateCheckService.CurrentVersion}",
            timestamps = new { start = _startedUnix },
            assets     = new { large_image = LargeImageKey, large_text = AppName },
            buttons    = new[]
            {
                new { label = "Download Anchor Launcher", url = DownloadUrl },
                new { label = "View on GitHub",           url = GitHubUrl },
            },
        };

        var frame = new
        {
            cmd   = "SET_ACTIVITY",
            nonce = Guid.NewGuid().ToString(),
            args  = new { pid = Environment.ProcessId, activity },
        };

        await WriteFrameAsync(1, JsonSerializer.Serialize(frame));
    }

    private async Task WriteFrameAsync(int opcode, string json)
    {
        if (_pipe is null) return;
        var payload = Encoding.UTF8.GetBytes(json);
        var buffer  = new byte[8 + payload.Length];
        BitConverter.GetBytes(opcode).CopyTo(buffer, 0);          // little-endian on Windows
        BitConverter.GetBytes(payload.Length).CopyTo(buffer, 4);
        payload.CopyTo(buffer, 8);
        await _pipe.WriteAsync(buffer);
        await _pipe.FlushAsync();
    }

    /// <summary>Best-effort read of one inbound frame (e.g. the post-handshake READY) so the
    /// pipe buffer doesn't back up. Bounded by a short timeout; failures are ignored.</summary>
    private async Task DrainOneFrameAsync()
    {
        try
        {
            if (_pipe is null) return;
            using var cts = new CancellationTokenSource(1500);

            var header = new byte[8];
            if (!await ReadExactAsync(header, cts.Token)) return;

            int len = BitConverter.ToInt32(header, 4);
            if (len > 0 && len < 64 * 1024)
                await ReadExactAsync(new byte[len], cts.Token);
        }
        catch { /* draining is optional */ }
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        if (_pipe is null) return false;
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await _pipe.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Disposing the pipe ends the IPC session, which clears the presence on Discord's side.
        try { _pipe?.Dispose(); } catch { }
        _pipe = null;
    }
}
