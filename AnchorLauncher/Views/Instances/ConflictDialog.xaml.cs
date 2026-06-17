using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Diagnostics;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Instances;

/// <summary>
/// Pre-launch conflict warning. DialogResult == true means "launch" (either Launch Anyway or
/// after Auto-Fix); anything else aborts the launch.
/// </summary>
public partial class ConflictDialog : Window
{
    private readonly MinecraftInstance _instance;
    private readonly List<ModConflict> _conflicts;
    private readonly string _modsFolder;

    public ConflictDialog(List<ModConflict> conflicts, MinecraftInstance instance)
    {
        InitializeComponent();
        _instance   = instance;
        _conflicts  = conflicts;
        _modsFolder = Path.Combine(instance.GameDir, "mods");
        ConflictList.ItemsSource = conflicts;

        // Auto-Fix only helps when there's an incompatibility we can resolve by disabling a jar.
        BtnAutoFix.IsEnabled = conflicts.Any(c => c.Kind == ConflictKind.Incompatible &&
                                                  (!string.IsNullOrEmpty(c.PrimaryFile) || !string.IsNullOrEmpty(c.SecondaryFile)));
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnOpenMods_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_modsFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConflictDialog] open mods folder failed: {ex.Message}");
        }
        DialogResult = false;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// Snapshots the mods folder, then for each incompatibility disables the mod with fewer
    /// dependents (so the fewest other mods break), and launches.
    /// </summary>
    private async void BtnAutoFix_Click(object sender, RoutedEventArgs e)
    {
        BtnAutoFix.IsEnabled = false;
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Safety net before any file change
            await new SnapshotService().TakeSnapshotAsync(_instance, "Before conflict Auto-Fix");

            foreach (var c in _conflicts.Where(c => c.Kind == ConflictKind.Incompatible))
            {
                // Pick the side with fewer dependents (fall back to whichever file we know).
                string? target;
                if (!string.IsNullOrEmpty(c.PrimaryFile) && !string.IsNullOrEmpty(c.SecondaryFile))
                    target = c.PrimaryDependents <= c.SecondaryDependents ? c.PrimaryFile : c.SecondaryFile;
                else
                    target = c.PrimaryFile ?? c.SecondaryFile;

                if (string.IsNullOrEmpty(target) || disabled.Contains(target)) continue;

                var jar = Path.Combine(_modsFolder, target);
                var off = jar + ".disabled";
                if (File.Exists(jar))
                {
                    if (File.Exists(off)) File.Delete(off);
                    File.Move(jar, off);
                    disabled.Add(target);
                    Debug.WriteLine($"[ConflictDialog] Auto-Fix disabled '{target}'.");
                }
            }

            var summary = disabled.Count > 0
                ? $"[Anchor] Auto-Fix disabled {disabled.Count} mod(s) to resolve conflicts: {string.Join(", ", disabled)}. Restore from Snapshot History if needed."
                : "[Anchor] Auto-Fix found nothing it could safely disable.";
            InstancesViewModel.Shared.LogExternal(summary);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConflictDialog] Auto-Fix failed: {ex}");
            InstancesViewModel.Shared.LogExternal($"[Anchor] Auto-Fix failed: {ex.Message}");
        }

        DialogResult = true;   // proceed to launch
        Close();
    }
}
