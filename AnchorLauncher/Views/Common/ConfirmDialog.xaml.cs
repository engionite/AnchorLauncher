using System.Windows;
using System.Windows.Input;

namespace AnchorLauncher.Views.Common;

/// <summary>
/// Dark, design-system confirmation dialog for destructive actions. Replaces the system
/// MessageBox. Returns DialogResult == true when the destructive action is confirmed.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string subject, string detail, string confirmLabel = "Delete")
    {
        InitializeComponent();
        TitleText.Text   = title;
        SubjectText.Text = subject;
        DetailText.Text  = detail;
        BtnConfirm.Content = confirmLabel;
    }

    /// <summary>Convenience helper: shows the dialog and returns true on confirm.</summary>
    public static bool Show(Window? owner, string title, string subject, string detail, string confirmLabel = "Delete")
    {
        var dlg = new ConfirmDialog(title, subject, detail, confirmLabel);
        if (owner != null) dlg.Owner = owner;
        return dlg.ShowDialog() == true;
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
