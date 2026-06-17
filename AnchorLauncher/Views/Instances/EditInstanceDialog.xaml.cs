using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Instances;

public partial class EditInstanceDialog : Window
{
    private readonly EditInstanceViewModel _vm;

    public EditInstanceDialog(MinecraftInstance instance)
    {
        InitializeComponent();
        _vm = new EditInstanceViewModel(instance);
        DataContext = _vm;
        _vm.Closed += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        _vm.DownloadShadersRequested += (_, _) =>
        {
            DialogResult = false;
            Close();
            (Application.Current.MainWindow as MainWindow)
                ?.NavigateToMarketplace(Models.Marketplace.ProjectType.Shader);
        };
        Closed += (_, _) => _vm.Shutdown(); // release the shaderpacks FileSystemWatcher
    }

    // ── Shaderpack drag-and-drop ────────────────────────────────────────────────

    private void Shaderpacks_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Shaderpacks_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _vm.InstallDroppedShaderpacks(files);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select Java executable",
            Filter = "Java executable (java.exe;javaw.exe)|java.exe;javaw.exe|Executable (*.exe)|*.exe"
        };
        if (dlg.ShowDialog() == true)
            _vm.SetJavaPath(dlg.FileName);
    }

    private void WorldDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorldSaveEntry world }) return;

        var confirmed = Common.ConfirmDialog.Show(
            this,
            "Delete world",
            world.Name,
            "This cannot be undone — back it up first if unsure.");

        if (confirmed)
            _vm.DeleteWorld(world);
    }
}
