using System.Windows;

namespace AnchorSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Run as the uninstaller when Add/Remove Programs passes --uninstall, OR when this exe is
        // simply named "uninstall.exe" (so double-clicking it in the install folder also uninstalls
        // instead of relaunching the installer).
        var exeName = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        var isUninstaller = e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            || exeName.Equals("uninstall", StringComparison.OrdinalIgnoreCase);

        if (isUninstaller)
        {
            new UninstallWindow().Show();
            return;
        }

        new MainWindow().Show();
    }
}
