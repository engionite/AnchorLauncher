using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Instances;

public partial class CreateInstanceDialog : Window
{
    private readonly CreateInstanceViewModel _vm = new();

    /// <summary>Set when creation succeeds; the caller adds it to the list.</summary>
    public MinecraftInstance? ResultInstance { get; private set; }

    public CreateInstanceDialog()
    {
        InitializeComponent();
        DataContext = _vm;
        _vm.Created += OnCreated;
        Loaded += (_, _) => _vm.LoadVersionsCommand.Execute(null);
    }

    private void OnCreated(object? sender, MinecraftInstance instance)
    {
        ResultInstance = instance;
        DialogResult   = true;
        Close();
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
