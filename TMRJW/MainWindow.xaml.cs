using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TMRJW.Properties;
using VersOne.Epub;
using System.Windows.Input;
using System.Windows.Interop;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls.Primitives;

namespace TMRJW
{
    public partial class MainWindow : Window
    {
        private ProyeccionWindow? proyeccionWindow;
        private bool _isProjecting = false;

        private List<BitmapImage> _epubImages = new List<BitmapImage>();
        private List<BitmapImage> _userImages = new List<BitmapImage>();
        private List<GrupoImagenes> _gruposImagenes = new List<GrupoImagenes>();

        private ObservableCollection<VideoItem> _videos = new ObservableCollection<VideoItem>();

        private double _previewScale = 1.0;
        private const double PreviewMinScale = 0.2;
        private const double PreviewMaxScale = 5.0;
        private const double PreviewScaleStep = 0.1;
        private bool _isPanningPreview = false;
        private Point _lastPreviewMousePos;

        private double _monitorScale = 1.0;
        private bool _isPanningMonitor = false;
        private Point _lastMonitorMousePos;

        private bool _isTimelineDragging = false;
        private int _thumbsPerRow = 3;
        private object? vi;
        private const double MonitorPanStep = 40.0;

        public MainWindow()
        {
            InitializeComponent();
            // No crear proyeccionWindow aquí; se crea en OpenProyeccionOnSelectedMonitor (MainWindow.Extensions.cs)
            proyeccionWindow = null;

            this.Closing += MainWindow_Closing;

            var listaVideos = FindControl<ListBox>("ListaVideos");
            if (listaVideos != null)
            {
                listaVideos.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listaVideos.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                listaVideos.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, false);

                listaVideos.Loaded += (s, e) => UpdateWrapPanelItemSize(listaVideos);
                listaVideos.SizeChanged += (s, e) => UpdateWrapPanelItemSize(listaVideos);
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                // Cerrar la ventana de proyección si existe
                proyeccionWindow?.Close();
            }
            catch
            {
                // Ignorar errores de cierre
            }
            // Forzar el cierre de la aplicación para liberar el ejecutable
            Application.Current.Shutdown();
        }

        // Helper para obtener controles por nombre (evita dependencias del campo generado por XAML si faltan)
        private T? FindControl<T>(string name) where T : class
        {
            return this.FindName(name) as T;
        }

