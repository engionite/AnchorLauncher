using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Services.Platform;

namespace AnchorLauncher.Views;

public partial class UpdateDialog : Window
{
    private readonly string? _url;
    private readonly CancellationTokenSource _cts = new();
    private bool _showWebFallback;

    public UpdateDialog(string latest, string current, string notes, string? url)
    {
        InitializeComponent();
        _url = url;

        VersionText.Text = $"v{latest}";
        CurrentText.Text = $"{Loc.I["upd_youron"]} v{current}";
        if (string.IsNullOrWhiteSpace(notes))
            NotesText.Text = string.Empty;
        else
            NotesText.Text = notes;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            try { DragMove(); } catch { }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        // After a failure the primary button becomes "Open download page".
        if (_showWebFallback) { OpenPage(); Close(); return; }

        // Not an installed/writable build (e.g. running from a dev folder) — just open the page.
        if (!SelfUpdateService.CanSelfUpdate()) { OpenPage(); Close(); return; }

        PromptButtons.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<double>(p =>
        {
            Bar.Value = p;
            PctText.Text = $"{p:0}%";
        });

        try
        {
            await SelfUpdateService.DownloadAndApplyAsync(progress, _cts.Token);
            // The new build is already launching — force-exit so we never leave two launchers
            // running (Application.Shutdown can linger if the tray icon/threads keep the process up).
            Environment.Exit(0);
        }
        catch (OperationCanceledException) { /* dialog closed mid-download */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SelfUpdate] failed: {ex.Message}");
            SelfUpdateService.LogFailure(ex);   // persist the real reason for diagnosis
            ProgressPanel.Visibility = Visibility.Collapsed;
            FailText.Visibility = Visibility.Visible;
            _showWebFallback = true;
            UpdateButton.Content = Loc.I["upd_openpage"];
            PromptButtons.Visibility = Visibility.Visible;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        try { _cts.Cancel(); } catch { }
        Close();
    }

    private void OpenPage()
    {
        if (string.IsNullOrWhiteSpace(_url)) return;
        try { Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[SelfUpdate] open page failed: {ex.Message}"); }
    }
}
