using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Settings;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        // Settings auto-save on change (debounced). Flush any pending write the
        // moment the user navigates away, so nothing is lost on a fast page switch.
        Unloaded += async (_, _) =>
        {
            if (DataContext is SettingsViewModel vm)
                await vm.FlushAsync();
        };
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsPage] open link failed: {ex.Message}");
        }
        e.Handled = true;
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var dlg = new OpenFileDialog
        {
            Title  = "Select Java executable",
            Filter = "Java executable (java.exe;javaw.exe)|java.exe;javaw.exe|Executable (*.exe)|*.exe"
        };
        if (dlg.ShowDialog() == true)
            vm.SetJavaPath(dlg.FileName);
    }

    private void BrowseCloudPath_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var dlg = new OpenFolderDialog { Title = "Select sync folder" };
        if (dlg.ShowDialog() == true)
            vm.SetCloudCustomPath(dlg.FolderName);
    }

    private void BrowseScript_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var dlg = new OpenFileDialog
        {
            Title  = "Select pre-launch script",
            Filter = "Scripts (*.bat;*.cmd;*.ps1;*.sh)|*.bat;*.cmd;*.ps1;*.sh|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            vm.SetPreLaunchScript(dlg.FileName);
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        var confirmed = Common.ConfirmDialog.Show(
            Window.GetWindow(this),
            "Reset all settings",
            "All launcher settings",
            "Everything returns to defaults. Instances and accounts are kept.",
            "Reset");

        if (confirmed)
            vm.ResetSettingsCommand.Execute(null);
    }
}