        private async void BtnCargarArchivo_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Archivos EPUB/HTML (*.epub;*.html;*.htm)|*.epub;*.html;*.htm"
            };

            if (openFileDialog.ShowDialog() != true) return;

            string rutaArchivo = openFileDialog.FileName;

            try
            {
                if (Path.GetExtension(rutaArchivo).Equals(".epub", StringComparison.OrdinalIgnoreCase))
                {
                    _epubImages.Clear();
                    var epubBook = await EpubReader.ReadBookAsync(rutaArchivo);

                    var primeraImagen = CargarImagenesEPUB(epubBook);

                    int audioCount = 0;

                    if (primeraImagen != null)
                    {
                        FindControl<Image>("MonitorDeSalida")?.SetCurrentValue(Image.SourceProperty, primeraImagen);

                        // Mostrar en la ventana de proyección aunque _isProjecting sea false,
                        // el usuario puede activar proyección con el botón ON/OFF.
                        try { proyeccionWindow?.MostrarImagenTexto(primeraImagen); } catch { }

                        FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, $"Primera imagen EPUB mostrada. Total de imágenes: {_epubImages.Count}");
                    }

                    AgruparImagenesPorSeccion();
                    MostrarImagenesEnPanelDinamico();

                    LlenarListaProgramaDesdeTexto($"EPUB cargado: {epubBook.Title}\nImágenes: {_epubImages.Count}\nAudio: {audioCount}");

                    MessageBox.Show(
                        $"EPUB '{epubBook.Title}' cargado exitosamente. Imágenes: {_epubImages.Count}, Audio: {audioCount}",
                        "Carga Completa", MessageBoxButton.OK, MessageBoxImage.Information
                    );
                }
                else
                {
                    string fileContent = File.ReadAllText(rutaArchivo);
                    LlenarListaProgramaDesdeTexto("Contenido cargado desde archivo:\n" +
                        fileContent.Substring(0, Math.Min(500, fileContent.Length)) + "...");
                    MessageBox.Show($"Guía Semanal '{Path.GetFileName(rutaArchivo)}' cargada exitosamente. (Modo Offline)", "Carga Completa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // Mostrar información completa del error para depuración
                MessageBox.Show($"Error al cargar o leer el archivo:\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, $"Error: {ex.Message}");
            }
        }

        // -------------------- MÉTODOS AUXILIARES --------------------

        private BitmapImage? CargarImagenesEPUB(EpubBook epubBook)
        {
            BitmapImage? primeraImagen = null;

            if (epubBook?.Content?.Images?.Local == null) return null;

            foreach (var imgFile in epubBook.Content.Images.Local)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    using (var ms = new MemoryStream(imgFile.Content))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }

                    _epubImages.Add(bitmap);

                    if (primeraImagen == null)
                        primeraImagen = bitmap;
                }
                catch
                {
                    // Si alguna imagen falla, continuar con las restantes
                }
            }

            // Ordenar todas las imágenes en orden descendente por resolución (mayor a menor)
            _epubImages = _epubImages
                .OrderByDescending(b => (long)b.PixelWidth * b.PixelHeight)
                .ToList();

            return _epubImages.FirstOrDefault();
        }

        private void AgruparImagenesPorSeccion()
        {
            // Reconstruir grupo de forma determinística (orden fijo):
            var nuevos = new List<GrupoImagenes>();

            if (_userImages.Count > 0)
            {
                nuevos.Add(new GrupoImagenes
                {
                    TituloPestana = "Imágenes Cargadas",
                    Imagenes = _userImages.ToList()
                });
            }

            nuevos.Add(new GrupoImagenes
            {
                TituloPestana = "📚 Todas las Imágenes (Introducción)",
                Imagenes = _epubImages.ToList(),
                EsIntroduccion = true
            });

            int imagesPerGroup = 5;
            int imageIndex = 0;
            int groupNumber = 1;

            while (imageIndex < _epubImages.Count)
            {
                nuevos.Add(new GrupoImagenes
                {
                    TituloPestana = $"Punto {groupNumber}",
                    Imagenes = _epubImages.Skip(imageIndex).Take(imagesPerGroup).ToList()
                });

                imageIndex += imagesPerGroup;
                groupNumber++;
            }

            _gruposImagenes = nuevos;
        }

        private void MostrarImagenesEnPanelDinamico()
        {
            var tabControl = FindControl<TabControl>("TabControlImagenes");
            if (tabControl == null) return;

            string? selectedHeader = (tabControl.SelectedItem as TabItem)?.Header?.ToString();
            tabControl.Items.Clear();

            DataTemplate? itemTemplate = this.TryFindResource("ImageItemDataTemplate") as DataTemplate;
            if (itemTemplate == null)
            {
                var factoryImg = new FrameworkElementFactory(typeof(Image));
                factoryImg.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding());
                // No fijar tamaño rígido aquí: el WrapPanel controlará el ItemWidth dinámicamente
                factoryImg.SetValue(Image.StretchProperty, System.Windows.Media.Stretch.UniformToFill);
                factoryImg.SetValue(FrameworkElement.MarginProperty, new Thickness(6));
                factoryImg.SetValue(Image.CursorProperty, System.Windows.Input.Cursors.Hand);
                itemTemplate = new DataTemplate { VisualTree = factoryImg };
            }

            for (int g = 0; g < _gruposImagenes.Count; g++)
            {
                var grupo = _gruposImagenes[g];
                var orderedImages = grupo.Imagenes.OrderByDescending(b => (long)b.PixelWidth * b.PixelHeight).ToList();

                // ListBox que mostrará todas las miniaturas (scroll vertical)
                var listBox = new ListBox
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    ItemTemplate = itemTemplate,
                    SelectionMode = SelectionMode.Single
                };

                // Evitar scroll horizontal (importante para que WrapPanel haga wrap correctamente)
                listBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                // Desactivar content scrolling virtualizado para evitar comportamiento extraño al redimensionar
                listBox.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, false);

                // ItemsPanel: WrapPanel sin ItemWidth fijo; lo ajustaremos en Loaded/SizeChanged
                var itemsPanel = new ItemsPanelTemplate();
                var panelFactory = new FrameworkElementFactory(typeof(WrapPanel));
                panelFactory.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
                panelFactory.SetValue(WrapPanel.HorizontalAlignmentProperty, HorizontalAlignment.Left);
                itemsPanel.VisualTree = panelFactory;
                listBox.ItemsPanel = itemsPanel;

                // Asignar todas las miniaturas (sin paginación)
                listBox.ItemsSource = orderedImages;

                // Handlers
                listBox.SelectionChanged += ListBoxImagenes_PreviewSelectionChanged;
                listBox.MouseDoubleClick += ListBoxImagenes_MouseDoubleClick;

                // Después de añadir al visual tree ajustamos el tamaño de cada celda según ancho disponible
                listBox.Loaded += (s, e) =>
                {
                    UpdateWrapPanelItemSize(listBox);
                };
                listBox.SizeChanged += (s, e) =>
                {
                    UpdateWrapPanelItemSize(listBox);
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                listBox.VerticalContentAlignment = VerticalAlignment.Top;
                Grid.SetRow(listBox, 0);
                grid.Children.Add(listBox);

                var tabItem = new TabItem
                {
                    Header = grupo.TituloPestana,
                    Foreground = Brushes.Black,
                    Padding = new Thickness(10, 5, 10, 5),
                    Content = grid
                };

                tabControl.Items.Add(tabItem);
            }

            // Restaurar pestaña seleccionada
            if (!string.IsNullOrEmpty(selectedHeader))
            {
                for (int i = 0; i < tabControl.Items.Count; i++)
                {
                    if ((tabControl.Items[i] as TabItem)?.Header?.ToString() == selectedHeader)
                    {
                        tabControl.SelectedIndex = i;
                        return;
                    }
                }
            }

            if (tabControl.Items.Count > 0)
                tabControl.SelectedIndex = 0;
        }

        // -------------------- Helpers y llamadas a PlatformInterop --------------------

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

        // Reemplazamos las implementaciones locales por llamadas al helper estático PlatformInterop
        // LoadBitmapFromFile -> PlatformInterop.LoadBitmapFromFile
        // GetFileIconAsBitmap -> PlatformInterop.GetFileIconAsBitmap
        // GenerateVideoFrameThumbnail -> PlatformInterop.GenerateVideoFrameThumbnail

        // Ejemplo de uso en BtnAsociarMedia_Click (se mantiene la llamada a PlatformInterop dentro del método)
    }
}

