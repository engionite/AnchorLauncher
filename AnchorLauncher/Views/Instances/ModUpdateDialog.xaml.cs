using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Platform;

namespace AnchorLauncher.Views.Instances;

/// <summary>Scans an instance's mods against Modrinth (by hash) and updates them with one click.</summary>
public partial class ModUpdateDialog : Window
{
    private readonly MinecraftInstance _instance;
    private readonly ModUpdateService  _service = new();
    private readonly CancellationTokenSource _cts = new();
    private List<ModUpdateService.ModUpdate> _updates = new();

    public ModUpdateDialog(MinecraftInstance instance)
    {
        InitializeComponent();
        _instance = instance;
        SubtitleText.Text = instance.Name;
        Loaded += async (_, _) => await CheckAsync();
    }

    private async System.Threading.Tasks.Task CheckAsync()
    {
        StatusText.Text = Loc.I["mu_checking"];
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<DownloadProgress>(p => { Bar.Value = p.Percent; CurrentText.Text = p.Status; });
        try
        {
            _updates = await _service.CheckAsync(_instance, progress, _cts.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Debug.WriteLine($"[ModUpdate] check failed: {ex}"); }

        ProgressPanel.Visibility = Visibility.Collapsed;
        CurrentText.Text = string.Empty;

        if (_updates.Count == 0)
        {
            StatusText.Text = Loc.I["mu_uptodate"];
            return;
        }

        StatusText.Text = string.Format(Loc.I["mu_available"], _updates.Count);
        UpdatesList.ItemsSource    = _updates;
        ListContainer.Visibility   = Visibility.Visible;
        UpdateAllButton.Visibility = Visibility.Visible;
    }

    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.Count == 0) { Close(); return; }

        UpdateAllButton.IsEnabled = false;
        ListContainer.Visibility  = Visibility.Collapsed;
        StatusText.Text           = Loc.I["mu_updating"];
        ProgressPanel.Visibility  = Visibility.Visible;

        var progress = new Progress<DownloadProgress>(p => { Bar.Value = p.Percent; CurrentText.Text = p.Status; });
        try
        {
            await _service.ApplyAsync(_instance, _updates, progress, _cts.Token);
            StatusText.Text = string.Format(Loc.I["mu_done"], _updates.Count);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Debug.WriteLine($"[ModUpdate] apply failed: {ex}"); }

        ProgressPanel.Visibility   = Visibility.Collapsed;
        CurrentText.Text           = string.Empty;
        UpdateAllButton.Visibility = Visibility.Collapsed;
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
