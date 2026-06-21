using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Home;

namespace AnchorLauncher.Views.Home;

/// <summary>Expanded view of an Anchor News item: full text plus links to the repository and,
/// for posts that announce a release, that specific GitHub release.</summary>
public partial class NewsDetailDialog : Window
{
    private const string Repo = "https://github.com/engionite/AnchorLauncher";
    private readonly string? _releaseTag;

    public NewsDetailDialog(NewsItem item)
    {
        InitializeComponent();
        BadgeText.Text = item.Date;
        TitleText.Text = item.Title;
        BodyText.Text  = item.Summary;
        _releaseTag    = item.ReleaseTag;

        // Only release posts get the "View this release" button.
        if (string.IsNullOrWhiteSpace(_releaseTag))
            ReleaseButton.Visibility = Visibility.Collapsed;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e)   => Close();
    private void GitHub_Click(object sender, RoutedEventArgs e)  => Open(Repo);
    private void Release_Click(object sender, RoutedEventArgs e) => Open($"{Repo}/releases/tag/{_releaseTag}");

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Debug.WriteLine($"[NewsDetail] open failed: {ex.Message}"); }
    }
}
