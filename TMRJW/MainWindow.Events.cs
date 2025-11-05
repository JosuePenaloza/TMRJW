using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TMRJW
{
    public partial class MainWindow : Window
    {
        private void BtnAjustes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ajustes = new AjustesWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                ajustes.ShowDialog();
                try { OpenProyeccionOnSelectedMonitor(); } catch { }
            }
            catch { }
        }

        private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bw = new BrowserWindow { Owner = this };
                // Imagen: mostrar en preview/proyección como antes
                bw.SetImageSelectedCallback(img => Dispatcher.Invoke(() => DisplayImageAndStopVideo(img, true)));

                // Vídeo: cuando BrowserWindow notifique una ruta/URL de vídeo, detener primero la reproducción en el preview
                // para evitar conflicto en el acceso al fichero y luego abrir la proyección y reproducir.
                bw.SetVideoSelectedCallback(path => Dispatcher.Invoke(async () =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(path)) return;

                        // Pedir al BrowserWindow que libere su MediaElement
                        try { bw.StopPreviewPlaybackAndReset(); } catch { }

                        // Copiar a temporal antes de reproducir en proyección para evitar locks
                        string? tempPath = null;
                        try { tempPath = await bw.CopyMediaToTempAsync(path, forProjection: true).ConfigureAwait(false); } catch { }

                        // Asegurar ventana de proyección
                        if (proyeccionWindow == null) OpenProyeccionOnSelectedMonitor();
                        if (proyeccionWindow == null) return;

                        if (!string.IsNullOrWhiteSpace(tempPath))
                        {
                            proyeccionWindow.PlayVideo(tempPath);
                        }
                        else
                        {
                            // fallback a usar la ruta original
                            if (Uri.TryCreate(path, UriKind.Absolute, out Uri? u) && (u.Scheme == "http" || u.Scheme == "https"))
                                proyeccionWindow.PlayVideo(u);
                            else
                                proyeccionWindow.PlayVideo(path);
                        }
                    }
                    catch { }
                }));

                bw.Show();
            }
            catch { }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            var lista = FindControl<ListBox>("ListaVideos");
            if (lista == null || lista.Items.Count == 0) return;
            int idx = Math.Max(0, (lista.SelectedIndex < 0 ? 0 : lista.SelectedIndex) - 1);
            lista.SelectedIndex = idx;
            lista.ScrollIntoView(lista.SelectedItem);
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            var lista = FindControl<ListBox>("ListaVideos");
            if (lista == null || lista.Items.Count == 0) return;
            int idx = lista.SelectedIndex;
            idx = Math.Min(lista.Items.Count - 1, (idx < 0 ? 0 : idx + 1));
            lista.SelectedIndex = idx;
            lista.ScrollIntoView(lista.SelectedItem);
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (proyeccionWindow == null) OpenProyeccionOnSelectedMonitor();
                if (proyeccionWindow == null) return;

                // Obtener vídeo seleccionado (si lo hay)
                var lista = FindControl<ListBox>("ListaVideos");
                VideoItem? seleccionado = (lista?.SelectedItem as VideoItem);

                // INVOCAR los métodos (no acceder como propiedades)
                if (proyeccionWindow.IsPlayingVideo())
                {
                    proyeccionWindow.PauseVideo();
                    (sender as Button)?.SetCurrentValue(ContentProperty, "⏵");
                }
                else
                {
                    // Si hay un vídeo seleccionado, reproducir su ruta
                    if (seleccionado != null && !string.IsNullOrWhiteSpace(seleccionado.FilePath))
                    {
                        proyeccionWindow.PlayVideo(seleccionado.FilePath);
                    }
                    (sender as Button)?.SetCurrentValue(ContentProperty, "⏸");
                }
            }
            catch { }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            try { proyeccionWindow?.StopVideo(); } catch { }
            FindControl<Button>("BtnPlayPause")?.SetCurrentValue(ContentProperty, "⏵");
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("MonitorScaleTransform") is ScaleTransform mst)
            {
                _monitorScale = Math.Min(PreviewMaxScale, _monitorScale + PreviewScaleStep);
                mst.ScaleX = _monitorScale; mst.ScaleY = _monitorScale;
                ApplyClampMonitorTranslation();
                try { proyeccionWindow?.UpdateImageTransform(_monitorScale, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.X ?? 0, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.Y ?? 0); } catch { }
            }
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("MonitorScaleTransform") is ScaleTransform mst)
            {
                _monitorScale = Math.Max(PreviewMinScale, _monitorScale - PreviewScaleStep);
                mst.ScaleX = _monitorScale; mst.ScaleY = _monitorScale;
                ApplyClampMonitorTranslation();
                try { proyeccionWindow?.UpdateImageTransform(_monitorScale, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.X ?? 0, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.Y ?? 0); } catch { }
            }
        }

        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            _monitorScale = 1.0;
            if (this.FindName("MonitorScaleTransform") is ScaleTransform mst) { mst.ScaleX = 1.0; mst.ScaleY = 1.0; }
            if (this.FindName("MonitorTranslateTransform") is TranslateTransform mt) { mt.X = 0; mt.Y = 0; }
            try { proyeccionWindow?.UpdateImageTransform(1.0, 0, 0); } catch { }
        }

        private void BtnPanLeft_Click(object sender, RoutedEventArgs e) => PanMonitor(-MonitorPanStep, 0);
        private void BtnPanRight_Click(object sender, RoutedEventArgs e) => PanMonitor(MonitorPanStep, 0);
        private void BtnPanUp_Click(object sender, RoutedEventArgs e) => PanMonitor(0, -MonitorPanStep);
        private void BtnPanDown_Click(object sender, RoutedEventArgs e) => PanMonitor(0, MonitorPanStep);

        private void BtnPreviewReset_Click(object sender, RoutedEventArgs e)
        {
            _previewScale = 1.0;
            if (this.FindName("PreviewScaleTransform") is ScaleTransform pst) { pst.ScaleX = 1.0; pst.ScaleY = 1.0; }
            if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt) { ptt.X = 0; ptt.Y = 0; }
            ClampPreviewTranslation();
        }

        private void ListaPrograma_MouseDoubleClick(object sender, MouseButtonEventArgs e) { /* no-op */ }

        private void ListaPrograma_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    foreach (var f in files) FindControl<ListBox>("ListaPrograma")?.Items.Add(new TextBlock { Text = $"Archivo añadido: {Path.GetFileName(f)}", Foreground = Brushes.LightGray });
                }
            }
            catch { }
        }

        private void ListaVideos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var lista = sender as ListBox ?? FindControl<ListBox>("ListaVideos");
            var mediaPanel = FindControl<FrameworkElement>("MediaControlsPanel");
            var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
            var preview = FindControl<Image>("PreviewImage");

            if (lista?.SelectedItem != null)
            {
                mediaPanel?.SetCurrentValue(FrameworkElement.VisibilityProperty, Visibility.Visible);
                if (lista.SelectedItem is VideoItem vi)
                {
                    txtInfo?.SetCurrentValue(TextBlock.TextProperty, $"Seleccionado: {vi.FileName}");
                    if (preview != null && vi.Thumbnail != null) preview.SetCurrentValue(Image.SourceProperty, vi.Thumbnail);
                }
            }
            else
            {
                mediaPanel?.SetCurrentValue(FrameworkElement.VisibilityProperty, Visibility.Collapsed);
                txtInfo?.SetCurrentValue(TextBlock.TextProperty, "Reproduciendo: Ninguno");
            }
        }

        private void PreviewImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double oldScale = _previewScale;
            if (e.Delta > 0) _previewScale = Math.Min(PreviewMaxScale, _previewScale + PreviewScaleStep);
            else _previewScale = Math.Max(PreviewMinScale, _previewScale - PreviewScaleStep);

            double scaleFactor = oldScale <= 0 ? 1.0 : _previewScale / oldScale;
            var img = FindControl<Image>("PreviewImage");
            if (img == null) return;
            var pos = e.GetPosition(img);

            if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt)
            {
                ptt.X = (1 - scaleFactor) * pos.X + scaleFactor * ptt.X;
                ptt.Y = (1 - scaleFactor) * pos.Y + scaleFactor * ptt.Y;
            }
            if (this.FindName("PreviewScaleTransform") is ScaleTransform pst)
            {
                pst.ScaleX = _previewScale; pst.ScaleY = _previewScale;
            }
            ClampPreviewTranslation();
            e.Handled = true;
        }

        private void PreviewImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningPreview = true;
                _lastPreviewMousePos = e.GetPosition(this);
                try { Mouse.Capture(FindControl<Image>("PreviewImage")); } catch { }
            }
        }

        private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanningPreview || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(this);
            var dx = pos.X - _lastPreviewMousePos.X;
            var dy = pos.Y - _lastPreviewMousePos.Y;
            _lastPreviewMousePos = pos;
            if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt) { ptt.X += dx; ptt.Y += dy; }
            ClampPreviewTranslation();
        }

        private void PreviewImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) { _isPanningPreview = false; try { Mouse.Capture(null); } catch { } }
        }

        private void Monitor_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double oldScale = _monitorScale;
            if (e.Delta > 0) _monitorScale = Math.Min(PreviewMaxScale, _monitorScale + PreviewScaleStep);
            else _monitorScale = Math.Max(PreviewMinScale, _monitorScale - PreviewScaleStep);

            double scaleFactor = oldScale <= 0 ? 1.0 : _monitorScale / oldScale;
            var monitor = FindControl<Image>("MonitorDeSalida");
            if (monitor == null) return;
            var pos = e.GetPosition(monitor);

            if (this.FindName("MonitorTranslateTransform") is TranslateTransform mt)
            {
                mt.X = (1 - scaleFactor) * pos.X + scaleFactor * mt.X;
                mt.Y = (1 - scaleFactor) * pos.Y + scaleFactor * mt.Y;
            }
            if (this.FindName("MonitorScaleTransform") is ScaleTransform mst)
            {
                mst.ScaleX = _monitorScale; mst.ScaleY = _monitorScale;
            }
            ApplyClampMonitorTranslation();
            try { proyeccionWindow?.UpdateImageTransform(_monitorScale, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.X ?? 0, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.Y ?? 0); } catch { }
            e.Handled = true;
        }

        private void Monitor_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningMonitor = true;
                _lastMonitorMousePos = e.GetPosition(this);
                try { Mouse.Capture(FindControl<Image>("MonitorDeSalida")); } catch { }
            }
        }

        private void Monitor_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanningMonitor || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(this);
            var dx = pos.X - _lastMonitorMousePos.X;
            var dy = pos.Y - _lastMonitorMousePos.Y;
            _lastMonitorMousePos = pos;
            if (this.FindName("MonitorTranslateTransform") is TranslateTransform mt) { mt.X += dx; mt.Y += dy; }
            ApplyClampMonitorTranslation();
            try { proyeccionWindow?.UpdateImageTransform(_monitorScale, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.X ?? 0, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.Y ?? 0); } catch { }
        }

        private void Monitor_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) { _isPanningMonitor = false; try { Mouse.Capture(null); } catch { } }
        }

        private void SldTimeline_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isTimelineDragging = true;
        }

        private void SldTimeline_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isTimelineDragging = false;
            try
            {
                var sld = FindControl<Slider>("SldTimeline");
                if (sld != null)
                {
                    proyeccionWindow?.SeekToFraction(sld.Value);
                }
            }
            catch { }
        }

        private void SldTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Si el usuario está arrastrando, actualizar el texto de tiempo localmente
            if (!_isTimelineDragging) return;
            try
            {
                var sld = sender as Slider ?? FindControl<Slider>("SldTimeline");
                var txtCur = FindControl<TextBlock>("TxtCurrentTime");
                var txtTot = FindControl<TextBlock>("TxtTotalTime");

                if (sld == null) return;

                // Intentar derivar duración desde la proyección si está disponible
                TimeSpan? dur = null;
                try { dur = proyeccionWindow?.ProjectionMedia?.NaturalDuration.HasTimeSpan == true ? proyeccionWindow.ProjectionMedia.NaturalDuration.TimeSpan : (TimeSpan?)null; } catch { }

                double frac = Math.Max(0.0, Math.Min(1.0, sld.Value));
                string fmt(TimeSpan t) => t.ToString(@"hh\:mm\:ss");

                if (dur.HasValue && dur.Value.TotalSeconds >0)
                {
                    var pos = TimeSpan.FromSeconds(frac * dur.Value.TotalSeconds);
                    if (txtCur != null) txtCur.SetCurrentValue(TextBlock.TextProperty, fmt(pos));
                    if (txtTot != null) txtTot.SetCurrentValue(TextBlock.TextProperty, $" / {fmt(dur.Value)}");
                }
                else
                {
                    if (txtCur != null) txtCur.SetCurrentValue(TextBlock.TextProperty, "00:00:00");
                }
            }
            catch { }
        }

        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                double vol = (FindControl<Slider>("SldVolume")?.Value ?? 75) / 100.0;
                proyeccionWindow?.SetVolume(vol);
            }
            catch { }
        }

        // NOTE: La implementación de UpdateWrapPanelItemSize se ha eliminado de este archivo
        // porque ya existe en `MainWindow.SharedHelpers.cs`. Mantener una única definición evita
        // el error CS0111 (miembro duplicado). Llame simplemente a `UpdateWrapPanelItemSize(listBox)`
        // desde aquí y el compilador resolverá la única implementación.

        // Wrappers para ser invocadas desde BrowserWindow para controlar la proyección
        public void PlayProjectionFromPath(string path)
        {
            try
            {
                if (proyeccionWindow == null) OpenProyeccionOnSelectedMonitor();
                if (proyeccionWindow == null) return;

                // Intentar reproducir directamente; BrowserWindow ya puede haber copiado a temp para proyección
                proyeccionWindow.PlayVideo(path);
            }
            catch { }
        }

        public void PauseProjection()
        {
            try { proyeccionWindow?.PauseVideo(); } catch { }
        }

        public void StopProjection()
        {
            try { proyeccionWindow?.StopVideo(); } catch { }
        }

        public bool ProyeccionWindowIsPlaying()
        {
            try { return proyeccionWindow?.IsPlayingVideo() ?? false; } catch { return false; }
        }

        public void SeekProjectionToFraction(double fraction)
        {
            try { proyeccionWindow?.SeekToFraction(fraction); } catch { }
        }

        public TimeSpan? GetProjectionDuration()
        {
            try
            {
                if (proyeccionWindow == null) return null;
                var dur = proyeccionWindow.ProjectionMedia?.NaturalDuration.HasTimeSpan == true ? proyeccionWindow.ProjectionMedia.NaturalDuration.TimeSpan : (TimeSpan?)null;
                return dur;
            }
            catch { return null; }
        }
    }
}