using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using System.ComponentModel;

namespace TMRJW
{
    public partial class MainWindow : Window
    {
        // Clase ligera para videos (única definición centralizada)
        private class VideoItem : INotifyPropertyChanged
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;

            private BitmapImage? _thumbnail;
            public BitmapImage? Thumbnail
            {
                get => _thumbnail;
                set
                {
                    if (!Equals(_thumbnail, value))
                    {
                        _thumbnail = value;
                        OnPropertyChanged(nameof(Thumbnail));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        // Comprueba extensiones de imagen simples
        private static bool IsImageExtension(string ext)
        {
            switch (ext?.ToLowerInvariant())
            {
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".gif":
                    return true;
                default:
                    return false;
            }
        }

        // Wrapper para PlatformInterop (cargar imagen)
        private BitmapImage? LoadBitmapFromFileWrapper(string path) => PlatformInterop.LoadBitmapFromFile(path);

        // Wrapper para PlatformInterop (icono/thumbnail de fichero)
        private BitmapImage? GetFileIconAsBitmapWrapper(string path) => PlatformInterop.GetFileIconAsBitmap(path);

        // Wrapper para generar thumbnail de video (ejecuta en background + dispatcher)
        private Task<BitmapImage?> GenerateVideoFrameThumbnailAsync(string path, int w, int h)
        {
            return Task.Run(() => PlatformInterop.GenerateVideoFrameThumbnail(path, w, h));
        }

        // Crear thumbnail placeholder local (usado como fallback)
        private BitmapImage CreatePlaceholderThumbnail(int width = 160, int height = 90)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                var ft = new FormattedText("VIDEO", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 20, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
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

        // Mostrar imagen en preview y (opcional) en proyección, detener vídeo
        private void DisplayImageAndStopVideo(BitmapImage img, bool showInProjection = false)
        {
            if (img == null) return;

            FindControl<Image>("PreviewImage")?.SetCurrentValue(Image.SourceProperty, img);
            FindControl<Image>("MonitorDeSalida")?.SetCurrentValue(Image.SourceProperty, img);

            try { proyeccionWindow?.StopVideo(); } catch { }

            FindControl<FrameworkElement>("MediaControlsPanel")?.SetCurrentValue(FrameworkElement.VisibilityProperty, Visibility.Collapsed);

            _previewScale = 1.0;
            (this.FindName("PreviewScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleXProperty, 1.0);
            (this.FindName("PreviewScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleYProperty, 1.0);
            if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt) { ptt.X = 0; ptt.Y = 0; }

            _monitorScale = 1.0;
            (this.FindName("MonitorScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleXProperty, 1.0);
            (this.FindName("MonitorScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleYProperty, 1.0);
            if (this.FindName("MonitorTranslateTransform") is TranslateTransform mtt) { mtt.X = 0; mtt.Y = 0; }

            if (showInProjection)
            {
                try
                {
                    OpenProyeccionOnSelectedMonitor();
                    proyeccionWindow?.MostrarImagenTexto(img);
                    if (proyeccionWindow != null)
                    {
                        if (proyeccionWindow.WindowState == WindowState.Minimized) proyeccionWindow.WindowState = WindowState.Normal;
                        proyeccionWindow.Show();
                        _isProjecting = true;
                        FindControl<Button>("BtnProyectarHDMI")?.SetCurrentValue(ContentProperty, "PROYECTAR ON/OFF (ON)");
                    }
                }
                catch { }
            }
        }

        // Handlers de la lista de miniaturas (preview single-click y double-click para proyectar)
        private void ListBoxImagenes_PreviewSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is BitmapImage selectedImage)
            {
                FindControl<Image>("PreviewImage")?.SetCurrentValue(Image.SourceProperty, selectedImage);
                FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, "Vista previa: imagen seleccionada (single-click)");
            }
        }

        // Firma compatible con MouseButtonEventHandler
        private void ListBoxImagenes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is BitmapImage selectedImage)
            {
                DisplayImageAndStopVideo(selectedImage, showInProjection: true);
                FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, "Reproduciendo: Imagen seleccionada (doble click)");
            }
        }

        // Update responsive para WrapPanel (miniaturas)
        private void UpdateWrapPanelItemSize(ListBox listBox)
        {
            if (listBox == null) return;

            var wrap = FindVisualChild<WrapPanel>(listBox);
            if (wrap == null) return;

            double availableWidth = listBox.ActualWidth;
            if (availableWidth <= 0) availableWidth = Math.Max(200, this.ActualWidth - 320);

            double scrollbarWidth = SystemParameters.VerticalScrollBarWidth;
            availableWidth = Math.Max(0, availableWidth - scrollbarWidth - listBox.Padding.Left - listBox.Padding.Right - 8);

            const double minThumbWidth = 140.0;
            const int maxCols = 3;

            int cols = Math.Min(maxCols, Math.Max(1, (int)Math.Floor(availableWidth / minThumbWidth)));
            if (cols < 1) cols = 1;

            double spacing = 12.0;
            double itemWidth = Math.Floor((availableWidth - (cols - 1) * spacing) / cols);
            if (itemWidth < 80) itemWidth = 80;
            double itemHeight = Math.Floor(itemWidth * 90.0 / 160.0); // relación ancho:alto

            try
            {
                wrap.ItemWidth = itemWidth;
                wrap.ItemHeight = itemHeight;
            }
            catch { }
        }

        // Llenar ListaPrograma desde un texto (método reutilizable)
        private void LlenarListaProgramaDesdeTexto(string textoPrograma)
        {
            var lista = FindControl<ListBox>("ListaPrograma");
            if (lista == null) return;

            lista.Items.Clear();
            string[] lineas = textoPrograma.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var linea in lineas)
            {
                if (linea.Length < 1) continue;

                if (linea.Contains("Cántico") || linea.Contains("Oración") || linea.Contains("Video") || linea.Contains("Tesoros"))
                    lista.Items.Add(new TextBlock { Text = linea.Trim(), Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });
                else
                    lista.Items.Add(new TextBlock { Text = linea.Trim(), Margin = new Thickness(5, 0, 0, 0) });
            }
        }
    }

}