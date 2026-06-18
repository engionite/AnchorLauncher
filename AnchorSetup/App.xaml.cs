using System.Windows;

namespace AnchorSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Add/Remove Programs calls "<installdir>\uninstall.exe --uninstall".
        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            new UninstallWindow().Show();
            return;
        }

        new MainWindow().Show();
    }
}
