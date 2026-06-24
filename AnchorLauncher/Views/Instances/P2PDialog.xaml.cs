using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Services.Net;
using AnchorLauncher.Services.Platform;

namespace AnchorLauncher.Views.Instances;

/// <summary>"Play with a friend": forwards a port (auto-detected from Open-to-LAN, or typed in for a
/// dedicated server) through the router via UPnP and shows the public address to share. Direct
/// peer-to-peer — no relay server, no account.</summary>
public partial class P2PDialog : Window
{
    private readonly LanTunnelService _svc = new();
    private readonly CancellationTokenSource _cts = new();
    private bool   _hosting;
    private string _address = string.Empty;

    public P2PDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await DetectAsync();
    }

    private async System.Threading.Tasks.Task DetectAsync()
    {
        StatusText.Text = Loc.I["p2p_detecting"];
        int? port = null;
        try { port = await _svc.DetectLanPortAsync(TimeSpan.FromSeconds(6), _cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Debug.WriteLine($"[P2P] detect failed: {ex}"); }

        if (port is { } p)
        {
            PortBox.Text    = p.ToString();
            StatusText.Text = string.Format(Loc.I["p2p_detected"], p);
        }
        else
        {
            StatusText.Text = Loc.I["p2p_enterport"];
        }
    }

    private async void Host_Click(object sender, RoutedEventArgs e)
    {
        if (_hosting) { await StopHostingAsync(); return; }

        if (!int.TryParse(PortBox.Text?.Trim(), out var port) || port is <= 0 or > 65535)
        {
            StatusText.Text = Loc.I["p2p_enterport"];
            return;
        }

        HostButton.Visibility = Visibility.Collapsed;
        PortBox.IsEnabled     = false;
        StatusText.Text       = Loc.I["p2p_working"];
        try
        {
            var res = await _svc.HostAsync(port, _cts.Token);
            if (res.Status == LanTunnelService.HostStatus.Ok)
            {
                _hosting                = true;
                _address                = $"{res.PublicIp}:{res.Port}";
                AddressText.Text        = _address;
                StatusText.Text         = Loc.I["p2p_ready"];
                AddressPanel.Visibility = Visibility.Visible;
                HostButton.Content      = Loc.I["p2p_stop"];
                HostButton.Visibility   = Visibility.Visible;
            }
            else
            {
                StatusText.Text       = Loc.I["p2p_noupnp"];
                PortBox.IsEnabled     = true;
                HostButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[P2P] host failed: {ex}");
            StatusText.Text       = Loc.I["p2p_noupnp"];
            PortBox.IsEnabled     = true;
            HostButton.Visibility = Visibility.Visible;
        }
    }

    private async System.Threading.Tasks.Task StopHostingAsync()
    {
        await _svc.StopAsync();
        _hosting                = false;
        AddressPanel.Visibility = Visibility.Collapsed;
        HostButton.Content      = Loc.I["p2p_host"];
        PortBox.IsEnabled       = true;
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
