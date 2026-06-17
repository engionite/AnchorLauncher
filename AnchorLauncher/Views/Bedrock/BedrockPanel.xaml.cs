using System.Windows.Controls;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Bedrock;

public partial class BedrockPanel : UserControl
{
    public BedrockPanel()
    {
        InitializeComponent();
        DataContext = new BedrockViewModel();
    }
}
