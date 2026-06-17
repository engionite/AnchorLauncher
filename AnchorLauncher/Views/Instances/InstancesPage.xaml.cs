using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Instances;

public partial class InstancesPage : Page
{
    private readonly InstancesViewModel _vm = InstancesViewModel.Shared;

    public InstancesPage()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Bedrock mode: this page becomes the Bedrock management panel
        if (Services.Storage.LauncherStorageService.CurrentConfig?.Mode == Models.LauncherMode.Bedrock)
        {
            JavaContent.Visibility   = Visibility.Collapsed;
            BedrockHost.Content      = new Bedrock.BedrockPanel();
            BedrockHost.Visibility   = Visibility.Visible;
            return;
        }

        _vm.ConsoleLines.CollectionChanged += OnConsoleChanged;
        ScrollConsoleToEnd();

        // Pick up any card display/sort preferences changed in Settings (no full reload)
        _ = _vm.RefreshDisplayPreferencesAsync();

        // Pre-launch conflict dialog (view-layer orchestration; logic stays in the services/VM)
        _vm.ConfirmConflictLaunch = (conflicts, instance) =>
        {
            var dlg = new ConflictDialog(conflicts, instance)
            {
                Owner = Window.GetWindow(this)
            };
            return dlg.ShowDialog() == true;
        };

        // Crash auto-fix (missing dependency) → jump to the marketplace filtered to that mod
        _vm.RequestMarketplaceForMod = modName =>
        {
            MarketplaceViewModel.PendingSearchQuery = modName;
            (Window.GetWindow(this) as MainWindow)
                ?.NavigateToMarketplace(Models.Marketplace.ProjectType.Mod);
        };
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => _vm.ConsoleLines.CollectionChanged -= OnConsoleChanged;

    private ScrollViewer? _consoleScroll;
    private bool _consoleAtBottom = true;

    private void OnConsoleChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Auto-scroll only when the user is already at the bottom (don't yank them up while reading)
        if (_consoleAtBottom) ScrollConsoleToEnd();
    }

    private void ScrollConsoleToEnd()
    {
        if (ConsoleList.Items.Count > 0)
            ConsoleList.ScrollIntoView(ConsoleList.Items[^1]);
    }

    private void ConsoleList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is ScrollViewer sv) _consoleScroll = sv;
        var distanceFromBottom = e.ExtentHeight - e.VerticalOffset - e.ViewportHeight;
        _consoleAtBottom = distanceFromBottom <= 4;
        ScrollBottomFab.Visibility = distanceFromBottom > 40 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScrollBottom_Click(object sender, RoutedEventArgs e)
    {
        _consoleScroll?.ScrollToBottom();
        ScrollConsoleToEnd();
    }

    // ── New instance ────────────────────────────────────────────────────────────

    private void BtnNewInstance_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new CreateInstanceDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && dlg.ResultInstance != null)
                _vm.AddInstance(dlg.ResultInstance);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesPage] New instance failed: {ex}");
        }
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Import modpack",
                Filter = "Modrinth modpack (*.mrpack)|*.mrpack|Zip archive (*.zip)|*.zip|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
                await _vm.ImportModpackAsync(dlg.FileName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesPage] Import failed: {ex}");
        }
    }

    // ── Context menu ────────────────────────────────────────────────────────────

    private void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceFromMenu(sender) is not { } inst) return;
        try
        {
            var dlg = new EditInstanceDialog(inst) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesPage] Edit failed: {ex}");
        }
    }

    private async void MenuSetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceFromMenu(sender) is not { } inst) return;
        try
        {
            var dlg = new IconPickerDialog(inst.IconId) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
                await _vm.SetInstanceIconAsync(inst, dlg.SelectedIconId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesPage] Set icon failed: {ex}");
        }
    }

    private async void MenuSwitchVersion_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceFromMenu(sender) is not { } inst) return;
        try
        {
            var dlg = new VersionSwitchDialog(inst) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
                await _vm.LoadAsync();   // refresh cards (version label + switch badge)
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesPage] Switch version failed: {ex}");
        }
    }

    private void MenuUndoSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceFromMenu(sender) is { } inst)
            _vm.UndoSwitchCommand.Execute(inst);
    }

    private void MenuClone_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceFromMenu(sender) is { } inst)
            _vm.CloneCommand.Execute(inst);
    }

    private async void MenuExport_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceFromMenu(sender) is not { } inst) return;
        try
        {
            var dlg = new SaveFileDialog
            {
                Title    = "Export instance",
                Filter   = "Zip archive (*.zip)|*.zip",
                FileName = inst.Name + ".zip"
            };
            if (dlg.ShowDialog() == true)
                await _vm.ExportAsync(inst, dlg.FileName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesPage] Export failed: {ex}");
        }
    }

    private async void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (InstanceFromMenu(sender) is not { } inst) return;

        var confirmed = Common.ConfirmDialog.Show(
            Window.GetWindow(this),
            "Delete instance",
            inst.Name,
            "This removes its worlds, mods and config.");

        if (confirmed)
            await _vm.DeleteAsync(inst);
    }

    private static MinecraftInstance? InstanceFromMenu(object sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement fe } })
            return fe.DataContext as MinecraftInstance;
        return null;
    }
}
