using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Platform;

namespace AnchorLauncher.Views.Instances;

/// <summary>Scans all instances for duplicate mods/packs and reclaims the space via hard links.</summary>
public partial class DedupDialog : Window
{
    private readonly DeduplicationService _service = new();
    private readonly CancellationTokenSource _cts = new();

    public DedupDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ScanAsync();
    }

    private async System.Threading.Tasks.Task ScanAsync()
    {
        StatusText.Text          = Loc.I["dd_scanning"];
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<DownloadProgress>(p => { Bar.Value = p.Percent; CurrentText.Text = p.Status; });
        DeduplicationService.DedupReport report;
        try { report = await _service.ScanAsync(progress, _cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Debug.WriteLine($"[Dedup] scan failed: {ex}"); StatusText.Text = Loc.I["dd_none"]; ProgressPanel.Visibility = Visibility.Collapsed; return; }

        ProgressPanel.Visibility = Visibility.Collapsed;
        CurrentText.Text         = string.Empty;

        if (report.Bytes <= 0) { StatusText.Text = Loc.I["dd_none"]; return; }

        StatusText.Text         = string.Format(Loc.I["dd_found"], report.Files, FormatBytes(report.Bytes));
        ReclaimButton.Visibility = Visibility.Visible;
    }

    private async void Reclaim_Click(object sender, RoutedEventArgs e)
    {
        ReclaimButton.IsEnabled  = false;
        StatusText.Text          = Loc.I["dd_reclaiming"];
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<DownloadProgress>(p => { Bar.Value = p.Percent; CurrentText.Text = p.Status; });
        try
        {
            var report = await _service.ReclaimAsync(progress, _cts.Token);
            StatusText.Text = string.Format(Loc.I["dd_done"], FormatBytes(report.Bytes));
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Debug.WriteLine($"[Dedup] reclaim failed: {ex}"); }

        ProgressPanel.Visibility = Visibility.Collapsed;
        CurrentText.Text         = string.Empty;
        ReclaimButton.Visibility = Visibility.Collapsed;
    }

    private static string FormatBytes(long bytes)
    {
        double b = bytes;
        if (b >= 1024L * 1024 * 1024) return $"{b / (1024 * 1024 * 1024):0.0} GB";
        if (b >= 1024L * 1024)        return $"{b / (1024 * 1024):0.0} MB";
        if (b >= 1024)                return $"{b / 1024:0.0} KB";
        return $"{bytes} B";
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        try { _cts.Cancel(); } catch { }
        Close();
    }
}
