using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace TMRJW
{
    public partial class ProyeccionWindow : Window
    {
        private const double MinScale = 0.2;
        private const double MaxScale = 5.0;
        private const double ScaleStep = 0.1;

        private double _currentScale = 1.0;
        private Point _lastMousePosition;
        private bool _isPanning = false;
        private bool _isPlayingVideo = false;

        // Exponer estado público para que MainWindow pueda consultar/alternar
        public bool IsPlayingVideo => _isPlayingVideo;

        public ProyeccionWindow()
        {
            InitializeComponent();

            // Ocultar controles para que la ventana proyectada solo muestre la imagen/video sin superposiciones
            try
            {
                if (BtnPlayPause != null) BtnPlayPause.Visibility = Visibility.Collapsed;
                if (BtnStop != null) BtnStop.Visibility = Visibility.Collapsed;
                if (VolumeSlider != null) VolumeSlider.Visibility = Visibility.Collapsed;

                // Desactivar interacción directa sobre la imagen en la ventana de proyección
                if (imageDisplay != null) imageDisplay.IsHitTestVisible = false;
                if (LayoutRoot != null) LayoutRoot.IsHitTestVisible = false;
            }
            catch { }

            // Asegurar que el Image use las transformaciones que definimos en XAML
            if (imageDisplay != null)
            {
                var tg = new TransformGroup();
                tg.Children.Add(ScaleTransform);
                tg.Children.Add(TranslateTransform);
                imageDisplay.RenderTransform = tg;
                imageDisplay.RenderTransformOrigin = new Point(0.0, 0.0);
            }

            // Inicial volumen
            try
            {
                if (mediaElement != null) mediaElement.Volume = (VolumeSlider?.Value ?? 75) / 100.0;
            }
            catch { }
        }

        public void MostrarImagenTexto(BitmapImage imagen)
        {
            if (mediaElement != null)
            {
                try { mediaElement.Stop(); } catch { }
                mediaElement.Visibility = Visibility.Collapsed;
                _isPlayingVideo = false;
                BtnPlayPause.Content = "Play";
            }

            if (imageDisplay != null)
            {
                imageDisplay.Source = imagen;
                imageDisplay.Visibility = Visibility.Visible;
                ResetTransform();
            }

            if (textDisplay != null)
            {
                textDisplay.Text = string.Empty;
                textDisplay.Visibility = Visibility.Collapsed;
            }
        }

        public void MostrarTextoPrograma(string contenido)
        {
            if (mediaElement != null)
            {
                try { mediaElement.Stop(); } catch { }
                mediaElement.Visibility = Visibility.Collapsed;
                _isPlayingVideo = false;
                BtnPlayPause.Content = "Play";
            }

            if (imageDisplay != null)
            {
                imageDisplay.Source = null;
                imageDisplay.Visibility = Visibility.Collapsed;
            }

            if (textDisplay != null)
            {
                textDisplay.Text = contenido;
                textDisplay.Visibility = Visibility.Visible;
            }

            ResetTransform();
        }

        // Reproducir video en la ventana de proyección (MediaElement en XAML)
        public void MostrarVideo(string rutaArchivo)
        {
            if (!System.IO.File.Exists(rutaArchivo)) throw new ArgumentException("Archivo no encontrado", nameof(rutaArchivo));

            if (imageDisplay != null) imageDisplay.Visibility = Visibility.Collapsed;
            if (textDisplay != null) textDisplay.Visibility = Visibility.Collapsed;

            if (mediaElement == null) return;

            try
            {
                mediaElement.Source = new Uri(rutaArchivo);
                mediaElement.Visibility = Visibility.Visible;
                mediaElement.Stop();
                mediaElement.Play();
                _isPlayingVideo = true;
                BtnPlayPause.Content = "Pause";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error al reproducir video", ex);
            }
        }

        // Métodos de control de video expuestos
        public void SetVolume(double volume)
        {
            if (mediaElement != null)
            {
                mediaElement.Volume = Math.Max(0.0, Math.Min(1.0, volume));
                if (VolumeSlider != null) VolumeSlider.Value = mediaElement.Volume * 100;
            }
        }

        public void PauseVideo()
        {
            if (mediaElement != null)
            {
                mediaElement.Pause();
                _isPlayingVideo = false;
                BtnPlayPause.Content = "Play";
            }
        }

        public void StopVideo()
        {
            if (mediaElement != null)
            {
                mediaElement.Stop();
                _isPlayingVideo = false;
                BtnPlayPause.Content = "Play";
            }
        }

        public void PlayVideo()
        {
            if (mediaElement != null)
            {
                mediaElement.Play();
                _isPlayingVideo = true;
                BtnPlayPause.Content = "Pause";
            }
        }

        private void ResetTransform()
        {
            _currentScale = 1.0;
            ScaleTransform.ScaleX = 1.0;
            ScaleTransform.ScaleY = 1.0;
            TranslateTransform.X = 0;
            TranslateTransform.Y = 0;
        }

        // Eventos de zoom / pan (conectados en XAML al Grid LayoutRoot)
        private void LayoutRoot_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double oldScale = _currentScale;
            if (e.Delta > 0)
                _currentScale = Math.Min(MaxScale, _currentScale + ScaleStep);
            else
                _currentScale = Math.Max(MinScale, _currentScale - ScaleStep);

            double scaleFactor = _currentScale / oldScale;

            var target = (IInputElement)imageDisplay ?? (IInputElement)LayoutRoot;
            var pos = e.GetPosition(target);

            // Ajustar translate para mantener el punto bajo el cursor
            TranslateTransform.X = (1 - scaleFactor) * (pos.X) + scaleFactor * TranslateTransform.X;
            TranslateTransform.Y = (1 - scaleFactor) * (pos.Y) + scaleFactor * TranslateTransform.Y;

            ScaleTransform.ScaleX = _currentScale;
            ScaleTransform.ScaleY = _currentScale;
            e.Handled = true;
        }

        private void LayoutRoot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanning = true;
                _lastMousePosition = e.GetPosition(this);
                try { Mouse.Capture(LayoutRoot); } catch { }
            }
        }

        private void LayoutRoot_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this);
                var dx = pos.X - _lastMousePosition.X;
                var dy = pos.Y - _lastMousePosition.Y;

                TranslateTransform.X += dx;
                TranslateTransform.Y += dy;

                _lastMousePosition = pos;
            }
        }

        private void LayoutRoot_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanning = false;
                try { Mouse.Capture(null); } catch { }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Asegurar transform por si fue creado antes
            if (imageDisplay != null)
            {
                var tg = new TransformGroup();
                tg.Children.Add(ScaleTransform);
                tg.Children.Add(TranslateTransform);
                imageDisplay.RenderTransform = tg;
            }
        }

        // Handlers UI
        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlayingVideo) PauseVideo(); else PlayVideo();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopVideo();
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (mediaElement != null) mediaElement.Volume = (VolumeSlider?.Value ?? 75) / 100.0;
            }
            catch { }
        }

        // Añadir método público para que MainWindow pueda actualizar transformaciones de la imagen proyectada
        public void UpdateImageTransform(double scale, double offsetX, double offsetY)
        {
            // Ejecutar en dispatcher por seguridad de hilos y para evitar excepciones UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    ScaleTransform.ScaleX = scale;
                    ScaleTransform.ScaleY = scale;
                    TranslateTransform.X = offsetX;
                    TranslateTransform.Y = offsetY;
                }
                catch { }
            });
        }
    }
}
