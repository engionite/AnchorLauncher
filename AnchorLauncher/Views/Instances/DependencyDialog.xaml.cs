using System.Windows;
using System.Windows.Input;

namespace AnchorLauncher.Views.Instances;

/// <summary>Asks whether to also install a mod's required dependencies (e.g. Fabric API).
/// <see cref="Window.ShowDialog"/> returns true to install them, false/closed to skip.</summary>
public partial class DependencyDialog : Window
{
    public DependencyDialog(string modName, IEnumerable<string> dependencyNames)
    {
        InitializeComponent();
        SubjectText.Text    = modName;
        DepList.ItemsSource = dependencyNames.ToList();
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)    { DialogResult = false; Close(); }
    private void Install_Click(object sender, RoutedEventArgs e) { DialogResult = true;  Close(); }
}
