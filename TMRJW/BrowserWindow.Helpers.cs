using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace TMRJW
{
    public partial class BrowserWindow
    {
        // Última ruta seleccionada que representa un vídeo (archivo local o URL)
        private string? _lastSelectedVideoPath;

        // Compatibilidad: exponer callbacks para que MainWindow los registre
        public void SetImageSelectedCallback(Action<BitmapImage> callback) => _onImageSelected = callback;
        private Action<string?>? _onVideoSelected;
        public void SetVideoSelectedCallback(Action<string?> callback) => _onVideoSelected = callback;

        // Rutas temporales usadas para reproducción en preview y proyección (se eliminan al reemplazar)
        private string? _previewTempMediaPath;
        private string? _projectionTempMediaPath;

        // Projection window instance managed by BrowserWindow when MainWindow is not present
        private ProyeccionWindow? _localProyeccionWindow;

        private ProyeccionWindow? EnsureProjectionWindow()
        {
            try
            {
                // Reuse existing ProyeccionWindow if present in App windows
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is ProyeccionWindow pw)
                    {
                        _localProyeccionWindow = pw;
                        return pw;
                    }
                }

                // Create a new projection window and try to position it on the selected monitor
                var projWin = new ProyeccionWindow();

                // Load preferred monitor from settings
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

                    if (target != null)
                    {
                        projWin.WindowStartupLocation = WindowStartupLocation.Manual;
                        projWin.Width = target.Width;
                        projWin.Height = target.Height;
                        projWin.WindowStyle = WindowStyle.None;
                        projWin.ResizeMode = ResizeMode.NoResize;
                        projWin.Topmost = true;
                        projWin.ShowInTaskbar = false;
                    }
                }
                catch { }

                _localProyeccionWindow = projWin;
                // Attach minimal events so BrowserWindow preview stays in sync
                try
                {
                    projWin.PlaybackProgress -= LocalProjection_PlaybackProgress;
                    projWin.PlaybackEnded -= LocalProjection_PlaybackEnded;
                }
                catch { }
                projWin.PlaybackProgress += LocalProjection_PlaybackProgress;
                projWin.PlaybackEnded += LocalProjection_PlaybackEnded;

                projWin.Show();

                // If we set manual startup and have monitor info, try to move window to exact monitor coordinates
                try
                {
                    var settings = SettingsHelper.Load();
                    var selectedDevice = settings.SelectedMonitorDeviceName;
                    var monitors = PlatformInterop.GetMonitorsNative();
                    PlatformInterop.MonitorInfo? target = null;
                    if (!string.IsNullOrEmpty(selectedDevice))
                        target = monitors.Find(m => string.Equals(m.DeviceName, selectedDevice, StringComparison.OrdinalIgnoreCase));
                    if (target == null)
                        target = monitors.Find(m => !m.IsPrimary) ?? monitors.Find(m => m.IsPrimary) ?? (monitors.Count>0?monitors[0]:null);

                    if (target != null)
                    {
                        var helper = new System.Windows.Interop.WindowInteropHelper(projWin);
                        IntPtr hWnd = helper.Handle;
                        if (hWnd != IntPtr.Zero)
                        {
                            const uint SWP_SHOWWINDOW = 0x0040;
                            IntPtr HWND_TOPMOST = new IntPtr(-1);
                            SetWindowPos(hWnd, HWND_TOPMOST, target.X, target.Y, target.Width, target.Height, SWP_SHOWWINDOW);
                        }
                    }
                }
                catch { }

                return _localProyeccionWindow;
            }
            catch
            {
                return _localProyeccionWindow;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private void LocalProjection_PlaybackProgress(TimeSpan pos, TimeSpan? dur)
        {
            try { UpdatePreviewPlayback(pos, dur); } catch { }
        }

        private void LocalProjection_PlaybackEnded()
        {
            try
            {
                var settings = SettingsHelper.Load();
                var ruta = settings.ImagenTextoAnio;
                if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta))
                {
                    var img = PlatformInterop.LoadBitmapFromFile(ruta);
                    if (img != null)
                    {
                        try { _localProyeccionWindow?.MostrarImagenTexto(img); } catch { }
                    }
                }
            }
            catch { }
        }
        // Copia un fichero local o descarga una URL remota a un fichero temporal y devuelve la ruta.
        // Si 'forProjection' es true, mantiene la referencia en _projectionTempMediaPath y elimina la anterior.
        public async Task<string?> CopyMediaToTempAsync(string pathOrUrl, bool forProjection = false)
        {
            try
            {
                // Si es fichero local
                if (File.Exists(pathOrUrl))
                {
                    var ext = Path.GetExtension(pathOrUrl);
                    if (string.IsNullOrEmpty(ext)) ext = ".mp4";
                    string dest = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);
                    try { File.Copy(pathOrUrl, dest, true); }
                    catch { return null; }

                    // Eliminar anterior temp si corresponde
                    try
                    {
                        if (forProjection)
                        {
                            if (!string.IsNullOrEmpty(_projectionTempMediaPath) && File.Exists(_projectionTempMediaPath))
                                File.Delete(_projectionTempMediaPath);
                            _projectionTempMediaPath = dest;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(_previewTempMediaPath) && File.Exists(_previewTempMediaPath))
                                File.Delete(_previewTempMediaPath);
                            _previewTempMediaPath = dest;
                        }
                    }
                    catch { }

                    return dest;
                }

                // Si es URL remota
                if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out Uri? u))
                {
                    var ext = Path.GetExtension(u.LocalPath);
                    if (string.IsNullOrEmpty(ext)) ext = ".mp4";
                    string dest = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);

                    try
                    {
                        using var resp = await s_http.GetAsync(u, System.Net.Http.HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode) return null;
                        using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read);
                        await src.CopyToAsync(fs).ConfigureAwait(false);
                    }
                    catch { return null; }

                    try
                    {
                        if (forProjection)
                        {
                            if (!string.IsNullOrEmpty(_projectionTempMediaPath) && File.Exists(_projectionTempMediaPath))
                                File.Delete(_projectionTempMediaPath);
                            _projectionTempMediaPath = dest;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(_previewTempMediaPath) && File.Exists(_previewTempMediaPath))
                                File.Delete(_previewTempMediaPath);
                            _previewTempMediaPath = dest;
                        }
                    }
                    catch { }

                    return dest;
                }
            }
            catch { }
            return null;
        }

        // Reproduce un vídeo en el preview del BrowserWindow (ruta local o URL).
        // Muestra los controles, asigna la Source al MediaElement y comienza la reproducción.
        public void PlayPreviewVideo(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl)) return;
            try
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        bool isRemote = Uri.TryCreate(pathOrUrl, UriKind.Absolute, out Uri? u) && (u.Scheme == "http" || u.Scheme == "https");

                        PreviewImage.Visibility = Visibility.Collapsed;
                        PreviewMedia.Visibility = Visibility.Visible;

                        try
                        {
                            PreviewMedia.Source = isRemote ? u : new Uri(pathOrUrl);
                        }
                        catch
                        {
                            // si la URI no es válida, intentar asignar como string (no lanzamos)
                            try { PreviewMedia.Source = new Uri(pathOrUrl); } catch { PreviewMedia.Source = null; }
                        }

                        // mostrar controles
                        PreviewMediaControls.Visibility = Visibility.Visible;

                        // iniciar reproducción automática
                        try { PreviewMedia.Play(); _previewIsPlaying = true; BtnPreviewPlayPause.Content = "⏸"; StartPreviewTimer(); } catch { }
                    }
                    catch { }
                }));
            }
            catch { }
        }

        // Exponer ruta actual del preview (si hay)
        public string? CurrentPreviewMediaSource => PreviewMedia?.Source?.AbsoluteUri ?? null;

        // Crea un placeholder simple para miniaturas de vídeo
        private BitmapImage CreateGenericPlaceholderThumbnail(int width = 320, int height = 180)
        {
            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, width, height));
                    var ft = new FormattedText("VIDEO", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 20, Brushes.DarkSlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    var x = (width - ft.Width) / 2;
                    var y = (height - ft.Height) / 2;
                    dc.DrawText(ft, new Point(x, y));
                }

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var ms = new MemoryStream())
                {
                    encoder.Save(ms);
                    ms.Position = 0;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch { return null; }
        }

        // Crear una BitmapImage renderizada desde texto para proyectar mensajes grandes
        private BitmapImage CreateTextBitmapImage(string text, int width = 1920, int height = 1080, int dpi = 96, int fontSize = 60, double pixelsPerDip = 1.0)
        {
            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));

                    // Prepare FormattedText for each line to compute total height
                    var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
                    var formatted = lines.Select(l => new FormattedText(l, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), fontSize, Brushes.White, pixelsPerDip)).ToList();

                    double totalHeight = formatted.Sum(f => f.Height) + Math.Max(0, formatted.Count - 1) * (fontSize * 0.25);
                    double startY = (height - totalHeight) / 2.0;

                    double y = Math.Max(0, startY);
                    foreach (var f in formatted)
                    {
                        double x = (width - f.Width) / 2.0;
                        dc.DrawText(f, new Point(Math.Max(0, x), y));
                        y += f.Height + (fontSize * 0.25);
                    }
                }

                var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
                rtb.Render(dv);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // Compatibilidad: handler simple para el botón "Ir" en la barra de URL (si el XAML lo referencia)
        private void BtnGo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var url = UrlBox.Text?.Trim();
                if (!string.IsNullOrEmpty(url))
                    WebBrowserControl.Navigate(url);
            }
            catch { }
        }

        // Obtener el ListBox de la pestaña activa (si existe)
        private ListBox? GetActiveListBox()
        {
            try
            {
                if (InventoryTabs == null) return null;
                if (InventoryTabs.SelectedItem is TabItem ti)
                {
                    // En EnsureTabExists colocamos un Border cuyo Child es el ListBox
                    if (ti.Content is Border b && b.Child is ListBox lb) return lb;
                    // Si directamente es ListBox:
                    if (ti.Content is ListBox lb2) return lb2;
                }
            }
            catch { }
            return null;
        }

        // Propiedad de conveniencia para compatibilidad con código previo que refería ImagesListBox
        private ListBox? ImagesListBox => GetActiveListBox();

        // SelectionChanged handler usado por cada ListBox creado dinámicamente
        private void ImagesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                var lb = sender as ListBox ?? ImagesListBox;
                var selected = lb?.SelectedItem;
                if (selected is BitmapImage bi)
                {
                    // Imagen seleccionada
                    PreviewMedia.Stop();
                    PreviewMedia.Source = null;
                    PreviewMedia.Visibility = Visibility.Collapsed;

                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewImage.Source = bi;

                    // ocultar controles de media
                    PreviewMediaControls.Visibility = Visibility.Collapsed;

                    TxtPreviewInfo.Text = "Vista previa: imagen seleccionada (single click)";
                    try { BtnResetZoom_Click(null, null); } catch { }
                }
                else if (selected is CachedImage ci)
                {
                    // Imagen cacheada
                    PreviewMedia.Stop();
                    PreviewMedia.Source = null;
                    PreviewMedia.Visibility = Visibility.Collapsed;

                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewImage.Source = ci.Image;

                    // ocultar controles de media
                    PreviewMediaControls.Visibility = Visibility.Collapsed;

                    TxtPreviewInfo.Text = $"Vista previa: {Path.GetFileName(ci.FilePath)}";
                    try { BtnResetZoom_Click(null, null); } catch { }
                }
                else if (selected is string s && (!string.IsNullOrEmpty(s)))
                {
                    // Puede ser ruta local o URL remota
                    bool isLocalFile = File.Exists(s);
                    bool isRemote = Uri.TryCreate(s, UriKind.Absolute, out Uri? u) && (u.Scheme == "http" || u.Scheme == "https");

                    if (isLocalFile || isRemote)
                    {
                        try
                        {
                            // MOSTRAR SOLO THUMBNAIL en preview (no reproducir localmente). Los controles multimedia
                            // en el preview controlarán la proyección.
                            PreviewMedia.Stop();
                            PreviewMedia.Source = null;
                            PreviewMedia.Visibility = Visibility.Collapsed;

                            // Intentar obtener thumbnail del fichero (icono) como preview
                            BitmapImage? thumb = null;
                            try
                            {
                                if (isLocalFile)
                                {
                                    thumb = PlatformInterop.GetFileIconAsBitmap(s) ?? null;
                                }
                                else
                                {
                                    // Para URLs no intentamos descargar aquí; usar placeholder
                                    thumb = null;
                                }
                            }
                            catch { thumb = null; }

                            if (thumb != null)
                            {
                                PreviewImage.Source = thumb;
                            }
                            else
                            {
                                // placeholder simple
                                PreviewImage.Source = CreateGenericPlaceholderThumbnail(320, 180);
                            }

                            PreviewImage.Visibility = Visibility.Visible;
                            PreviewMediaControls.Visibility = Visibility.Visible; // mantener botones visibles
                            TxtPreviewInfo.Text = isLocalFile ? $"Vista preview: vídeo {Path.GetFileName(s)}" : $"Vista preview: vídeo {u?.Host}";

                            // Guardar ruta para que los botones del preview invoquen la reproducción en la proyección
                            _lastSelectedVideoPath = s;
                        }
                        catch
                        {
                            // fallback a image preview si no se puede reproducir
                            PreviewMedia.Stop();
                            PreviewMedia.Source = null;
                            PreviewMedia.Visibility = Visibility.Collapsed;
                            PreviewImage.Visibility = Visibility.Visible;
                            PreviewImage.Source = null;
                            PreviewMediaControls.Visibility = Visibility.Collapsed;
                            TxtPreviewInfo.Text = "Imagen / Video";
                        }
                    }
                    else
                    {
                        // string que no es archivo ni url
                        PreviewMedia.Stop();
                        PreviewMedia.Source = null;
                        PreviewMedia.Visibility = Visibility.Collapsed;
                        PreviewImage.Source = null;
                        PreviewImage.Visibility = Visibility.Visible;
                        PreviewMediaControls.Visibility = Visibility.Collapsed;
                        TxtPreviewInfo.Text = "Imagen / Video";
                    }
                }
                else if (selected is VideoListItem vli)
                {
                    try
                    {
                        PreviewMedia.Stop();
                        PreviewMedia.Source = null;
                        PreviewMedia.Visibility = Visibility.Collapsed;

                        if (vli.Thumbnail != null) PreviewImage.Source = vli.Thumbnail;
                        else PreviewImage.Source = CreateGenericPlaceholderThumbnail(320, 180);

                        PreviewImage.Visibility = Visibility.Visible;
                        PreviewMediaControls.Visibility = Visibility.Visible;
                        TxtPreviewInfo.Text = $"Vista previa: vídeo {vli.FileName}";

                        _lastSelectedVideoPath = vli.FilePath;
                    }
                    catch
                    {
                        PreviewImage.Visibility = Visibility.Visible;
                        PreviewMediaControls.Visibility = Visibility.Collapsed;
                        TxtPreviewInfo.Text = "Imagen / Video";
                    }
                }
                else
                {
                    PreviewMedia.Stop();
                    PreviewMedia.Source = null;
                    PreviewMedia.Visibility = Visibility.Collapsed;
                    PreviewImage.Source = null;
                    PreviewImage.Visibility = Visibility.Visible;

                    PreviewMediaControls.Visibility = Visibility.Collapsed;

                    TxtPreviewInfo.Text = "Imagen / Video";
                }
            }
            catch { }
        }

        // Doble click -> proyectar
        private async void ImagesListBox_MouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            try
            {
                var lb = sender as ListBox ?? ImagesListBox;
                var selected = lb?.SelectedItem;
                if (selected is string s && !string.IsNullOrEmpty(s))
                {
                    // si es ruta de vídeo o URL, notificar para que MainWindow reproduzca en proyección
                    if (_onVideoSelected != null)
                    {
                        try { _onVideoSelected.Invoke(s); } catch { }
                        return;
                    }

                    // Fallback: reproducir directamente usando ProyeccionWindow local
                    try
                    {
                        var temp = await CopyMediaToTempAsync(s, forProjection: true).ConfigureAwait(false);
                        await Dispatcher.InvokeAsync(() => {
                            try
                            {
                                var pw = EnsureProjectionWindow();
                                if (!string.IsNullOrWhiteSpace(temp)) pw.PlayVideo(temp);
                                else
                                {
                                    if (Uri.TryCreate(s, UriKind.Absolute, out Uri? u) && (u.Scheme=="http"||u.Scheme=="https")) pw.PlayVideo(u);
                                    else pw.PlayVideo(s);
                                }
                            }
                            catch { }
                        });
                    }
                    catch { }

                    return;
                }
                if (selected is VideoListItem vli && !string.IsNullOrEmpty(vli.FilePath))
                {
                    if (_onVideoSelected != null)
                    {
                        try { _onVideoSelected.Invoke(vli.FilePath); } catch { }
                        return;
                    }

                    try
                    {
                        var temp = await CopyMediaToTempAsync(vli.FilePath, forProjection: true).ConfigureAwait(false);
                        await Dispatcher.InvokeAsync(() => {
                            try
                            {
                                var pw = EnsureProjectionWindow();
                                if (!string.IsNullOrWhiteSpace(temp)) pw.PlayVideo(temp);
                                else pw.PlayVideo(vli.FilePath);
                            }
                            catch { }
                        });
                    }
                    catch { }

                    return;
                }

                // otherwise try project image
                TryProjectSelectedItem();
            }
            catch { }
        }

        // Enter -> proyectar
        private void ImagesListBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryProjectSelectedItem();
                e.Handled = true;
            }
        }

        // Centraliza la lógica de proyectar el elemento seleccionado en la lista activa
        private void TryProjectSelectedItem()
        {
            try
            {
                var lb = ImagesListBox;
                if (lb == null) return;
                var selected = lb.SelectedItem;
                if (selected == null) return;

                BitmapImage? toProject = null;
                if (selected is BitmapImage bi) toProject = bi;
                else if (selected is CachedImage ci) toProject = ci.Image;
                else if (selected is string path && File.Exists(path))
                {
                    try { toProject = LoadBitmapFromFileCached(path); } catch { }
                }

                if (toProject != null)
                {
                    try
                    {
                        if (_onImageSelected != null)
                        {
                            try { _onImageSelected.Invoke(toProject); } catch { }
                        }
                        else
                        {
                            // No MainWindow callback: project directly using local ProyeccionWindow
                            try
                            {
                                var pw = EnsureProjectionWindow();
                                if (pw != null)
                                {
                                    try { pw.StopVideo(); pw.SetLastWasTextoAnio(false); pw.MostrarImagenTexto(toProject); } catch { }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Slider handlers para preview
        private bool _isPreviewDragging = false;
        private void PreviewSldTimeline_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isPreviewDragging = true;
        }

        private void PreviewSldTimeline_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isPreviewDragging = false;
            try
            {
                // Al soltar el slider en el preview solicitamos a la proyección que haga seek
                try
                {
                    // Al soltar el slider en el preview solicitamos a la proyección que haga seek
                    try
                    {
                        var pw = EnsureProjectionWindow();
                        if (pw != null) pw.SeekToFraction(PreviewSldTimeline.Value);
                    }
                    catch { }
                }
                catch { }
            }
            catch { }
        }

        private void PreviewSldTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // No actualizar desde PreviewMedia; la proyección (MainWindow/ProyeccionWindow) empujará
            // los valores a este slider. Si el usuario está arrastrando, el Value cambiará localmente.
            if (_isPreviewDragging)
            {
                // opcional: actualizar texto localmente si conocemos duración desde la proyección
                try
                {
                    // intentar obtener duración desde la proyección
                    try
                    {
                        var pw = EnsureProjectionWindow();
                        var dur = pw?.GetVideoDuration();
                        if (dur.HasValue)
                        {
                            var pos = TimeSpan.FromSeconds(PreviewSldTimeline.Value * dur.Value.TotalSeconds);
                            TxtPreviewCurrentTime.Text = pos.ToString(@"hh\:mm\:ss");
                            TxtPreviewTotalTime.Text = "/" + dur.Value.ToString(@"hh\:mm\:ss");
                        }
                    }
                    catch { }
                }
                catch { }
            }
        }

        // Permite que MainWindow le pida a BrowserWindow que detenga la reproducción en el preview
        // y libere la fuente del MediaElement antes de proyectar para evitar conflictos.
        public void StopPreviewPlaybackAndReset()
        {
            try
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        PreviewMedia?.Stop();
                        try { PreviewMedia.Source = null; } catch { }
                        PreviewMedia.Visibility = Visibility.Collapsed;
                        PreviewImage.Visibility = Visibility.Visible;
                        _previewIsPlaying = false;
                        try { BtnPreviewPlayPause.Content = "⏵"; } catch { }
                        try { StopPreviewTimer(); } catch { }
                        try { PreviewSldTimeline.Value = 0; } catch { }
                        try { TxtPreviewCurrentTime.Text = "00:00:00"; } catch { }
                    }
                    catch { }
                }));
            }
            catch { }
        }

        // Llamado por MainWindow cuando la proyección avanza para reflejar progreso en el preview
        public void UpdatePreviewPlayback(TimeSpan position, TimeSpan? duration)
        {
            try
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (duration.HasValue && duration.Value.TotalSeconds > 0)
                        {
                            double frac = Math.Max(0.0, Math.Min(1.0, position.TotalSeconds / duration.Value.TotalSeconds));
                            try { PreviewSldTimeline.Value = frac; } catch { }
                            try { TxtPreviewCurrentTime.Text = position.ToString(@"hh\:mm\:ss"); } catch { }
                            try { TxtPreviewTotalTime.Text = "/" + duration.Value.ToString(@"hh\:mm\:ss"); } catch { }
                        }
                        else
                        {
                            try { TxtPreviewCurrentTime.Text = position.ToString(@"hh\:mm\:ss"); } catch { }
                        }
                    }
                    catch { }
                }));
            }
            catch { }
        }

        // Simple model para listar vídeos con thumbnail
        private class VideoListItem
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName => Path.GetFileName(FilePath);
            public BitmapImage? Thumbnail { get; set; }
            public override string ToString() => FileName;
        }

        private async Task<BitmapImage?> GenerateVideoThumbnailAsync(string path, int w = 160, int h = 90)
        {
            try
            {
                return await Task.Run(() => PlatformInterop.GenerateVideoFrameThumbnail(path, w, h));
            }
            catch { return null; }
        }
    }
}