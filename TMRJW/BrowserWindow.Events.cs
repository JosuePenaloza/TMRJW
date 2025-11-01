using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

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

        private async void BtnCaptureFromPage_Click(object sender, RoutedEventArgs e)
        {
            // Reutiliza la lógica existente para extraer/descargar imágenes desde la página
            try { await RefreshImagesFromCurrentPageAsync(); } catch { }
        }

        private void BtnProjectionPower_Click(object sender, RoutedEventArgs e)
        {
            _projectionPowerOn = !_projectionPowerOn;
            var btnSender = sender as System.Windows.Controls.Button;
            if (btnSender != null)
                btnSender.Content = _projectionPowerOn ? "Proy ON" : "Proy OFF";

            if (_projectionPowerOn)
            {
                // Al encender: intentar enviar la imagen seleccionada al MainWindow vía callback
                BitmapImage? imgToProject = null;

                // Preferir selección en inventario
                if (ImagesListBox.SelectedItem is BitmapImage selBI)
                {
                    imgToProject = selBI;
                }
                // Si hay imagen en preview
                else if (PreviewImage?.Source is BitmapImage previewBI)
                {
                    imgToProject = previewBI;
                }

                if (imgToProject != null)
                {
                    try { _onImageSelected?.Invoke(imgToProject); } catch { }
                }
                else
                {
                    MessageBox.Show("No hay imagen seleccionada para proyectar. Selecciona una imagen en el inventario o en la previsualización.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                // Al apagar: intentar notificar al MainWindow para desactivar la proyección
                if (this.Owner is MainWindow mw)
                {
                    try
                    {
                        var projBtn = mw.FindName("BtnProyectarHDMI") as System.Windows.Controls.Button;
                        if (projBtn != null)
                        {
                            // Disparar click en el botón del MainWindow para que su lógica de toggling se ejecute
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
                            var bi = new BitmapImage();
                            using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read);
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.StreamSource = fs;
                            bi.EndInit();
                            bi.Freeze();

                            ImagesListBox.Items.Add(bi);
                        }
                    }
                }
                catch { }
            }
        }

        private void BtnTextoDelAnio_Click(object sender, RoutedEventArgs e)
        {
            // Este botón proyecta el "Texto del Año" — implementar notificación a MainWindow o mostrar localmente
            // Por ahora avisamos al usuario; la funcionalidad completa la implementaremos según tu preferencia (MainWindow/ProyeccionWindow).
            MessageBox.Show("Acción: proyectar 'Texto del Año' (implementa la lógica que prefieras).", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
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
    }
}