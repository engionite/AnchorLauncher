using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AnchorLauncher.Converters;

namespace AnchorLauncher.Views.Instances;

/// <summary>Per-instance icon picker: a grid of built-in icons plus the user's uploaded ones,
/// with an "Upload custom…" path. Returns the chosen <see cref="SelectedIconId"/> on OK.</summary>
public partial class IconPickerDialog : Window
{
    public sealed class IconChoice
    {
        public string Id { get; init; } = "";
        public ImageSource? Image { get; init; }
    }

    public string? SelectedIconId { get; private set; }

    private readonly ObservableCollection<IconChoice> _choices = new();

    public IconPickerDialog(string? currentIconId)
    {
        InitializeComponent();
        BuildChoices();
        IconList.ItemsSource = _choices;

        // Pre-select the instance's current icon (default → "grass")
        var current = string.IsNullOrWhiteSpace(currentIconId) ? InstanceIconCatalog.Default : currentIconId!;
        var match = _choices.FirstOrDefault(c => string.Equals(c.Id, current, StringComparison.OrdinalIgnoreCase));
        IconList.SelectedItem = match ?? _choices.FirstOrDefault();
    }

    private void BuildChoices()
    {
        _choices.Clear();

        // Built-in icons
        foreach (var key in InstanceIconCatalog.BuiltIn)
        {
            if (Application.Current?.TryFindResource("III_" + key) is ImageSource src)
                _choices.Add(new IconChoice { Id = key, Image = src });
        }

        // Previously-uploaded custom icons
        try
        {
            var dir = InstanceIconCatalog.IconsDir;
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir)
                             .Where(f => IsImage(f))
                             .OrderByDescending(File.GetLastWriteTimeUtc))
                    _choices.Add(new IconChoice { Id = file, Image = InstanceIconConverter.LoadFile(file) });
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[IconPicker] scan customs failed: {ex.Message}"); }
    }

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    private void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Choose an image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var dir = InstanceIconCatalog.IconsDir;
            Directory.CreateDirectory(dir);
            // Copy under a unique name so the original can move/delete without breaking the icon
            var dest = Path.Combine(dir, $"{Guid.NewGuid():N}{Path.GetExtension(dlg.FileName)}");
            File.Copy(dlg.FileName, dest, overwrite: true);

            var choice = new IconChoice { Id = dest, Image = InstanceIconConverter.LoadFile(dest) };
            _choices.Insert(0, choice);
            IconList.SelectedItem = choice;
            IconList.ScrollIntoView(choice);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IconPicker] upload failed: {ex.Message}");
            MessageBox.Show(this, "Couldn't import that image.", "Upload failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void IconList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IconList.SelectedItem is IconChoice c) PreviewImage.Source = c.Image;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        SelectedIconId = (IconList.SelectedItem as IconChoice)?.Id;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
}
