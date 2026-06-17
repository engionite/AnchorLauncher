using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Instances;

public partial class VersionSwitchDialog : Window
{
    private readonly VersionSwitchViewModel _vm;

    public VersionSwitchDialog(MinecraftInstance instance)
    {
        InitializeComponent();
        _vm = new VersionSwitchViewModel(instance);
        DataContext = _vm;
        _vm.Completed += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
