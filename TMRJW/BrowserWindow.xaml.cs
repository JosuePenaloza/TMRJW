using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;
using System.Windows.Input; // añadido para MouseButtonEventArgs
using System.Net.Http;
using System.IO;
using System.Runtime.InteropServices;

namespace TMRJW
{
    // Objeto expuesto a JavaScript => debe ser COM visible
    [ComVisible(true)]
    public class ScriptBridge
    {
        private readonly BrowserWindow _parent;
        public ScriptBridge(BrowserWindow parent) => _parent = parent;
        // Método invocado desde JS: recibe la URL de la imagen
        public void ImageClicked(string url) => _parent.OnImageUrlClicked(url);
    }

    public partial class BrowserWindow : Window
    {
        private Action<BitmapImage>? _onImageSelected;
        private CancellationTokenSource? _cts;
        private static readonly HttpClient s_http = new HttpClient();

        public BrowserWindow()
        {
            InitializeComponent();
            UrlBox.Text = "https://wol.jw.org/es/wol/meetings/r4/lp-s/";

            // permitir seleccionar con un solo click (MouseLeftButtonUp)
            ImagesListBox.MouseLeftButtonUp += ImagesListBox_MouseLeftButtonUp;

            // silenciar errores de script en el WebBrowser
            WebBrowserControl.Navigated += WebBrowserControl_Navigated;

            // exponer objeto COM para que JS pueda llamar a window.external.ImageClicked(...)
            try
            {
                WebBrowserControl.ObjectForScripting = new ScriptBridge(this);
            }
            catch
            {
                // ignorar si no es posible
            }
        }

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
            InjectClickScript(); // inyectar handlers para clicks dentro de la página
        }

        // NOTE: handler BtnRefreshImages_Click está implementado en BrowserWindow.Events.cs
        // para evitar definición duplicada lo eliminé de este archivo.

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

        // Nuevo: Click simple en la miniatura seleccionada
        private void ImagesListBox_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
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

        // Mantener doble click por compatibilidad (ya existía)
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

        // Silenciar errores de script en el WebBrowser (usa ActiveX.Silent)
        private void WebBrowserControl_Navigated(object? sender, NavigationEventArgs e)
        {
            try
            {
                dynamic? activeX = WebBrowserControl.GetType().InvokeMember("ActiveXInstance",
                    System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, WebBrowserControl, new object[] { });

                if (activeX != null)
                {
                    try { activeX.Silent = true; } catch { /* ignorar si la propiedad no existe */ }
                }
            }
            catch
            {
                // ignorar si no se puede establecer Silent
            }

            // también intentar inyectar script después de navegación
            InjectClickScript();
        }

        // Método llamado por ScriptBridge cuando JS hace click en una imagen dentro de la página
        public async void OnImageUrlClicked(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                // resolver URL relativa respecto a la página si es necesario
                Uri? finalUri = null;
                try
                {
                    var baseUri = WebBrowserControl.Source ?? new Uri(UrlBox.Text);
                    finalUri = new Uri(baseUri, url);
                }
                catch
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? tmp) || tmp == null)
                        return;
                    finalUri = tmp;
                }

                if (finalUri == null) return;

                var bmp = await DownloadBitmapFromUrlAsync(finalUri).ConfigureAwait(false);
                if (bmp != null)
                {
                    // actualizar UI y notificar callback (proyección) en UI thread
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ImagesListBox.SelectedItem = null; // evitar confusiones
                        try { _onImageSelected?.Invoke(bmp); } catch { }
                    });
                }
            }
            catch
            {
                // ignorar
            }
        }

        private async Task<BitmapImage?> DownloadBitmapFromUrlAsync(Uri uri)
        {
            try
            {
                var bytes = await s_http.GetByteArrayAsync(uri).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0) return null;

                return await Task.Run(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                    catch
                    {
                        return null;
                    }
                }).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        // Inyecta un script sencillo que escucha clicks y llama a window.external.ImageClicked(url)
        private void InjectClickScript()
        {
            try
            {
                dynamic doc = WebBrowserControl.Document;
                if (doc == null) return;

                string js = @"
                        (function(){
                            try {
                                function sendUrl(u){ try { if(u) window.external.ImageClicked(u); } catch(e){} }
                                document.addEventListener('click', function(ev){
                                    var t = ev.target;
                                    while(t && t.tagName !== 'IMG') { t = t.parentElement; }
                                    if(t && t.src) { sendUrl(t.src); return; }
                                    // fallback: si el elemento tiene background-image en estilo, intentar extraer url(...)
                                    if(t){
                                        var bg = window.getComputedStyle(t).backgroundImage;
                                        if(bg && bg.indexOf('url(') !== -1){
                                            var m = /url\\(['""]?(.*?)['""]?\\)/.exec(bg);
                                            if(m && m[1]) sendUrl(m[1]);
                                        }
                                    }
                                }, true);
                            } catch(e){}
                        })();";

                // Ejecutar en el contexto de la página
                try
                {
                    doc.parentWindow.execScript(js, "JavaScript");
                }
                catch
                {
                    // fallback para algunos documentos
                    try { WebBrowserControl.InvokeScript("eval", new object[] { js }); } catch { }
                }
            }
            catch
            {
                // ignorar
            }
        }
    }
}
