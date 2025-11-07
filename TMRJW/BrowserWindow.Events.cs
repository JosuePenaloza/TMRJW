using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Windows.Controls; // añadido para WrapPanel, ListBox

namespace TMRJW
{
    public partial class BrowserWindow
    {
        private bool _projectionPowerOn = false;
        private const double ZoomStep = 0.2;

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            try { if (WebBrowserControl.CanGoBack) WebBrowserControl.GoBack(); } catch { }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            try { if (WebBrowserControl.CanGoForward) WebBrowserControl.GoForward(); } catch { }
        }

        private async void BtnRefreshImages_Click(object sender, RoutedEventArgs e)
        {
            try { await RefreshImagesFromCurrentPageAsync(); InjectClickScript(); } catch { }
        }

        // Ahora captura/descarga todas las imágenes de la página, guarda en cache y las añade al inventario
        private async void BtnCaptureFromPage_Click(object sender, RoutedEventArgs e)
        {
            // Mostrar indicador y deshabilitar botón mientras se analiza la página
            try
            {
                Dispatcher.Invoke(() =>
                {
                    try { CaptureIndicator.Visibility = Visibility.Visible; } catch { }
                });

                var url = WebBrowserControl.Source?.AbsoluteUri ?? UrlBox.Text;
                if (string.IsNullOrWhiteSpace(url)) return;

                var pageUri = new Uri(url);
                string imagesTabKey = $"Web: {pageUri.Host}";
                string videosTabKey = $"Videos: {pageUri.Host}";

                // Extraer URIs de imágenes y vídeos por separado
                var imageUris = await OnlineLibraryHelper.ExtractMediaUrisFromPageAsync(url, new[] { "png", "jpg", "jpeg", "gif", "webp" }, CancellationToken.None);
                var videoUris = await OnlineLibraryHelper.ExtractMediaUrisFromPageAsync(url, new[] { "mp4", "webm", "wmv", "avi" }, CancellationToken.None);

                // Descargar imágenes
                var imagesList = new List<(Uri Uri, byte[]? Bytes)>();
                foreach (var u in imageUris)
                {
                    try { var bytes = await s_http.GetByteArrayAsync(u).ConfigureAwait(false); imagesList.Add((u, bytes)); }
                    catch { imagesList.Add((u, null)); }
                }

                EnsureTabExists(imagesTabKey, imagesTabKey);
                EnsureTabExists(videosTabKey, videosTabKey);

                var addedImages = 0;
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

                // Procesar imágenes y añadir a la pestaña de imágenes (y 'Todas')
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in imagesList)
                    {
                        if (item.Bytes == null) continue;
                        try
                        {
                            var ext = Path.GetExtension(item.Uri.LocalPath);
                            var path = SaveBytesToCacheAsync(item.Bytes, ext).GetAwaiter().GetResult();
                            if (!string.IsNullOrEmpty(path))
                            {
                                var bi = LoadBitmapFromFileCached(path);
                                if (bi != null)
                                {
                                    var ci = new CachedImage { FilePath = path, Image = bi };
                                    AddImageToTab(imagesTabKey, ci); // por defecto añade también a 'Todas'
                                    addedImages++;
                                }
                            }
                        }
                        catch { }
                    }
                });

                // Añadir vídeos como URLs a una pestaña separada "Videos: host" (NO añadir a 'Todas')
                int addedVideos = 0;
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                foreach (var vu in videoUris)
                {
                    try
                    {
                        // Guardar la URL como string; no añadir a 'Todas' (addToAll = false)
                        AddImageToTab(videosTabKey, vu.AbsoluteUri, videosTabKey, addToAll: false);
                        addedVideos++;
                    }
                    catch { }
                }

                try { Dispatcher.Invoke(() => { var sa = new SilentAlertWindow($"Capturadas {addedImages} imágenes y {addedVideos} vídeos desde la página.", "Captura completa"); sa.Owner = this; sa.ShowDialog(); }); } catch { }
                try { UpdateWrapPanelItemSize(); } catch { }
            }
            catch
            {
                try { Dispatcher.Invoke(() => { var sa = new SilentAlertWindow("Error al capturar imágenes o vídeos de la página.", "Error"); sa.Owner = this; sa.ShowDialog(); }); } catch { }
            }
            finally
            {
                // Restaurar UI
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        try { CaptureIndicator.Visibility = Visibility.Collapsed; } catch { }
                    });
                }
                catch { }
            }
        }

        private async void BtnProjectionPower_Click(object sender, RoutedEventArgs e)
        {
            _projectionPowerOn = !_projectionPowerOn;
            var btnSender = sender as System.Windows.Controls.Button;
            if (btnSender != null)
                btnSender.Content = _projectionPowerOn ? "Proy ON" : "Proy OFF";

            if (_projectionPowerOn)
            {
                // Intentar obtener imagen local inmediata
                BitmapImage? imgToProject = null;

                if (ImagesListBox?.SelectedItem is CachedImage ciSel)
                    imgToProject = ciSel.Image;
                else if (ImagesListBox?.SelectedItem is BitmapImage biSel)
                    imgToProject = biSel;
                else if (PreviewImage?.Source is BitmapImage previewBI)
                    imgToProject = previewBI;

                if (imgToProject != null)
                {
                    try { _onImageSelected?.Invoke(imgToProject); } catch { }
                    return;
                }

                // Si hay descarga en curso, marcamos que estamos esperando y salimos (la callback se ejecutará al terminar)
                if (_isDownloadingImage)
                {
                    _projectionWaitingForImage = true;
                    // mostrar alerta silenciosa en ventana
                    try { Dispatcher.Invoke(() => { var sa = new SilentAlertWindow("Proyección encendida: esperando a que la imagen termine de descargarse...", "Información"); sa.Owner = this; sa.ShowDialog(); }); } catch { }
                    return;
                }

                // Si tenemos una URL de último click y no se ha descargado, iniciar descarga y proyectar cuando esté lista
                if (_lastClickedImageUri != null)
                {
                    _isDownloadingImage = true;
                    BitmapImage? bmp = null;
                    try
                    {
                        var bytes = await s_http.GetByteArrayAsync(_lastClickedImageUri).ConfigureAwait(false);
                        if (bytes != null)
                        {
                            var path = await SaveBytesToCacheAsync(bytes, Path.GetExtension(_lastClickedImageUri.LocalPath)).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(path))
                                bmp = LoadBitmapFromFileCached(path);
                        }
                    }
                    catch { }
                    _isDownloadingImage = false;

                    if (bmp != null)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            AddImageToTab(DefaultTabKey, new CachedImage { FilePath = "", Image = bmp });
                            try { _onImageSelected?.Invoke(bmp); } catch { }
                        });
                        return;
                    }
                }

                // Nada disponible
                try { Dispatcher.Invoke(() => { var sa = new SilentAlertWindow("No hay imagen seleccionada para proyectar. Selecciona una imagen en el inventario o en la previsualización.", "Información"); sa.Owner = this; sa.ShowDialog(); }); } catch { }
            }
            else
            {
                // Al apagar: ocultar o cerrar cualquier ventana de proyección manejada localmente
                try
                {
                    foreach (Window w in Application.Current.Windows)
                    {
                        if (w is ProyeccionWindow pw)
                        {
                            try { pw.Hide(); } catch { }
                        }
                    }
                }
                catch { }
            }
        }

        // BtnLoadExtra: ahora agrupa por carpeta y crea pestaña "Carpeta: <nombre>"
        private void BtnLoadExtra_Click(object sender, RoutedEventArgs e)
        {
            // Mostrar por defecto filtro que incluye imágenes y vídeos para que el usuario vea ambos tipos al abrir la carpeta
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "Imágenes y Vídeos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.wmv;*.avi|Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Vídeos|*.mp4;*.wmv;*.avi|Todos los archivos|*.*"
            };

            if (dlg.ShowDialog() != true) return;

            // Agrupar todos los archivos cargados desde 'Cargar extra' en pestaanas únicas
            // para imágenes usaremos 'Imágenes Cargadas' y para vídeos 'Videos: Extras'
            // Usar clave con prefijo 'Carpeta:' para que EnsureTabExists trate la pestaña como lista vertical
            string imagesTabKey = "Carpeta: Imágenes Cargadas";
            string imagesTabHeader = "Imágenes Cargadas";
            string videosTabKey = "Videos: Extras"; // mantiene prefijo 'Videos:' para plantilla de vídeo
            string videosTabHeader = "Videos Extras";

            EnsureTabExists(imagesTabKey, imagesTabHeader);
            EnsureTabExists(videosTabKey, videosTabHeader);

            foreach (var f in dlg.FileNames)
            {
                try
                {
                    if (!File.Exists(f)) continue;

                    var ext = Path.GetExtension(f) ?? string.Empty;
                    if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
                    {
                        // Vídeos: añadir a la pestaña de Vídeos de esta carpeta. NO añadir a 'Todas' (addToAll=false)
                        AddImageToTab(videosTabKey, f, videosTabHeader, addToAll: false);
                    }
                    else
                    {
                        // Imágenes: copiar al cache para uniformidad
                        try
                        {
                            var bytes = File.ReadAllBytes(f);
                            var path = SaveBytesToCacheAsync(bytes, Path.GetExtension(f)).GetAwaiter().GetResult();
                            if (!string.IsNullOrEmpty(path))
                            {
                                var bi = LoadBitmapFromFileCached(path);
                                if (bi != null)
                                {
                                    var ci = new CachedImage { FilePath = path, Image = bi };
                                    AddImageToTab(imagesTabKey, ci, imagesTabHeader);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Ajustar miniaturas tras añadir
            try { UpdateWrapPanelItemSize(); } catch { }
        }

        private void BtnTextoDelAnio_Click(object sender, RoutedEventArgs e)
        {
            // Mostrar Texto del Año directamente en la ventana de proyección local (no depender de MainWindow)
            try
            {
                var pw = EnsureProjectionWindow();
                if (pw != null)
                {
                    // Intentar con la imagen de ajustes
                    try
                    {
                        var settings = SettingsHelper.Load();
                        var ruta = settings.ImagenTextoAnio;
                        if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta))
                        {
                            var img = PlatformInterop.LoadBitmapFromFile(ruta);
                            if (img != null)
                            {
                                try { pw.StopVideo(); pw.SetLastWasTextoAnio(true); pw.MostrarImagenTexto(img); try { pw.Show(); pw.Activate(); } catch { } return; } catch { }
                            }
                        }
                    }
                    catch { }

                    // Como último recurso, generar texto
                    try
                    {
                        int targetW = (int)Math.Max(800, pw.Width > 0 ? pw.Width : 1920);
                        int targetH = (int)Math.Max(600, pw.Height > 0 ? pw.Height : 1080);
                        int fontSize = Math.Max(36, targetH / 18);
                        var txt = "Denle a Jehová\nla gloria que su nombre merece\n(Salmo 96:8)";
                        var bi = CreateTextBitmapImage(txt, targetW, targetH, 96, fontSize);
                        if (bi != null) { pw.MostrarImagenTexto(bi); try { pw.Show(); pw.Activate(); } catch { } }
                    }
                    catch { }
                }
            }
            catch { }

            // Fallback informativo silencioso
            try { Dispatcher.Invoke(() => { var sa = new SilentAlertWindow("Acción: proyectar 'Texto del Año' (no se encontró MainWindow).", "Información"); sa.Owner = this; sa.ShowDialog(); }); } catch { }
        }

        // ---------------------------------------
        // Handlers añadidos para corregir errores
        // ---------------------------------------

        // Cerrar la ventana y volver al MainWindow
        private void BtnReturnToMain_Click(object sender, RoutedEventArgs e)
        {
            try { this.Close(); } catch { }
        }

        private void BtnOpenAjustes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Abrir ajustes como ventana modeless y sin Owner para evitar que su ciclo de vida
                // afecte a la ventana de proyección.
                var ajustes = new AjustesWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                ajustes.Show();
            }
            catch { }
        }

        private void PopulateMonitorsAfterSettingsChange()
        {
            try
            {
                // Intentar reposicionar la ventana de proyección existente según los ajustes
                try
                {
                    var settings = SettingsHelper.Load();
                    var selectedDevice = settings.SelectedMonitorDeviceName;
                    var monitors = PlatformInterop.GetMonitorsNative();
                    PlatformInterop.MonitorInfo? target = null;

                    if (!string.IsNullOrEmpty(selectedDevice))
                        target = monitors.Find(m => string.Equals(m.DeviceName, selectedDevice, StringComparison.OrdinalIgnoreCase));

                    if (target == null)
                        target = monitors.Find(m => !m.IsPrimary);
                    if (target == null)
                        target = monitors.Find(m => m.IsPrimary) ?? (monitors.Count > 0 ? monitors[0] : null);

                    if (target != null && _localProyeccionWindow != null)
                    {
                        try
                        {
                            var helper = new System.Windows.Interop.WindowInteropHelper(_localProyeccionWindow);
                            IntPtr hWnd = helper.Handle;
                            if (hWnd != IntPtr.Zero)
                            {
                                const uint SWP_SHOWWINDOW = 0x0040;
                                IntPtr HWND_TOPMOST = new IntPtr(-1);
                                SetWindowPos(hWnd, HWND_TOPMOST, target.X, target.Y, target.Width, target.Height, SWP_SHOWWINDOW);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            catch { }
        }

        // Handler para eliminar imagen del inventario (botón 🗑)
        private void BtnDeleteImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not System.Windows.Controls.Button btn) return;
                var item = btn.DataContext;
                if (item == null) return;

                // Confirmar eliminación
                var res = MessageBox.Show("¿Eliminar esta imagen del inventario?", "Confirmar eliminación", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;

                // Si es CachedImage intentamos borrar el archivo cacheado
                if (item is CachedImage ci)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(ci.FilePath) && File.Exists(ci.FilePath))
                        {
                            try { File.Delete(ci.FilePath); } catch { /* ignorar fallo borrado */ }
                        }
                    }
                    catch { }
                    // quitar de todas las colecciones donde aparezca
                    foreach (var k in _tabCollections.Keys.ToArray())
                    {
                        try { _tabCollections[k].Remove(ci); } catch { }
                    }

                    // ajustar layout
                    try { UpdateWrapPanelItemSize(); } catch { }
                    return;
                }

                // Si es BitmapImage o string, quitar de las colecciones
                try
                {
                    // Soportar VideoListItem
                    if (item is VideoListItem vli)
                    {
                        foreach (var k in _tabCollections.Keys.ToArray())
                        {
                            try { _tabCollections[k].Remove(vli); } catch { }
                        }
                        try { UpdateWrapPanelItemSize(); } catch { }
                        return;
                    }

                    foreach (var k in _tabCollections.Keys.ToArray())
                    {
                        try { _tabCollections[k].Remove(item); } catch { }
                    }
                    try { UpdateWrapPanelItemSize(); } catch { }
                }
                catch { }
            }
            catch { }
        }

        // Play/Pause toggle for preview
        private async void BtnPreviewPlayPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Controlar la reproducción en la ProyeccionWindow directamente
                try
                {
                    var pw = EnsureProjectionWindow();
                    if (pw == null) return;

                    // Try to resume if paused
                    try
                    {
                        if (pw.TryResume()) { BtnPreviewPlayPause.Content = "⏸"; return; }
                    }
                    catch { }

                    if (pw.IsPlayingVideo())
                    {
                        pw.PauseVideo();
                        BtnPreviewPlayPause.Content = "⏵";
                        return;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(_lastSelectedVideoPath)) return;
                        // Pedir que BrowserWindow libere su preview
                        try { StopPreviewPlaybackAndReset(); } catch { }

                        // Copiar a temporal para evitar locks
                        string? temp = null;
                        try { temp = await CopyMediaToTempAsync(_lastSelectedVideoPath, forProjection: true).ConfigureAwait(false); } catch { }

                        if (!string.IsNullOrWhiteSpace(temp)) pw.PlayVideo(temp);
                        else
                        {
                            if (Uri.TryCreate(_lastSelectedVideoPath, UriKind.Absolute, out Uri? u) && (u.Scheme=="http"||u.Scheme=="https")) pw.PlayVideo(u);
                            else pw.PlayVideo(_lastSelectedVideoPath);
                        }
                        BtnPreviewPlayPause.Content = "⏸";
                        return;
                    }
                }
                catch { }
            }
            catch { }
        }

        private void BtnPreviewStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                try
                {
                    var pw = EnsureProjectionWindow();
                    if (pw != null) pw.StopVideo();
                    BtnPreviewPlayPause.Content = "⏵";
                }
                catch { }
            }
            catch { }
        }

        private bool _previewIsPlaying = false;
        private System.Windows.Threading.DispatcherTimer? _previewTimer;

        private void StartPreviewTimer()
        {
            try
            {
                if (_previewTimer == null)
                {
                    _previewTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(250)
                    };
                    _previewTimer.Tick += (s, e) => {
                        try
                        {
                            if (PreviewMedia != null && PreviewMedia.NaturalDuration.HasTimeSpan)
                            {
                                var pos = PreviewMedia.Position;
                                var dur = PreviewMedia.NaturalDuration.TimeSpan;
                                double frac = dur.TotalSeconds > 0 ? pos.TotalSeconds / dur.TotalSeconds : 0;
                                PreviewSldTimeline.Value = frac;
                                TxtPreviewCurrentTime.Text = pos.ToString(@"hh\:mm\:ss");
                                TxtPreviewTotalTime.Text = "/" + dur.ToString(@"hh\:mm\:ss");
                            }
                        }
                        catch { }
                    };
                }
                _previewTimer?.Start();
            }
            catch { }
        }

        private void StopPreviewTimer()
        {
            try { _previewTimer?.Stop(); } catch { }
        }

        // Zoom / Pan básicos sobre PreviewImage (añade RenderTransform si hace falta)
        private (ScaleTransform scale, TranslateTransform translate) EnsurePreviewTransforms()
        {
            if (PreviewImage.RenderTransform is TransformGroup tg)
            {
                var s = tg.Children.OfType<ScaleTransform>().FirstOrDefault();
                var t = tg.Children.OfType<TranslateTransform>().FirstOrDefault();
                if (s == null) { s = new ScaleTransform(1, 1); tg.Children.Insert(0, s); }
                if (t == null) { t = new TranslateTransform(0, 0); tg.Children.Add(t); }
                return (s, t);
            }
            else
            {
                var s = new ScaleTransform(1, 1);
                var t = new TranslateTransform(0, 0);
                var group = new TransformGroup();
                group.Children.Add(s);
                group.Children.Add(t);
                PreviewImage.RenderTransform = group;
                return (s, t);
            }
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            var (s, t) = EnsurePreviewTransforms();
            s.ScaleX = Math.Min(5.0, s.ScaleX + ZoomStep);
            s.ScaleY = s.ScaleX;
            // Sincronizar a la proyección
            try { SyncProjectionTransform(s.ScaleX, t.X, t.Y); } catch { }
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            var (s, t) = EnsurePreviewTransforms();
            s.ScaleX = Math.Max(0.2, s.ScaleX - ZoomStep);
            s.ScaleY = s.ScaleX;
            try { SyncProjectionTransform(s.ScaleX, t.X, t.Y); } catch { }
        }

        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            var (s, t) = EnsurePreviewTransforms();
            s.ScaleX = 1.0; s.ScaleY = 1.0;
            t.X = 0; t.Y = 0;
            try { SyncProjectionTransform(1.0, 0, 0); } catch { }
        }

        private void BtnPanLeft_Click(object sender, RoutedEventArgs e)
        {
            var (s, t) = EnsurePreviewTransforms();
            t.X -= 40;
            try { SyncProjectionTransform(s.ScaleX, t.X, t.Y); } catch { }
        }

        private void BtnPanRight_Click(object sender, RoutedEventArgs e)
        {
            var (s, t) = EnsurePreviewTransforms();
            t.X += 40;
            try { SyncProjectionTransform(s.ScaleX, t.X, t.Y); } catch { }
        }

        private void BtnPanUp_Click(object sender, RoutedEventArgs e)
        {
            var (s, t) = EnsurePreviewTransforms();
            t.Y -= 40;
            try { SyncProjectionTransform(s.ScaleX, t.X, t.Y); } catch { }
        }

        private void BtnPanDown_Click(object sender, RoutedEventArgs e)
        {
            var (s, t) = EnsurePreviewTransforms();
            t.Y += 40;
            try { SyncProjectionTransform(s.ScaleX, t.X, t.Y); } catch { }
        }

        private void PreviewVolumeSld_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                double volPerc = (sender as Slider)?.Value ?? 75.0;
                double level = Math.Max(0.0, Math.Min(100.0, volPerc)) / 100.0;
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is ProyeccionWindow pw)
                    {
                        try { pw.SetVolume(level); } catch { }
                    }
                }
            }
            catch { }
        }
        // -------------------------
        // Helpers para layout responsivo (WrapPanel)
        // -------------------------

        private void UpdateWrapPanelItemSize()
        {
            try
            {
                var wrap = FindVisualChild<WrapPanel>(ImagesListBox);
                if (wrap == null) return;

                double availableWidth = ImagesListBox.ActualWidth;
                if (availableWidth <= 0) return;

                double scrollbarWidth = SystemParameters.VerticalScrollBarWidth;
                availableWidth = Math.Max(0, availableWidth - scrollbarWidth - ImagesListBox.Padding.Left - ImagesListBox.Padding.Right - 12);

                const double minThumbWidth = 140.0;
                const int maxCols = 3;

                int cols = Math.Min(maxCols, Math.Max(1, (int)Math.Floor(availableWidth / minThumbWidth)));
                if (cols < 1) cols = 1;

                double spacing = 12.0;
                double itemWidth = Math.Floor((availableWidth - (cols - 1) * spacing) / cols);
                if (itemWidth < 80) itemWidth = 80;

                try
                {
                    wrap.ItemWidth = itemWidth;
                    // ajustar altura aproximada (imagen + botones)
                    wrap.ItemHeight = Math.Floor(itemWidth * 110.0 / 150.0) + 44;
                }
                catch { }
            }
            catch { }
        }

        // Helper para buscar hijo visual
        private static T? FindVisualChild<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        // Buscar instancias de ProyeccionWindow y actualizar sus transformaciones
        private void SyncProjectionTransform(double scale, double offsetX, double offsetY)
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is ProyeccionWindow pw)
                    {
                        try
                        {
                            pw.UpdateImageTransform(scale, offsetX, offsetY);
                        }
                        catch { /* ignorar errores de sincronización */ }
                    }
                }
            }
            catch { }
        }

        // Helpers para cache/IO
        private BitmapImage? LoadBitmapFromFileCached(string path)
        {
            try
            {
                var bi = new BitmapImage();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = fs;
                    bi.EndInit();
                    bi.Freeze();
                }
                return bi;
            }
            catch { return null; }
        }

        private async Task<string> SaveBytesToCacheAsync(byte[] bytes, string? extension)
        {
            try
            {
                var dir = GetCacheDirectory(); // GetCacheDirectory está en este archivo
                Directory.CreateDirectory(dir);
                string ext = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
                if (!ext.StartsWith(".")) ext = "." + ext;
                string fileName = Guid.NewGuid().ToString("N") + ext;
                string path = Path.Combine(dir, fileName);
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
                return path;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetCacheDirectory()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "TMRJW", "cache");
        }
    }
}