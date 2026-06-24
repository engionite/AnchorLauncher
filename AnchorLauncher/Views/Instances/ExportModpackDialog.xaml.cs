using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Platform;

namespace AnchorLauncher.Views.Instances;

/// <summary>Exports an instance to a Modrinth .mrpack with progress, then offers to reveal the file.</summary>
public partial class ExportModpackDialog : Window
{
    private readonly MinecraftInstance     _instance;
    private readonly string                _destPath;
    private readonly ModpackExportService  _service = new();
    private readonly CancellationTokenSource _cts = new();

    public ExportModpackDialog(MinecraftInstance instance, string destPath)
    {
        InitializeComponent();
        _instance = instance;
        _destPath = destPath;
        SubtitleText.Text = instance.Name;
        Loaded += async (_, _) => await RunAsync();
    }

    private async System.Threading.Tasks.Task RunAsync()
    {
        StatusText.Text          = Loc.I["exp_working"];
        ProgressPanel.Visibility = Visibility.Visible;

        var progress = new Progress<DownloadProgress>(p => { Bar.Value = p.Percent; CurrentText.Text = p.Status; });
        try
        {
            await _service.ExportMrpackAsync(_instance, _destPath, progress, _cts.Token);
            StatusText.Text             = Loc.I["exp_done"];
            OpenFolderButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Export] failed: {ex}");
            StatusText.Text = Loc.I["exp_failed"];
        }

        ProgressPanel.Visibility = Visibility.Collapsed;
        CurrentText.Text         = string.Empty;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_destPath}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[Export] reveal failed: {ex.Message}"); }
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
