using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;

namespace TMRJW
{
    public partial class BrowserWindow : Window
    {
        // Quitar readonly para permitir asignación posterior
        private Action<BitmapImage>? _onImageSelected;
        private CancellationTokenSource? _cts;

        // Constructor sin parámetros (asegura compatibilidad con XAML)
        public BrowserWindow()
        {
            InitializeComponent();
            UrlBox.Text = "https://wol.jw.org/es/wol/meetings/r4/lp-s/";
        }

        // Permite asignar callback después de crear la ventana
        public void SetImageSelectedCallback(Action<BitmapImage> callback)
        {
            _onImageSelected = callback;
        }

        private void BtnGo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = UrlBox.Text.Trim();
                if (!string.IsNullOrEmpty(url))
                    WebBrowserControl.Navigate(url);
            }
            catch { }
        }

        private async void WebBrowserControl_LoadCompleted(object sender, NavigationEventArgs e)
        {
            await RefreshImagesFromCurrentPageAsync();
        }

        private async void BtnRefreshImages_Click(object sender, RoutedEventArgs e)
        {
            await RefreshImagesFromCurrentPageAsync();
        }

        private async Task RefreshImagesFromCurrentPageAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                string? url = WebBrowserControl.Source?.AbsoluteUri ?? UrlBox.Text;
                if (string.IsNullOrWhiteSpace(url)) return;

                UrlBox.Text = url;

                var uris = await OnlineLibraryHelper.ExtractImageUrisFromPageAsync(url, _cts.Token).ConfigureAwait(false);
                if (!uris.Any())
                {
                    await Dispatcher.InvokeAsync(() => ImagesListBox.ItemsSource = null);
                    return;
                }

                var list = uris.Take(20).ToList();
                var images = await OnlineLibraryHelper.DownloadImagesAsync(list, _cts.Token).ConfigureAwait(false);

                await Dispatcher.InvokeAsync(() =>
                {
                    ImagesListBox.ItemsSource = images;
                });
            }
            catch
            {
                // ignorar
            }
        }

        private void ImagesListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ImagesListBox.SelectedItem is BitmapImage bi)
            {
                try
                {
                    _onImageSelected?.Invoke(bi);
                }
                catch { }
            }
        }
    }
}
