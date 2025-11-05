using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Media.Imaging;
using System.Windows.Input;
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

        // Gestión de pestañas y colecciones
        private readonly Dictionary<string, ObservableCollection<object>> _tabCollections = new();
        private const string DefaultTabKey = "Todas";

        // Evitar añadir imágenes web duplicadas (clave: URI absoluta)
        private readonly HashSet<string> _seenImageUris = new();

        public BrowserWindow()
        {
            InitializeComponent();
            UrlBox.Text = "https://wol.jw.org/es/wol/meetings/r4/lp-s/";

            // Registrar handlers: selección actualiza solo previsualización; doble click/Enter proyectan.
            // (Nota: las ListBox de cada pestaña se crean dinámicamente)
            // ImagesListBox es una propiedad de conveniencia (no asignarla).

            // Recalcular tamaño de miniaturas al cargar / redimensionar (si usas UpdateWrapPanelItemSize)
            this.Loaded += (s, e) => {
                // crear pestaña por defecto
                EnsureTabExists(DefaultTabKey, "Todas");
                SelectTab(DefaultTabKey);
            };

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
            catch { }

            // silenciar errores de script en el WebBrowser
            WebBrowserControl.Navigated += WebBrowserControl_Navigated;

            // exponer objeto COM para que JS pueda llamar a window.external.ImageClicked(...)
            try
            {
                WebBrowserControl.ObjectForScripting = new ScriptBridge(this);
            }
            catch { }
        }

        // Nota: ImagesListBox no se usa ahora directamente; el ListBox se crea dentro de cada TabItem.

        // --- Gestión de pestañas / colecciones ---
        public void EnsureTabExists(string key, string header)
        {
            if (string.IsNullOrWhiteSpace(key)) key = Guid.NewGuid().ToString();
            if (_tabCollections.ContainsKey(key)) return;

            var col = new ObservableCollection<object>();
            _tabCollections[key] = col;

            // crear UI (TabItem + ListBox) en el dispatcher
            Dispatcher.Invoke(() =>
            {
                var tab = new TabItem { Header = header ?? key, Tag = key };

                var lb = new ListBox
                {
                    ItemsSource = col,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    SelectionMode = SelectionMode.Single
                };

                // Seleccionar plantilla según tipo de pestaña
                try
                {
                    if (key != null && key.StartsWith("Videos:", StringComparison.OrdinalIgnoreCase))
                        lb.ItemTemplate = (DataTemplate)FindResource("VideoItemTemplate");
                    else if (string.Equals(key, DefaultTabKey, StringComparison.OrdinalIgnoreCase) || (key != null && key.StartsWith("Carpeta:", StringComparison.OrdinalIgnoreCase)))
                        lb.ItemTemplate = (DataTemplate)FindResource("ListItemTemplate");
                    else
                        lb.ItemTemplate = (DataTemplate)FindResource("ThumbnailTemplate");
                }
                catch
                {
                    lb.ItemTemplate = (DataTemplate)FindResource("ThumbnailTemplate");
                }

                // Seleccionar ItemsPanel según tipo de pestaña
                if (key != null && key.StartsWith("Videos:", StringComparison.OrdinalIgnoreCase))
                {
                    // lista vertical para vídeos
                    var spFactory = new FrameworkElementFactory(typeof(StackPanel));
                    spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
                    lb.ItemsPanel = new ItemsPanelTemplate(spFactory);
                }
                else if (string.Equals(key, DefaultTabKey, StringComparison.OrdinalIgnoreCase) || (key != null && key.StartsWith("Carpeta:", StringComparison.OrdinalIgnoreCase)))
                {
                    // lista vertical para 'Todas' y carpetas de imágenes
                    var spFactory = new FrameworkElementFactory(typeof(StackPanel));
                    spFactory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
                    lb.ItemsPanel = new ItemsPanelTemplate(spFactory);
                }
                else
                {
                    // ItemsPanel = WrapPanel Horizontal para otras pestañas (miniaturas)
                    var factory = new FrameworkElementFactory(typeof(WrapPanel));
                    factory.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
                    lb.ItemsPanel = new ItemsPanelTemplate(factory);
                }

                // handlers que antes usábamos en ImagesListBox: selección y doble click debe actualizar preview / proyectar
                lb.SelectionChanged += ImagesListBox_SelectionChanged;
                lb.MouseDoubleClick += ImagesListBox_MouseDoubleClick;
                lb.KeyDown += ImagesListBox_KeyDown;

                tab.Content = new Border { Background = System.Windows.Media.Brushes.Transparent, Child = lb };
                InventoryTabs.Items.Add(tab);
            });
        }

        // Añade item a pestaña. Por defecto también añade a la pestaña 'Todas' (addToAll=true).
        public void AddImageToTab(string key, object item, string header = null, bool addToAll = true)
        {
            if (item == null) return;
            EnsureTabExists(key, header ?? key);
            EnsureTabExists(DefaultTabKey, "Todas");

            Dispatcher.Invoke(() =>
            {
                try
                {
                    object toAdd = item;

                    // Si es cadena y corresponde a vídeos, envolver en VideoListItem y lanzar generación de thumbnail
                    if (item is string s && !string.IsNullOrWhiteSpace(s) && key != null && key.StartsWith("Videos:", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = new VideoListItem { FilePath = s };
                        toAdd = v;

                        // generar thumbnail en background y asignar cuando esté listo
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var thumb = await GenerateVideoThumbnailAsync(s, 160, 90).ConfigureAwait(false);
                                if (thumb != null)
                                {
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        try { v.Thumbnail = thumb; } catch { }
                                    });
                                }
                            }
                            catch { }
                        });
                    }

                    if (addToAll)
                    {
                        _tabCollections[DefaultTabKey].Add(toAdd);
                    }

                    if (key != DefaultTabKey)
                        _tabCollections[key].Add(toAdd);
                }
                catch { }
            });
        }

        // Version que añade múltiples items; por defecto añade también a 'Todas'
        public void AddImagesToTab(string key, IEnumerable<object> items, string header = null, bool addToAll = true)
        {
            if (items == null) return;
            EnsureTabExists(key, header ?? key);
            EnsureTabExists(DefaultTabKey, "Todas");

            Dispatcher.Invoke(() =>
            {
                foreach (var it in items)
                {
                    try
                    {
                        if (addToAll)
                            _tabCollections[DefaultTabKey].Add(it);
                        if (key != DefaultTabKey)
                            _tabCollections[key].Add(it);
                    }
                    catch { }
                }
            });
        }

        public void SelectTab(string key)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var ti in InventoryTabs.Items.OfType<TabItem>())
                {
                    if (ti.Tag?.ToString() == key || ti.Header?.ToString() == key)
                    {
                        InventoryTabs.SelectedItem = ti;
                        return;
                    }
                }
            });
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
                // evitar añadir duplicados
                try { if (_seenImageUris.Contains(finalUri.AbsoluteUri)) return; } catch { }

                _isDownloadingImage = true;

                var bmp = await DownloadBitmapFromUrlAsync(finalUri).ConfigureAwait(false);

                _isDownloadingImage = false;

                if (bmp != null)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            string pageKey = WebBrowserControl.Source != null ? $"Web: {WebBrowserControl.Source.Host}" : "Web: Desconocida";
                            EnsureTabExists(pageKey, pageKey);
                            AddImageToTab(pageKey, new CachedImage { FilePath = "", Image = bmp });
                            try { _seenImageUris.Add(finalUri.AbsoluteUri); } catch { }
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

        private static async Task<BitmapImage?> DownloadBitmapFromUrlAsync(Uri uri)
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

        // Nuevo: refrescar imágenes desde la página actual y añadir a pestaña "Web: host"
        public async Task RefreshImagesFromCurrentPageAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                string? url = WebBrowserControl.Source?.AbsoluteUri ?? UrlBox.Text;
                if (string.IsNullOrWhiteSpace(url)) return;

                UrlBox.Text = url;

                var uris = await OnlineLibraryHelper.ExtractImageUrisFromPageAsync(url, _cts.Token).ConfigureAwait(false);
                if (!uris.Any()) return;

                var list = uris.Take(20).ToList();
                var images = await OnlineLibraryHelper.DownloadImagesAsync(list, _cts.Token).ConfigureAwait(false);

                var pageUri = new Uri(url);
                string tabKey = $"Web: {pageUri.Host}";
                EnsureTabExists(tabKey, tabKey);

                await Dispatcher.InvokeAsync(() =>
                {
                    int i = 0;
                    foreach (var bi in images)
                    {
                        try
                        {
                            var srcUri = list.ElementAtOrDefault(i);
                            AddImageToTab(tabKey, new CachedImage { FilePath = "", Image = bi });
                            if (srcUri != null)
                            {
                                try { _seenImageUris.Add(srcUri.AbsoluteUri); } catch { }
                            }
                        }
                        catch { }
                        i++;
                    }
                });
            }
            catch
            {
                // ignorar
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
                                            var m = /url\(['""']?(.*?)['""']?\)/.exec(bg);
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
