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
using System.Windows.Input; // añadido
using System.Runtime.InteropServices;
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
        private List<BitmapImage> _userImages = new List<BitmapImage>(); // imágenes cargadas por el usuario
        private List<GrupoImagenes> _gruposImagenes = new List<GrupoImagenes>();

        // private List<VideoItem> _videos = new List<VideoItem>(); // antiguo
        private ObservableCollection<VideoItem> _videos = new ObservableCollection<VideoItem>(); // videos precargados

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

        // Añadir campo para columnas de miniaturas
        private int _thumbsPerRow = 3; // por defecto 3 por fila
        private object? vi;

        // Controles manuales: zoom y pan para MonitorDeSalida
        private const double MonitorPanStep = 40.0; // píxeles por pulsación

        public MainWindow()
        {
            InitializeComponent();
            proyeccionWindow = new ProyeccionWindow();
            // No mostrar inicialmente; posicionarlo en el monitor configurado cuando se active
            proyeccionWindow?.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);

            // Registrar cierre
            this.Closing += MainWindow_Closing;

            // Registrar comportamiento responsive para ListaVideos (si existe en XAML)
            var listaVideos = FindControl<ListBox>("ListaVideos");
            if (listaVideos != null)
            {
                // asegurar que no haya scroll horizontal y que se recalculen tamaños al cargar/redimensionar
                listaVideos.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                listaVideos.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                listaVideos.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, false);

                listaVideos.Loaded += (s, e) => UpdateWrapPanelItemSize(listaVideos);
                listaVideos.SizeChanged += (s, e) => UpdateWrapPanelItemSize(listaVideos);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
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
                        proyeccionWindow?.MostrarImagenTexto(primeraImagen);

                        FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, $"Primera imagen EPUB mostrada. Total de imágenes: {_epubImages.Count}");
                    }

                    AgruparImagenesPorSeccion();
                    MostrarImagenesEnPanelDinamico();

                    LlenarListaProgramaDesdeTexto(
                        $"EPUB cargado: {epubBook.Title}\nImágenes: {_epubImages.Count}\nAudio: {audioCount}"
                    );

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

        // Reemplaza el método MostrarImagenesEnPanelDinamico y añade helpers para que las miniaturas sean responsivas.
        // Quita también los handlers `CboThumbsPerRow_SelectionChanged` y `BtnRefreshThumbs_Click` del archivo (no los incluyo aquí).

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

        // Helper para buscar control visual hijo de tipo T
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

        // SINGLE-CLICK preview: muestra en PreviewImage sin proyectar
        private void ListBoxImagenes_PreviewSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is BitmapImage selectedImage)
            {
                // Sólo actualizar la previsualización pequeña (PreviewImage).
                // No detener ni cambiar la proyección en segunda pantalla.
                FindControl<Image>("PreviewImage")?.SetCurrentValue(Image.SourceProperty, selectedImage);

                FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, "Vista previa: imagen seleccionada (single-click)");
            }
        }

        // DOBLE-CLICK: proyectar en ventana externa (mantener)
        private void ListBoxImagenes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is BitmapImage selectedImage)
            {
                // Mostrar en preview y monitor, detener vídeo y ocultar controles, además proyectar
                DisplayImageAndStopVideo(selectedImage, showInProjection: true);

                FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, "Reproduciendo: Imagen seleccionada del EPUB (doble click)");
            }
        }

        // SldVolume -> controla volumen del MediaElement en ProyeccionWindow
        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (proyeccionWindow != null)
            {
                double vol = (FindControl<Slider>("SldVolume")?.Value ?? 75) / 100.0;
                try { proyeccionWindow.SetVolume(vol); } catch { }
            }
        }

        private void LlenarListaProgramaDesdeTexto(string textoPrograma)
        {
            var lista = FindControl<ListBox>("ListaPrograma");
            if (lista == null) return;

            lista.Items.Clear();
            string[] lineas = textoPrograma.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var linea in lineas)
            {
                if (linea.Length < 5 || linea.Contains("=")) continue;

                if (linea.Contains("Cántico") || linea.Contains("Oración") || linea.Contains("Video") || linea.Contains("Tesoros"))
                    lista.Items.Add(new TextBlock { Text = linea.Trim(), Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });
                else
                    lista.Items.Add(new TextBlock { Text = linea.Trim(), Margin = new Thickness(5, 0, 0, 0) });
            }
        }

        private void LlenarListaProgramaDesdeError(string mensajeError)
        {
            var lista = FindControl<ListBox>("ListaPrograma");
            if (lista == null) return;

            lista.Items.Clear();
            lista.Items.Add(new TextBlock { Text = mensajeError, Foreground = Brushes.Red, FontWeight = FontWeights.Bold });
            lista.Items.Add(new TextBlock { Text = "Por favor, verifica tu conexión o usa 'Cargar Archivo' para modo Offline.", Margin = new Thickness(5, 5, 0, 0) });
        }

        private void BtnAjustes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ajustes = new AjustesWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                // Mostrar modal; el usuario guarda o cierra
                ajustes.ShowDialog();

                // Al cerrar, aplicar cambios relacionados (ej. monitor de salida)
                proyeccionWindow?.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la ventana de Ajustes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnProyectarHDMI_Click(object sender, RoutedEventArgs e)
        {
            var btn = FindControl<Button>("BtnProyectarHDMI");
            if (btn == null) return;

            if (!_isProjecting)
            {
                // Activar proyección
                _isProjecting = true;
                btn.SetCurrentValue(ContentProperty, "PROYECTAR ON/OFF (ON)");

                if (proyeccionWindow != null)
                {
                    proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                    proyeccionWindow.Show();

                    // Si hay una imagen seleccionada en el panel de imágenes, usarla
                    var tabControl = FindControl<TabControl>("TabControlImagenes");
                    if (tabControl != null && tabControl.SelectedItem is TabItem ti &&
                        ti.Content is Grid grid && grid.Children.OfType<ScrollViewer>().FirstOrDefault() is ScrollViewer sview &&
                        sview.Content is ListBox lb && lb.SelectedItem is BitmapImage sel)
                    {
                        proyeccionWindow.MostrarImagenTexto(sel);
                    }
                    else
                    {
                        // Si no, si existe imagen de "TextoAnio" en ajustes, mostrarla
                        var img = LoadBitmapFromFile(Settings.Default.ImagenTextoAnio);
                        if (img != null) proyeccionWindow.MostrarImagenTexto(img);
                    }
                }
            }
            else
            {
                // Desactivar proyección
                _isProjecting = false;
                btn.SetCurrentValue(ContentProperty, "PROYECTAR ON/OFF (OFF)");
                proyeccionWindow?.Hide();
            }
        }

        private void BtnTextoDelAnio_Click(object sender, RoutedEventArgs e)
        {
            // Intentar mostrar la imagen configurada en Ajustes
            if (!string.IsNullOrWhiteSpace(Settings.Default.ImagenTextoAnio) && File.Exists(Settings.Default.ImagenTextoAnio))
            {
                var img = LoadBitmapFromFile(Settings.Default.ImagenTextoAnio);
                if (img != null)
                {
                    // Usar helper que detiene vídeo y proyecta la imagen
                    DisplayImageAndStopVideo(img, showInProjection: true);
                    return;
                }
            }

            MessageBox.Show("No hay imagen de 'Texto del año' configurada o no se encontró el archivo.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnAsociarMedia_Click(object sender, RoutedEventArgs e)
        {
            // Ahora permite seleccionar videos o imágenes; agrega su registro en secciones apropiadas.
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Videos e Imágenes (*.mp4;*.mkv;*.wmv;*.avi;*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.mp4;*.mkv;*.wmv;*.avi;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            var listaVideos = FindControl<ListBox>("ListaVideos"); // <-- Añadir esta línea para definir listaVideos

            foreach (string ruta in dlg.FileNames)
            {
                string ext = Path.GetExtension(ruta).ToLowerInvariant();
                if (IsImageExtension(ext))
                {
                    var img = LoadBitmapFromFile(ruta);
                    if (img != null)
                    {
                        _userImages.Add(img);
                        AgruparImagenesPorSeccion();
                        MostrarImagenesEnPanelDinamico();

                        // Usar helper para previsualizar y detener cualquier vídeo activo
                        DisplayImageAndStopVideo(img, showInProjection: true);

                        FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, $"Imagen cargada: {Path.GetFileName(ruta)}");
                    }
                }
                else
                {
                    // tratar como video
                    var vi = new VideoItem { FilePath = ruta, FileName = Path.GetFileName(ruta) };

                    // Generar thumbnail (icono asociado del archivo como fallback rápido)
                    vi.Thumbnail = GetFileIconAsBitmap(ruta) ?? CreatePlaceholderThumbnail();

                    _videos.Add(vi);

                    // Agregar entrada al programa semanal (ListaPrograma)
                    FindControl<ListBox>("ListaPrograma")?.Items.Add(new TextBlock { Text = $"Video: {vi.FileName}", Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });

                    // Intentar mostrar info/previsualización en UI si hay control para lista de videos
                    if (listaVideos != null)
                    {
                        // Si no hay ItemTemplate, crear una sencilla con imagen y nombre
                        if (listaVideos.ItemTemplate == null)
                        {
                            var stackFactory = new FrameworkElementFactory(typeof(StackPanel));
                            stackFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

                            var imgFactory = new FrameworkElementFactory(typeof(Image));
                            imgFactory.SetValue(FrameworkElement.WidthProperty, 48.0);
                            imgFactory.SetValue(FrameworkElement.HeightProperty, 48.0);
                            imgFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
                            imgFactory.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding("Thumbnail"));
                            stackFactory.AppendChild(imgFactory);

                            var txtFactory = new FrameworkElementFactory(typeof(TextBlock));
                            txtFactory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
                            txtFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("FileName"));
                            stackFactory.AppendChild(txtFactory);

                            listaVideos.ItemTemplate = new DataTemplate { VisualTree = stackFactory };
                        }

                        // Asegurarse de que el handler de doble click esté suscrito sólo una vez
                        listaVideos.MouseDoubleClick -= ListaVideos_MouseDoubleClick;
                        listaVideos.MouseDoubleClick += ListaVideos_MouseDoubleClick;

                        listaVideos.Items.Add(vi);

                        // Generar thumbnail real en segundo plano (no bloqueante) y actualizar item cuando esté listo
                        Task.Run(() =>
                        {
                            try
                            {
                                var generated = GenerateVideoFrameThumbnail(ruta, 160, 90);
                                if (generated != null)
                                {
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        vi.Thumbnail = generated;
                                        // Forzar refresco del item UI
                                        var idx = listaVideos.Items.IndexOf(vi);
                                        if (idx >= 0)
                                        {
                                            // re-asignar item para forzar el binding a refrescar
                                            listaVideos.Items.RemoveAt(idx);
                                            listaVideos.Items.Insert(idx, vi);
                                        }
                                    });
                                }
                            }
                            catch { /* ignorar errores de generación */ }
                        });
                    }

                    FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, $"Video cargado: {vi.FileName}");

                    // NO reproducir automáticamente en la proyección.
                    // El video se reproducirá sólo cuando el usuario haga doble click en la lista de videos.
                }
            }

            // FORZAR actualización del tamaño de thumbnails si hay control (evitar CS8604)
            if (listaVideos != null)
            {
                UpdateWrapPanelItemSize(listaVideos);
                // Seleccionar automáticamente el primer vídeo añadido (muestra los controles multimedia)
                if (listaVideos.SelectedItem == null && _videos.Count > 0)
                {
                    listaVideos.SelectedItem = _videos[0];
                }
            }
        }

        // Añade estos métodos al final de la clase MainWindow (o integra en la sección correspondiente).

        // Handler del botón 'X' para eliminar el vídeo de la lista
        private void BtnDeleteVideo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.DataContext is not VideoItem vi) return;

            // Confirmación opcional
            var res = MessageBox.Show($"¿Eliminar el vídeo '{vi.FileName}' de la lista?", "Eliminar vídeo", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            // Eliminar de la colección observable de forma segura
            try
            {
                var toRemove = _videos.Where(v => string.Equals(v.FilePath, vi.FilePath, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var r in toRemove) _videos.Remove(r);
            }
            catch { /* ignorar */ }

            // Forzar recálculo del layout/responsive si existe control
            var listaVideos = FindControl<ListBox>("ListaVideos");
            if (listaVideos != null)
            {
                UpdateWrapPanelItemSize(listaVideos);
            }

            // Actualizar lista de programa (si se añadió una entrada) — buscar y borrar el TextBlock que coincide con el nombre
            var listaPrograma = FindControl<ListBox>("ListaPrograma");
            if (listaPrograma != null)
            {
                TextBlock? toRemoveTb = null;
                foreach (var item in listaPrograma.Items)
                {
                    if (item is TextBlock tb && tb.Text.Contains(vi.FileName))
                    {
                        toRemoveTb = tb;
                        break;
                    }
                }
                if (toRemoveTb != null) listaPrograma.Items.Remove(toRemoveTb);
            }
        }

        // Implementación de UpdateWrapPanelItemSize(ListBox) (reemplaza el stub que lanzó NotImplementedException)
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
            double itemHeight = Math.Floor(itemWidth * 90.0 / 160.0); // relación ancho:alto para miniaturas de vídeo

            try
            {
                wrap.ItemWidth = itemWidth;
                wrap.ItemHeight = itemHeight;
            }
            catch
            {
                // ignorar
            }
        }

        // -------------------- Helpers --------------------

        private BitmapImage? LoadBitmapFromFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var bitmap = new BitmapImage();
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = fs;
                        bitmap.EndInit();
                        bitmap.Freeze();
                    }
                    return bitmap;
                }
            }
            catch
            {
                // ignorar error en carga de imagen
            }
            return null;
        }

        // P/Invoke para obtener icono asociado sin depender de System.Drawing
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;

        private BitmapImage? GetFileIconAsBitmap(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                SHFILEINFO shfi;
                IntPtr res = SHGetFileInfo(path, 0, out shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
                if (shfi.hIcon == IntPtr.Zero) return null;

                try
                {
                    // Crear BitmapSource desde HICON
                    var bmpSource = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));

                    // Encoder a PNG en memoria para obtener BitmapImage (compatible con el resto del código)
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmpSource));
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
                finally
                {
                    // liberar HICON obtenido
                    DestroyIcon(shfi.hIcon);
                }
            }
            catch
            {
                // fallback nulo
            }
            return null;
        }

        private static bool IsImageExtension(string ext)
        {
            switch (ext)
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

        // Clase ligera para registrar videos precargados
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

        private BitmapImage CreatePlaceholderThumbnail(int width = 160, int height = 90)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
                var ft = new FormattedText("VIDEO",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    20, Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
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

        private void ListaVideos_MouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            var lista = sender as ListBox;
            if (lista?.SelectedItem is VideoItem vi)
            {
                FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, $"Reproduciendo video: {vi.FileName}");

                if (proyeccionWindow != null)
                {
                    try
                    {
                        // Preparar reproducción en la ventana de proyección
                        proyeccionWindow.MostrarVideo(vi.FilePath);

                        // Sincronizar volumen desde la UI (si existe)
                        var volSlider = FindControl<Slider>("SldVolume");
                        double vol = (volSlider?.Value ?? 75) / 100.0;
                        try { proyeccionWindow.SetVolume(vol); } catch { }

                        // Asegurar que el video empiece a reproducirse
                        try { proyeccionWindow.PlayVideo(); } catch { }

                        proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                        if (proyeccionWindow.WindowState == WindowState.Minimized)
                            proyeccionWindow.WindowState = WindowState.Normal;
                        proyeccionWindow.Show();
                        _isProjecting = true;

                        // Mostrar/activar controles multimedia en la UI principal
                        FindControl<FrameworkElement>("MediaControlsPanel")?.SetCurrentValue(FrameworkElement.VisibilityProperty, Visibility.Visible);

                        // Actualizar estado visual de botones/timeline
                        FindControl<Button>("BtnPlayPause")?.SetCurrentValue(ContentProperty, "⏸");
                        FindControl<TextBlock>("TxtCurrentTime")?.SetCurrentValue(TextBlock.TextProperty, "00:00:00");
                        FindControl<Slider>("SldTimeline")?.SetCurrentValue(RangeBase.ValueProperty, 0.0);

                        FindControl<Button>("BtnProyectarHDMI")?.SetCurrentValue(ContentProperty, "PROYECTAR ON/OFF (ON)");
                    }
                    catch
                    {
                        // ignorar errores de reproducción para no bloquear UI
                    }
                }
            }
        }

        private BitmapImage? GenerateVideoFrameThumbnail(string path, int width, int height)
        {
            try
            {
                BitmapImage? result = null;
                var mre = new AutoResetEvent(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var mp = new MediaPlayer();
                    mp.MediaOpened += (s, e) => mre.Set();
                    mp.Open(new Uri(path));
                    // esperar apertura breve
                    if (!mre.WaitOne(2000))
                    {
                        mp.Close();
                        return;
                    }

                    // seek a un pequeño offset para evitar frame en negro
                    mp.Position = TimeSpan.FromMilliseconds(300);
                    mp.Play();
                    System.Threading.Thread.Sleep(150);

                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        var vb = new VideoDrawing { Rect = new Rect(0, 0, width, height), Player = mp };
                        dc.DrawDrawing(vb);
                    }

                    var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    mp.Close();

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
                        result = bi;
                    }
                });

                return result;
            }
            catch
            {
                return null;
            }
        }

        private void BtnPreviewPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (proyeccionWindow == null) return;

            try
            {
                // Alternar reproducción en la ventana de proyección
                if (proyeccionWindow.IsPlayingVideo)
                {
                    proyeccionWindow.PauseVideo();
                    (sender as Button)!.SetCurrentValue(ContentProperty, "Play");
                }
                else
                {
                    proyeccionWindow.PlayVideo();
                    (sender as Button)!.SetCurrentValue(ContentProperty, "Pause");

                    // Asegurar que la ventana de proyección esté visible en el monitor seleccionado
                    proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                    if (proyeccionWindow.WindowState == WindowState.Minimized)
                        proyeccionWindow.WindowState = WindowState.Normal;
                    proyeccionWindow.Show();
                    _isProjecting = true;

                    FindControl<Button>("BtnProyectarHDMI")?.SetCurrentValue(ContentProperty, "PROYECTAR ON/OFF (ON)");
                }
            }
            catch
            {
                // ignorar errores de control
            }
        }

        private void BtnPreviewStop_Click(object sender, RoutedEventArgs e)
        {
            if (proyeccionWindow == null) return;

            try
            {
                proyeccionWindow.StopVideo();

                FindControl<Button>("BtnPreviewPlayPause")?.SetCurrentValue(ContentProperty, "Play");
            }
            catch
            {
                // ignorar errores
            }
        }

        private void PreviewImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (FindControl<Image>("PreviewImage") == null) return;

            double oldScale = _previewScale;
            if (e.Delta > 0) _previewScale = Math.Min(PreviewMaxScale, _previewScale + PreviewScaleStep);
            else _previewScale = Math.Max(PreviewMinScale, _previewScale - PreviewScaleStep);

            double scaleFactor = _previewScale / oldScale;

            var pos = e.GetPosition(FindControl<Image>("PreviewImage"));

            // Ajustar translate para mantener el punto bajo el cursor
            if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt)
            {
                ptt.X = (1 - scaleFactor) * (pos.X) + scaleFactor * ptt.X;
                ptt.Y = (1 - scaleFactor) * (pos.Y) + scaleFactor * ptt.Y;
            }

            if (this.FindName("PreviewScaleTransform") is ScaleTransform pst)
            {
                pst.ScaleX = _previewScale;
                pst.ScaleY = _previewScale;
            }

            // Limitar la traslación para que la imagen no salga del borde del control
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
            if (_isPanningPreview && e.LeftButton == MouseButtonState.Pressed)
            {
                var globalPos = e.GetPosition(this);
                var gdx = globalPos.X - _lastPreviewMousePos.X;
                var gdy = globalPos.Y - _lastPreviewMousePos.Y;

                if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt)
                {
                    ptt.X += gdx;
                    ptt.Y += gdy;
                }

                _lastPreviewMousePos = globalPos;

                ClampPreviewTranslation();
            }
        }

        private void PreviewImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningPreview = false;
                try { Mouse.Capture(null); } catch { }
            }
        }

        private void BtnPreviewReset_Click(object sender, RoutedEventArgs e)
        {
            _previewScale = 1.0;
            if (this.FindName("PreviewScaleTransform") is ScaleTransform pst) { pst.ScaleX = 1.0; pst.ScaleY = 1.0; }
            if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt) { ptt.X = 0; ptt.Y = 0; }

            // Asegurar límites (centrar)
            ClampPreviewTranslation();
        }

        // Nuevo helper: limita la traslación de la imagen de previsualización
        private void ClampPreviewTranslation()
        {
            // Si no existe preview, nada que hacer
            var preview = FindControl<Image>("PreviewImage");
            if (preview == null) return;

            // Si no hay zoom (escala 1.0 o menor) forzamos centrado y no permitimos pan
            if (_previewScale <= 1.0)
            {
                if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt)
                {
                    ptt.X = 0;
                    ptt.Y = 0;
                }
                return;
            }

            // Con zoom activado, permitimos pan libre (no forzamos clamp).
            // Nota: la ventana de proyección recibe sus transformaciones desde Monitor_MouseMove, Monitor_MouseWheel o los botones.
            // Si quieres limitar el pan solo ligeramente, podemos introducir límites más amplios en vez de permitir pan ilimitado.
        }

        // Añade/pega este bloque dentro de la clase `MainWindow` (una sola vez).
        // Implementa los handlers que faltaban y el helper ApplyClampMonitorTranslation.

        private void SldTimeline_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isTimelineDragging = true;
        }

        private void SldTimeline_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isTimelineDragging = false;
            // Si se usa seek en proyeccion, aquí se llamaría al método correspondiente.
        }

        private void SldTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isTimelineDragging)
            {
                FindControl<TextBlock>("TxtCurrentTime")?.SetCurrentValue(TextBlock.TextProperty, $"{Math.Round(Math.Clamp(e.NewValue, 0.0, 1.0) * 100.0)}%");
            }
        }

        // Monitor (imagen grande) - zoom / pan
        private void Monitor_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var monitor = FindControl<Image>("MonitorDeSalida");
            if (monitor == null) return;

            double oldScale = _monitorScale;
            if (e.Delta > 0) _monitorScale = Math.Min(PreviewMaxScale, _monitorScale + PreviewScaleStep);
            else _monitorScale = Math.Max(PreviewMinScale, _monitorScale - PreviewScaleStep);

            double scaleFactor = (oldScale <= 0) ? 1.0 : _monitorScale / oldScale;
            var pos = e.GetPosition(monitor);

            if (this.FindName("MonitorTranslateTransform") is TranslateTransform mt)
            {
                mt.X = (1 - scaleFactor) * (pos.X) + scaleFactor * mt.X;
                mt.Y = (1 - scaleFactor) * (pos.Y) + scaleFactor * mt.Y;
            }

            if (this.FindName("MonitorScaleTransform") is ScaleTransform mst)
            {
                mst.ScaleX = _monitorScale;
                mst.ScaleY = _monitorScale;
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

            if (this.FindName("MonitorTranslateTransform") is TranslateTransform mt)
            {
                mt.X += dx;
                mt.Y += dy;
            }

            _lastMonitorMousePos = pos;

            ApplyClampMonitorTranslation();

            try { proyeccionWindow?.UpdateImageTransform(_monitorScale, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.X ?? 0, (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.Y ?? 0); } catch { }
        }

        private void Monitor_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningMonitor = false;
                try { Mouse.Capture(null); } catch { }
            }
        }

        // Implementación única y sin duplicados.
        // Mantiene el comportamiento pedido: centrar cuando scale <= 1, permitir pan libre cuando está ampliada.
        private void ApplyClampMonitorTranslation()
        {
            var monitor = FindControl<Image>("MonitorDeSalida");
            if (monitor == null) return;

            // Si no hay zoom (escala <= 1) centramos y evitamos desplazamiento
            if (_monitorScale <= 1.0)
            {
                if (this.FindName("MonitorTranslateTransform") is TranslateTransform mtZero)
                {
                    mtZero.X = 0;
                    mtZero.Y = 0;
                }
                return;
            }

            // Con zoom activado, permitimos pan libre (sin forzar clamp).
            // Si en el futuro quieres limitar el pan, añade lógica aquí.
        }

        // ListaVideos selection changed
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

        // ListaPrograma placeholders
        private void ListaPrograma_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Opcional: abrir edición del item o asociar media
        }

        private void ListaPrograma_Drop(object sender, DragEventArgs e)
        {
            // Opcional: manejar arrastrar/soltar de archivos al programa
        }

        // Controles de reproducción básicos en la UI principal
        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            proyeccionWindow?.StopVideo();
            FindControl<Button>("BtnPlayPause")?.SetCurrentValue(ContentProperty, "⏵");
            FindControl<TextBlock>("TxtCurrentTime")?.SetCurrentValue(TextBlock.TextProperty, "00:00:00");
        }

        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            _monitorScale = 1.0;
            (this.FindName("MonitorScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleXProperty, 1.0);
            (this.FindName("MonitorScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleYProperty, 1.0);
            if (this.FindName("MonitorTranslateTransform") is TranslateTransform tt) { tt.X = 0; tt.Y = 0; }
            // antes: ClampMonitorTranslation();
            ApplyClampMonitorTranslation();
            try { proyeccionWindow?.UpdateImageTransform(1.0, 0, 0); } catch { }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            var lista = FindControl<ListBox>("ListaVideos");
            if (lista == null || lista.Items.Count == 0) return;
            int idx = Math.Max(0, (lista.SelectedIndex < 0 ? 0 : lista.SelectedIndex) - 1);
            lista.SelectedIndex = idx;
            lista.ScrollIntoView(lista.SelectedItem);
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = sender as Button ?? FindControl<Button>("BtnPlayPause");
                if (proyeccionWindow != null && proyeccionWindow.IsPlayingVideo)
                {
                    proyeccionWindow.PauseVideo();
                    btn?.SetCurrentValue(ContentProperty, "⏵");
                }
                else
                {
                    proyeccionWindow?.PlayVideo();
                    btn?.SetCurrentValue(ContentProperty, "⏸");
                }
            }
            catch { }
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

        // Añade este método dentro de la clase `MainWindow` (una sola vez).
        // Implementa la acción de mostrar una imagen en preview/monitor y detener cualquier vídeo activo.
        private void DisplayImageAndStopVideo(BitmapImage img, bool showInProjection = false)
        {
            if (img == null) return;

            // Actualizar preview y monitor (si existen)
            FindControl<Image>("PreviewImage")?.SetCurrentValue(Image.SourceProperty, img);
            FindControl<Image>("MonitorDeSalida")?.SetCurrentValue(Image.SourceProperty, img);

            // Detener cualquier reproducción en la ventana de proyección
            try { proyeccionWindow?.StopVideo(); } catch { }

            // Ocultar panel de controles multimedia en la UI principal (si existe)
            FindControl<FrameworkElement>("MediaControlsPanel")?.SetCurrentValue(FrameworkElement.VisibilityProperty, Visibility.Collapsed);

            // Reset transforms de preview para evitar que quede zoom/pan previo
            _previewScale = 1.0;
            (this.FindName("PreviewScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleXProperty, 1.0);
            (this.FindName("PreviewScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleYProperty, 1.0);
            if (this.FindName("PreviewTranslateTransform") is TranslateTransform ptt) { ptt.X = 0; ptt.Y = 0; }
            try { ClampPreviewTranslation(); } catch { }

            // Reset transforms del monitor local
            _monitorScale = 1.0;
            (this.FindName("MonitorScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleXProperty, 1.0);
            (this.FindName("MonitorScaleTransform") as ScaleTransform)?.SetCurrentValue(ScaleTransform.ScaleYProperty, 1.0);
            if (this.FindName("MonitorTranslateTransform") is TranslateTransform mtt) { mtt.X = 0; mtt.Y = 0; }
            try { ClampMonitorTranslation(); } catch { }

            // Mostrar en la ventana de proyección si se solicita
            if (showInProjection)
            {
                try
                {       
                    proyeccionWindow?.MostrarImagenTexto(img);
                    proyeccionWindow?.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                    if (proyeccionWindow != null)
                    {
                        if (proyeccionWindow.WindowState == WindowState.Minimized)
                        {
                            proyeccionWindow.WindowState = WindowState.Normal;
                        }
                        proyeccionWindow.Show();
                        _isProjecting = true;
                        FindControl<Button>("BtnProyectarHDMI")?.SetCurrentValue(ContentProperty, "PROYECTAR ON/OFF (ON)");
                    }
                }
                catch { }
            }
        }

        // Reusa PreviewScaleStep para cambio de zoom
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("MonitorScaleTransform") is ScaleTransform mst)
            {
                _monitorScale = Math.Min(PreviewMaxScale, _monitorScale + PreviewScaleStep);
                mst.SetCurrentValue(ScaleTransform.ScaleXProperty, _monitorScale);
                mst.SetCurrentValue(ScaleTransform.ScaleYProperty, _monitorScale);

                // antes: ClampMonitorTranslation();
                ApplyClampMonitorTranslation();
                var tt = this.FindName("MonitorTranslateTransform") as TranslateTransform;
                try { proyeccionWindow?.UpdateImageTransform(_monitorScale, tt?.X ?? 0, tt?.Y ?? 0); } catch { }
            }
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("MonitorScaleTransform") is ScaleTransform mst)
            {
                _monitorScale = Math.Max(PreviewMinScale, _monitorScale - PreviewScaleStep);
                mst.SetCurrentValue(ScaleTransform.ScaleXProperty, _monitorScale);
                mst.SetCurrentValue(ScaleTransform.ScaleYProperty, _monitorScale);

                // antes: ClampMonitorTranslation();
                ApplyClampMonitorTranslation();
                var tt = this.FindName("MonitorTranslateTransform") as TranslateTransform;
                try { proyeccionWindow?.UpdateImageTransform(_monitorScale, tt?.X ?? 0, tt?.Y ?? 0); } catch { }
            }
        }

        private void PanMonitor(double dx, double dy)
        {
            if (this.FindName("MonitorTranslateTransform") is TranslateTransform mt)
            {
                mt.X += dx;
                mt.Y += dy;

                // antes: ClampMonitorTranslation();
                ApplyClampMonitorTranslation();

                try { proyeccionWindow?.UpdateImageTransform(_monitorScale, mt.X, mt.Y); } catch { }
            }
        }

        // Agrega estos handlers dentro de la clase `MainWindow` (por ejemplo justo arriba o debajo de `PanMonitor`).
        private void BtnPanLeft_Click(object sender, RoutedEventArgs e) => PanMonitor(-MonitorPanStep, 0);
        private void BtnPanRight_Click(object sender, RoutedEventArgs e) => PanMonitor(MonitorPanStep, 0);
        private void BtnPanUp_Click(object sender, RoutedEventArgs e) => PanMonitor(0, -MonitorPanStep);
        private void BtnPanDown_Click(object sender, RoutedEventArgs e) => PanMonitor(0, MonitorPanStep);

        // Añadir este wrapper dentro de la clase `MainWindow` (por ejemplo justo después de `ApplyClampMonitorTranslation`).
        private void ClampMonitorTranslation() => ApplyClampMonitorTranslation();
    }
}

