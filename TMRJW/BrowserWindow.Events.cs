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

namespace TMRJW
{
    // Tipo ligero para representar imágenes cacheadas en disco
    internal class CachedImage
    {
        public string FilePath { get; set; } = string.Empty;
        public BitmapImage Image { get; set; } = null!;
        public override string ToString() => Path.GetFileName(FilePath);
    }

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
            try
            {
                var url = WebBrowserControl.Source?.AbsoluteUri ?? UrlBox.Text;
                if (string.IsNullOrWhiteSpace(url)) return;

                // extraer URIs
                var uris = await OnlineLibraryHelper.ExtractImageUrisFromPageAsync(url, CancellationToken.None);
                if (!uris.Any())
                {
                    MessageBox.Show("No se encontraron imágenes en la página.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // descargar todas (secuencial para evitar sobrecarga; se puede paralelizar con limitador)
                var imagesList = new List<(Uri Uri, byte[]? Bytes)>();
                foreach (var u in uris)
                {
                    try
                    {
                        var bytes = await s_http.GetByteArrayAsync(u).ConfigureAwait(false);
                        imagesList.Add((u, bytes));
                    }
                    catch
                    {
                        imagesList.Add((u, null));
                    }
                }

                var added = 0;
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
                                    ImagesListBox.Items.Add(ci);
                                    added++;
                                }
                            }
                        }
                        catch { }
                    }
                    MessageBox.Show($"Capturadas {added} imágenes y añadidas al inventario.", "Captura completa", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch
            {
                MessageBox.Show("Error al capturar imágenes de la página.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                if (ImagesListBox.SelectedItem is CachedImage ciSel)
                    imgToProject = ciSel.Image;
                else if (ImagesListBox.SelectedItem is BitmapImage biSel)
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
                    MessageBox.Show("Proyección encendida: esperando a que la imagen termine de descargarse...", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
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
                            ImagesListBox.Items.Add(new CachedImage { FilePath = "", Image = bmp });
                            ImagesListBox.SelectedItem = ImagesListBox.Items[ImagesListBox.Items.Count - 1];
                            try { _onImageSelected?.Invoke(bmp); } catch { }
                        });
                        return;
                    }
                }

                // Nada disponible
                MessageBox.Show("No hay imagen seleccionada para proyectar. Selecciona una imagen en el inventario o en la previsualización.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Al apagar: notificar MainWindow para desactivar la proyección (si existe)
                if (this.Owner is MainWindow mw)
                {
                    try
                    {
                        var projBtn = mw.FindName("BtnProyectarHDMI") as System.Windows.Controls.Button;
                        if (projBtn != null)
                        {
                            projBtn.Dispatcher.Invoke(() => projBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));
                        }
                    }
                    catch { }
                }
            }
        }

        private void BtnLoadExtra_Click(object sender, RoutedEventArgs e)
        {
            // Abrir selector de archivos y añadir imágenes al inventario simple
            var dlg = new OpenFileDialog { Multiselect = true, Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.gif|Videos|*.mp4;*.wmv;*.avi" };
            if (dlg.ShowDialog() != true) return;

            foreach (var f in dlg.FileNames)
            {
                try
                {
                    if (File.Exists(f))
                    {
                        if (f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
                        {
                            ImagesListBox.Items.Add(System.IO.Path.GetFileName(f) + " (video)");
                        }
                        else
                        {
                            // copiar al cache para uniformidad
                            var bytes = File.ReadAllBytes(f);
                            var path = SaveBytesToCacheAsync(bytes, Path.GetExtension(f)).GetAwaiter().GetResult();
                            var bi = LoadBitmapFromFileCached(path);
                            if (bi != null) ImagesListBox.Items.Add(new CachedImage { FilePath = path, Image = bi });
                        }
                    }
                }
                catch { }
            }
        }

        private void BtnTextoDelAnio_Click(object sender, RoutedEventArgs e)
        {
            // Intentar delegar a MainWindow si existe (dispara su botón de Texto del Año)
            if (this.Owner is MainWindow mw)
            {
                try
                {
                    var btn = mw.FindName("BtnTextoDelAnio") as System.Windows.Controls.Button;
                    if (btn != null)
                    {
                        btn.Dispatcher.Invoke(() => btn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));
                        return;
                    }
                }
                catch { }
            }

            MessageBox.Show("Acción: proyectar 'Texto del Año' (no se encontró MainWindow).", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ---------------------------------------
        // Handlers añadidos para corregir errores
        // ---------------------------------------

        // Cerrar la ventana y volver al MainWindow
        private void BtnReturnToMain_Click(object sender, RoutedEventArgs e)
        {
            try { this.Close(); } catch { }
        }

        // Preview: reproducir media en el panel de previsualización
        private void BtnPreviewPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PreviewMedia == null) return;

                // Si hay Source asignado, reproducir; si no, intentar detectar si el item seleccionado es una ruta de vídeo en formato de texto
                if (PreviewMedia.Source != null)
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewMedia.Visibility = Visibility.Visible;
                    PreviewMedia.Play();
                }
            }
            catch { }
        }

        private void BtnPreviewPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PreviewMedia == null) return;
                PreviewMedia.Pause();
            }
            catch { }
        }

        private void BtnPreviewStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PreviewMedia == null) return;
                PreviewMedia.Stop();
                PreviewMedia.Visibility = Visibility.Collapsed;
                PreviewImage.Visibility = Visibility.Visible;
            }
            catch { }
        }

        // Controles multimedia (panel inferior) que manejan el mismo MediaElement de previsualización
        private void BtnMediaPlay_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PreviewMedia == null) return;
                if (PreviewMedia.Source == null)
                {
                    // intentar tomar Source desde selección en ImagesListBox si es texto (nombre de archivo)
                    if (ImagesListBox.SelectedItem is string s && File.Exists(s))
                        PreviewMedia.Source = new Uri(s);
                }

                if (PreviewMedia.Source != null)
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewMedia.Visibility = Visibility.Visible;
                    PreviewMedia.Play();
                }
            }
            catch { }
        }

        private void BtnMediaPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PreviewMedia?.Pause();
            }
            catch { }
        }

        private void BtnMediaStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (PreviewMedia == null) return;
                PreviewMedia.Stop();
                PreviewMedia.Visibility = Visibility.Collapsed;
                PreviewImage.Visibility = Visibility.Visible;
            }
            catch { }
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
            var (s, _) = EnsurePreviewTransforms();
            s.ScaleX += ZoomStep;
            s.ScaleY += ZoomStep;
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            var (s, _) = EnsurePreviewTransforms();
            s.ScaleX = Math.Max(0.2, s.ScaleX - ZoomStep);
            s.ScaleY = Math.Max(0.2, s.ScaleY - ZoomStep);
        }

        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            var (s, t) = EnsurePreviewTransforms();
            s.ScaleX = 1.0; s.ScaleY = 1.0;
            t.X = 0; t.Y = 0;
        }

        private void BtnPanLeft_Click(object sender, RoutedEventArgs e)
        {
            var (_, t) = EnsurePreviewTransforms();
            t.X -= 40;
        }

        private void BtnPanRight_Click(object sender, RoutedEventArgs e)
        {
            var (_, t) = EnsurePreviewTransforms();
            t.X += 40;
        }

        private void BtnPanUp_Click(object sender, RoutedEventArgs e)
        {
            var (_, t) = EnsurePreviewTransforms();
            t.Y -= 40;
        }

        private void BtnPanDown_Click(object sender, RoutedEventArgs e)
        {
            var (_, t) = EnsurePreviewTransforms();
            t.Y += 40;
        }

        // -------------------------
        // Helpers para cache/IO
        // -------------------------

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
                var dir = GetCacheDirectory(); // GetCacheDirectory está en BrowserWindow.xaml.cs
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