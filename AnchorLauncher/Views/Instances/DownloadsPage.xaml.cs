using System.Windows.Controls;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Instances;

public partial class DownloadsPage : Page
{
    public DownloadsPage() => InitializeComponent();

    // Infinite scroll — load the next page when the user nears the bottom.
    private void Results_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer sv) return;
        if (DataContext is not MarketplaceViewModel vm) return;

        if (sv.ScrollableHeight > 0 && sv.VerticalOffset >= sv.ScrollableHeight - 240)
            vm.LoadMoreCommand.Execute(null);
    }
}
