using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using AnchorSetup.Localization;
using AnchorSetup.Services;

namespace AnchorSetup;

public partial class UninstallWindow : Window
{
    public UninstallWindow()
    {
        InitializeComponent();
        SetupLoc.I.SetLanguage(DetectLanguage());

        // Add/Remove Programs "quiet uninstall" path: remove without prompting.
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
            Loaded += async (_, __) => await RemoveAsync();
    }

    /// <summary>Match the launcher's chosen language so uninstall reads naturally; fall back to OS.</summary>
    private static string DetectLanguage()
    {
        try
        {
            var path = Path.Combine(InstallService.AnchorDataDir, "settings.json");
            if (File.Exists(path) &&
                JsonNode.Parse(File.ReadAllText(path)) is JsonObject o &&
                o["Language"]?.GetValue<string>() is { } display)
            {
                int a = display.IndexOf('('), b = display.IndexOf(')');
                var inside = (a >= 0 && b > a) ? display.Substring(a + 1, b - a - 1) : display;
                var code = inside.Split('-', '_')[0].Trim().ToLowerInvariant();
                if (code.Length == 2) return code;
            }
        }
        catch { /* fall through */ }
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
    }

    private async void Remove_Click(object sender, RoutedEventArgs e) => await RemoveAsync();

    private async Task RemoveAsync()
    {
        var deleteData = DeleteDataCheck.IsChecked == true;

        PromptPanel.Visibility = Visibility.Collapsed;
        FooterPanel.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        StatusText.Text = SetupLoc.I["un_removing"];

        await Task.Run(() => InstallService.Uninstall(deleteData));

        StatusText.Text = SetupLoc.I["un_done"];
        await Task.Delay(800);
        Application.Current.Shutdown();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void Chrome_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            try { DragMove(); } catch { }
    }
}
