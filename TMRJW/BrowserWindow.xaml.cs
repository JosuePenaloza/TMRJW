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

        // Nuevos campos para coordinar descargas/proyección
        private Uri? _lastClickedImageUri;
        private bool _isDownloadingImage = false;
        private bool _projectionWaitingForImage = false;

        // Campos para interacción con previsualización (zoom/pan con tecla Espacio)
        private bool _isSpacePressed = false;
        private bool _isPanningPreview = false;
        private Point _lastPreviewMousePos;

        public BrowserWindow()
        {
            InitializeComponent();
            UrlBox.Text = "https://wol.jw.org/es/wol/meetings/r4/lp-s/";

            // Registrar handlers: selección actualiza solo previsualización; doble click/Enter proyectan.
            ImagesListBox.SelectionChanged += ImagesListBox_SelectionChanged;
            ImagesListBox.MouseDoubleClick += ImagesListBox_MouseDoubleClick;
            ImagesListBox.KeyDown += ImagesListBox_KeyDown;

            // Recalcular tamaño de miniaturas al cargar / redimensionar
            ImagesListBox.Loaded += (s, e) => UpdateWrapPanelItemSize();
            ImagesListBox.SizeChanged += (s, e) => UpdateWrapPanelItemSize();

            // Registrar eventos para interacción con PreviewImage (zoom/pan con Espacio)
            this.PreviewKeyDown += Window_PreviewKeyDown;
            this.PreviewKeyUp += Window_PreviewKeyUp;

            // PreviewImage puede no ser focusable por defecto; permitir eventos de ratón
            try
            {
                PreviewImage.Focusable = true;
                PreviewImage.MouseWheel += PreviewImage_MouseWheel;
                PreviewImage.MouseDown += PreviewImage_MouseDown;
                PreviewImage.MouseMove += PreviewImage_MouseMove;
                PreviewImage.MouseUp += PreviewImage_MouseUp;
            }
            catch
            {
                // ignorar si algún control no existe en tiempo de diseño
            }

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

        // SELECTION CHANGED: actualizar solo la previsualización (NO proyectar)
        private void ImagesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selected = ImagesListBox.SelectedItem;
                if (selected is BitmapImage bi)
                {
                    PreviewMedia.Visibility = Visibility.Collapsed;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewImage.Source = bi;
                    TxtPreviewInfo.Text = "Vista previa: imagen seleccionada (single click)";

                    // Reset transforms de preview para evitar sorpresas al seleccionar nueva imagen
                    try { BtnResetZoom_Click(null, null); } catch { }
                }
                else if (selected is CachedImage ci)
                {
                    PreviewMedia.Visibility = Visibility.Collapsed;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewImage.Source = ci.Image;
                    TxtPreviewInfo.Text = $"Vista previa: {Path.GetFileName(ci.FilePath)}";

                    try { BtnResetZoom_Click(null, null); } catch { }
                }
                else if (selected is string s && File.Exists(s))
                {
                    // posible vídeo (ruta como texto)
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewMedia.Visibility = Visibility.Visible;
                    try { PreviewMedia.Source = new Uri(s); } catch { PreviewMedia.Source = null; }
                    TxtPreviewInfo.Text = $"Vista previa: vídeo {Path.GetFileName(s)}";
                }
                else
                {
                    // limpiar previsualización
                    PreviewMedia.Stop();
                    PreviewMedia.Source = null;
                    PreviewMedia.Visibility = Visibility.Collapsed;
                    PreviewImage.Source = null;
                    PreviewImage.Visibility = Visibility.Visible;
                    TxtPreviewInfo.Text = "Imagen / Video";
                }
            }
            catch
            {
                // ignorar
            }
        }

        // KeyDown en la lista: Enter proyecta (igual que doble click)
        private void ImagesListBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryProjectSelectedItem();
                e.Handled = true;
            }
        }

        // Doble click en inventario → proyectar (comportamiento requerido)
        private void ImagesListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TryProjectSelectedItem();
        }

        // Helper que centraliza la lógica de proyectar el ítem seleccionado (invoca _onImageSelected solo aquí)
        private void TryProjectSelectedItem()
        {
            var selected = ImagesListBox.SelectedItem;
            if (selected == null) return;

            BitmapImage? toProject = null;
            if (selected is BitmapImage bi) toProject = bi;
            else if (selected is CachedImage ci) toProject = ci.Image;
            else if (selected is string s && File.Exists(s))
            {
                // si fuera ruta de imagen, intentar cargarla
                try
                {
                    var bmp = LoadBitmapFromFileCached(s);
                    if (bmp != null) toProject = bmp;
                }
                catch { }
            }

            if (toProject != null)
            {
                try
                {
                    // Asegurar invocación en el dispatcher (y que la acción sea la única responsable de proyectar)
                    Dispatcher.Invoke(() => { try { _onImageSelected?.Invoke(toProject); } catch { } });
                }
                catch { }
            }
        }

        // Mantener doble click por compatibilidad (ya existía) - vaciar si estaba definido anteriormente
        private void ImagesListBox_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            // Intencionalmente vacío: la proyección se realiza solo en doble click o Enter.
        }

        // ---------------------------
        // Interacción Preview: Espacio + rueda/arrastre
        // ---------------------------

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space && !_isSpacePressed)
            {
                _isSpacePressed = true;
                Mouse.OverrideCursor = Cursors.SizeAll;
            }
        }

        private void Window_PreviewKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                _isSpacePressed = false;
                _isPanningPreview = false;
                Mouse.OverrideCursor = null;
            }
        }

        private void PreviewImage_MouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (!_isSpacePressed) return; // solo cuando se mantiene espacio

            try
            {
                var (s, t) = EnsurePreviewTransforms(); // método en BrowserWindow.Events.cs
                double oldScale = s.ScaleX;
                double delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
                double newScale = Math.Max(0.2, Math.Min(5.0, oldScale + delta));
                if (Math.Abs(newScale - oldScale) < 0.0001) return;

                var pos = e.GetPosition(PreviewImage);

                double scaleFactor = newScale / oldScale;

                // Ajustar translate para mantener el punto bajo el cursor
                t.X = (1 - scaleFactor) * pos.X + scaleFactor * t.X;
                t.Y = (1 - scaleFactor) * pos.Y + scaleFactor * t.Y;

                s.ScaleX = newScale;
                s.ScaleY = newScale;

                // Propagar la transformación a la proyección (si hay ventana de proyección abierta)
                // SyncProjectionTransform está implementado en BrowserWindow.Events.cs (única copia)
                // Aquí sólo llamamos al método existente.
                try { SyncProjectionTransform(newScale, t.X, t.Y); } catch { }

                e.Handled = true;
            }
            catch
            {
                // ignorar
            }
        }

        private void PreviewImage_MouseDown(object? sender, MouseButtonEventArgs e)
        {
            if (!_isSpacePressed) return;

            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningPreview = true;
                _lastPreviewMousePos = e.GetPosition(PreviewImage);
                try { Mouse.Capture(PreviewImage); } catch { }
            }
        }

        private void PreviewImage_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isSpacePressed || !_isPanningPreview) return;

            try
            {
                var pos = e.GetPosition(PreviewImage);
                var dx = pos.X - _lastPreviewMousePos.X;
                var dy = pos.Y - _lastPreviewMousePos.Y;
                _lastPreviewMousePos = pos;

                var (_, t) = EnsurePreviewTransforms();
                t.X += dx;
                t.Y += dy;

                // Propagar cambios a la proyección
                var s = (EnsurePreviewTransforms().scale.ScaleX);
                try { SyncProjectionTransform(s, t.X, t.Y); } catch { }
            }
            catch
            {
                // ignorar
            }
        }

        private void PreviewImage_MouseUp(object? sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningPreview = false;
                try { Mouse.Capture(null); } catch { }
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
        // Guarda la última URL y descarga la imagen; añade al inventario, PERO NO INVOCARÁ LA PROYECCIÓN
        public async void OnImageUrlClicked(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                // resolver URL relativa respecto a la página si es necesario
                Uri finalUri;
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

                _lastClickedImageUri = finalUri;
                _isDownloadingImage = true;

                var bmp = await DownloadBitmapFromUrlAsync(finalUri).ConfigureAwait(false);

                _isDownloadingImage = false;

                if (bmp != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // Añadir al inventario (append) COMO CachedImage para consistencia
                            ImagesListBox.Items.Add(new CachedImage { FilePath = "", Image = bmp });

                            // Actualizar layout responsivo tras añadir item
                            try { UpdateWrapPanelItemSize(); } catch { }
                        }
                        catch { }
                    });
                }
                else
                {
                    // no descargada
                    await Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("No se pudo descargar la imagen seleccionada desde la web.", "Error descarga", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
            }
            catch
            {
                _isDownloadingImage = false;
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
