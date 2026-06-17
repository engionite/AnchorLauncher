using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Instances;

public partial class SkinsPage : Page
{
    private SkinsViewModel? _vm;

    public SkinsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Bedrock mode: this page becomes the Bedrock management panel
        if (Services.Storage.LauncherStorageService.CurrentConfig?.Mode == Models.LauncherMode.Bedrock)
        {
            SkinsContent.Visibility = Visibility.Collapsed;
            BedrockHost.Content     = new Bedrock.BedrockPanel();
            BedrockHost.Visibility  = Visibility.Visible;
            return;
        }

        if (DataContext is SkinsViewModel vm && _vm != vm)
        {
            if (_vm != null) _vm.SkinChanged -= OnSkinChanged;
            _vm = vm;
            _vm.SkinChanged += OnSkinChanged;
            _vm.PushCurrentSkin();   // sync the viewer in case the skin loaded before we subscribed
        }
    }

    private void OnSkinChanged(object? sender, byte[]? skinBytes)
    {
        try
        {
            if (skinBytes == null) { Viewer.SetSkin(null); return; }

            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(skinBytes))
            {
                bmp.BeginInit();
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            Viewer.SetSkin(bmp, _vm?.IsSlim ?? false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinsPage] OnSkinChanged failed: {ex}");
        }
    }

    private async void BtnChangeSkin_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SkinsViewModel vm) return;

        var dlg = new OpenFileDialog
        {
            Title  = "Select skin (64×64 PNG)",
            Filter = "PNG image (*.png)|*.png"
        };
        if (dlg.ShowDialog() == true)
            await vm.UploadSkinAsync(dlg.FileName);
    }
}
