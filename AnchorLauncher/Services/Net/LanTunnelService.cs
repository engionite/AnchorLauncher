using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AnchorLauncher.Services.Net;

/// <summary>
/// "Play with a friend" over the internet with **no relay server and no account**: it finds the port
/// Minecraft picked when you Open to LAN, asks your router (via UPnP-IGD) to forward it, and reports
/// your public address so a friend can join with vanilla Direct Connection. The connection is direct
/// peer-to-peer — nothing flows through Anchor or any third party. Works wherever the router allows
/// UPnP port-mapping (most home routers); fails honestly behind carrier-grade NAT or with UPnP off.
/// </summary>
public sealed class LanTunnelService : IDisposable
{
    public enum HostStatus { Ok, NoLanWorld, NoUpnp, MappingFailed }
    public record HostResult(HostStatus Status, string? PublicIp, int Port);

    private string? _controlUrl;
    private string? _serviceType;
    private string  _localIp = "127.0.0.1";
    private int     _mappedPort;

    /// <summary>Listens for Minecraft's Open-to-LAN broadcast (224.0.0.60:4445) and returns its port.</summary>
    public async Task<int?> DetectLanPortAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 4445));
            udp.JoinMulticastGroup(IPAddress.Parse("224.0.0.60"));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            while (!cts.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cts.Token);
                var msg = Encoding.UTF8.GetString(result.Buffer);
                var m = Regex.Match(msg, @"\[AD\](\d{1,5})\[/AD\]");
                if (m.Success && int.TryParse(m.Groups[1].Value, out var port) && port is > 0 and <= 65535)
                    return port;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[Lan] detect failed: {ex.Message}"); }
        return null;
    }

    /// <summary>Forwards <paramref name="port"/> through the router via UPnP and returns the public IP.</summary>
    public async Task<HostResult> HostAsync(int port, CancellationToken ct)
    {
        if (port is <= 0 or > 65535) return new HostResult(HostStatus.NoLanWorld, null, 0);

        _localIp = LocalIPv4();
        if (!await DiscoverIgdAsync(ct))            return new HostResult(HostStatus.NoUpnp, null, port);
        if (!await AddPortMappingAsync(port, ct))   return new HostResult(HostStatus.MappingFailed, null, port);

        _mappedPort = port;
        var publicIp = await GetExternalIpAsync(ct);
        return new HostResult(HostStatus.Ok, publicIp, port);
    }

    /// <summary>Removes the port mapping so the path closes when the host stops.</summary>
    public async Task StopAsync()
    {
        if (_mappedPort > 0 && _controlUrl != null)
            try { await DeletePortMappingAsync(_mappedPort); } catch { }
        _mappedPort = 0;
    }

    public void Dispose() { try { StopAsync().GetAwaiter().GetResult(); } catch { } }

    // ── UPnP-IGD ─────────────────────────────────────────────────────────────

    private async Task<bool> DiscoverIgdAsync(CancellationToken ct)
    {
        try
        {
            const string search =
                "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
                "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";

            using var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            var bytes = Encoding.ASCII.GetBytes(search);
            var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            for (int i = 0; i < 2; i++) await udp.SendAsync(bytes, bytes.Length, target);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            while (!cts.IsCancellationRequested)
            {
                var resp = await udp.ReceiveAsync(cts.Token);
                var text = Encoding.ASCII.GetString(resp.Buffer);
                var loc  = Regex.Match(text, @"LOCATION:\s*(\S+)", RegexOptions.IgnoreCase);
                if (loc.Success && await ResolveControlUrlAsync(loc.Groups[1].Value, ct)) return true;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[Lan] discover failed: {ex.Message}"); }
        return _controlUrl != null;
    }

    private async Task<bool> ResolveControlUrlAsync(string descUrl, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var xml = await http.GetStringAsync(descUrl, ct);

            foreach (var svc in new[] { "WANIPConnection", "WANPPPConnection" })
            {
                var m = Regex.Match(xml,
                    $@"<service>\s*<serviceType>([^<]*{svc}[^<]*)</serviceType>.*?<controlURL>([^<]+)</controlURL>",
                    RegexOptions.Singleline);
                if (!m.Success) continue;

                _serviceType = m.Groups[1].Value.Trim();
                _controlUrl  = new Uri(new Uri(descUrl), m.Groups[2].Value.Trim()).ToString();
                return true;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Lan] resolve control failed: {ex.Message}"); }
        return false;
    }

    private async Task<bool> AddPortMappingAsync(int port, CancellationToken ct)
    {
        var args =
            $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>TCP</NewProtocol>" +
            $"<NewInternalPort>{port}</NewInternalPort><NewInternalClient>{_localIp}</NewInternalClient>" +
            "<NewEnabled>1</NewEnabled><NewPortMappingDescription>Anchor Launcher</NewPortMappingDescription>" +
            "<NewLeaseDuration>0</NewLeaseDuration>";
        return await SoapAsync("AddPortMapping", args, ct) != null;
    }

    private async Task DeletePortMappingAsync(int port)
    {
        var args = $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>TCP</NewProtocol>";
        await SoapAsync("DeletePortMapping", args, CancellationToken.None);
    }

    private async Task<string?> GetExternalIpAsync(CancellationToken ct)
    {
        var resp = await SoapAsync("GetExternalIPAddress", string.Empty, ct);
        var m = resp != null ? Regex.Match(resp, @"<NewExternalIPAddress>([^<]*)</NewExternalIPAddress>") : Match.Empty;
        return m.Success ? m.Groups[1].Value : null;
    }

    private async Task<string?> SoapAsync(string action, string args, CancellationToken ct)
    {
        if (_controlUrl == null || _serviceType == null) return null;
        try
        {
            var soap =
                "<?xml version=\"1.0\"?>" +
                "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
                "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
                $"<u:{action} xmlns:u=\"{_serviceType}\">{args}</u:{action}>" +
                "</s:Body></s:Envelope>";

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var req  = new HttpRequestMessage(HttpMethod.Post, _controlUrl)
            {
                Content = new StringContent(soap, Encoding.UTF8, "text/xml")
            };
            req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{_serviceType}#{action}\"");

            using var resp = await http.SendAsync(req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode ? text : null;
        }
        catch (Exception ex) { Debug.WriteLine($"[Lan] SOAP {action} failed: {ex.Message}"); return null; }
    }

    /// <summary>The LAN IPv4 this machine would use to reach the internet (no packets are sent).</summary>
    private static string LocalIPv4()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            return (s.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }
}
