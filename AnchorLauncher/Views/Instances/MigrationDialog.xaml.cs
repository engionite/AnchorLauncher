using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Platform;

namespace AnchorLauncher.Views.Instances;

/// <summary>Scans other launchers (Prism, CurseForge, Modrinth, official) and imports their
/// instances into Anchor. Returns true if at least one instance was imported.</summary>
public partial class MigrationDialog : Window
{
    private readonly LauncherMigrationService _service = new();
    private readonly CancellationTokenSource  _cts = new();
    private List<DiscoveredInstance> _found = new();
    private bool _imported;

    public MigrationDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ScanAsync();
    }

    private async System.Threading.Tasks.Task ScanAsync()
    {
        StatusText.Text = Loc.I["mig_scanning"];
        try
        {
            _found = await System.Threading.Tasks.Task.Run(_service.Scan);
        }
        catch (Exception ex) { Debug.WriteLine($"[Migrate] scan failed: {ex}"); _found = new(); }

        if (_found.Count == 0)
        {
            StatusText.Text = Loc.I["mig_none"];
            return;
        }

        StatusText.Text          = string.Empty;
        FoundList.ItemsSource     = _found;
        ListContainer.Visibility  = Visibility.Visible;
        ImportButton.Visibility   = Visibility.Visible;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var selected = _found.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) { Close(); return; }

        ImportButton.Visibility  = Visibility.Collapsed;
        ListContainer.Visibility = Visibility.Collapsed;
        StatusText.Text          = Loc.I["mig_importing"];
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<DownloadProgress>(p => { Bar.Value = p.Percent; CurrentText.Text = p.Status; });
        int done = 0;
        foreach (var d in selected)
        {
            try { await _service.ImportAsync(d, progress, _cts.Token); done++; }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Debug.WriteLine($"[Migrate] import '{d.Name}' failed: {ex}"); }
        }

        ProgressPanel.Visibility  = Visibility.Collapsed;
        CurrentText.Text          = string.Empty;
        ImportButton.Visibility   = Visibility.Collapsed;
        StatusText.Text           = string.Format(Loc.I["mig_done"], done);
        _imported                 = done > 0;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        try { _cts.Cancel(); } catch { }
        try { DialogResult = _imported; } catch { Close(); }
    }
}
