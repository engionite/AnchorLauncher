using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Services.Net;
using AnchorLauncher.Services.Platform;

namespace AnchorLauncher.Views.Instances;

/// <summary>"Play with a friend": detects your Open-to-LAN port, forwards it via UPnP, and shows the
/// public address to share. Direct peer-to-peer — no relay server, no account.</summary>
public partial class P2PDialog : Window
{
    private readonly LanTunnelService _svc = new();
    private readonly CancellationTokenSource _cts = new();
    private bool   _hosting;
    private string _address = string.Empty;

    public P2PDialog() => InitializeComponent();

    private async void Host_Click(object sender, RoutedEventArgs e)
    {
        if (_hosting) { await StopHostingAsync(); return; }

        HostButton.IsEnabled = false;
        StatusText.Text = Loc.I["p2p_working"];
        try
        {
            var port = await _svc.DetectLanPortAsync(TimeSpan.FromSeconds(6), _cts.Token);
            if (port == null)
            {
                StatusText.Text = Loc.I["p2p_nolan"];
                return;
            }

            var res = await _svc.HostAsync(port.Value, _cts.Token);
            if (res.Status == LanTunnelService.HostStatus.Ok)
            {
                _hosting = true;
                _address = $"{res.PublicIp}:{res.Port}";
                AddressText.Text          = _address;
                StatusText.Text           = Loc.I["p2p_ready"];
                AddressPanel.Visibility   = Visibility.Visible;
                HostButton.Content        = Loc.I["p2p_stop"];
            }
            else if (res.Status == LanTunnelService.HostStatus.NoLanWorld)
                StatusText.Text = Loc.I["p2p_nolan"];
            else
                StatusText.Text = Loc.I["p2p_noupnp"];
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[P2P] host failed: {ex}"); StatusText.Text = Loc.I["p2p_noupnp"]; }
        finally { HostButton.IsEnabled = true; }
    }

    private async System.Threading.Tasks.Task StopHostingAsync()
    {
        await _svc.StopAsync();
        _hosting = false;
        AddressPanel.Visibility = Visibility.Collapsed;
        HostButton.Content      = Loc.I["p2p_host"];
        StatusText.Text         = string.Empty;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_address); CopyButton.Content = Loc.I["p2p_copied"]; }
        catch (Exception ex) { Debug.WriteLine($"[P2P] copy failed: {ex.Message}"); }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { }
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        try { _cts.Cancel(); } catch { }
        if (_hosting) { try { await _svc.StopAsync(); } catch { } }
        _svc.Dispose();
        Close();
    }
}
