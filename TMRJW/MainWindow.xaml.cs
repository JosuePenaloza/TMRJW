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

namespace TMRJW
{
    public partial class MainWindow : Window
    {
        private ProyeccionWindow? proyeccionWindow;
        private bool _isProjecting = false;

        private List<BitmapImage> _epubImages = new List<BitmapImage>();
        private List<BitmapImage> _userImages = new List<BitmapImage>(); // imágenes cargadas por el usuario
        private List<GrupoImagenes> _gruposImagenes = new List<GrupoImagenes>();

        private List<VideoItem> _videos = new List<VideoItem>(); // videos precargados

        private double _previewScale = 1.0;
        private const double PreviewMinScale = 0.2;
        private const double PreviewMaxScale = 5.0;
        private const double PreviewScaleStep = 0.1;
        private bool _isPanningPreview = false;
        private Point _lastPreviewMousePos;

        private double _monitorScale = 1.0;
        private bool _isPanningMonitor = false;
        private Point _lastMonitorMousePos;

        public MainWindow()
        {
            InitializeComponent();
            proyeccionWindow = new ProyeccionWindow();
            // No mostrar inicialmente; posicionarlo en el monitor configurado cuando se active
            proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);

            // Asegurar que al cerrar la ventana principal cerramos la ventana de proyección
            this.Closing += MainWindow_Closing;
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
                        var monitor = FindControl<Image>("MonitorDeSalida");
                        if (monitor != null) monitor.Source = primeraImagen;

                        // Mostrar en la ventana de proyección aunque _isProjecting sea false,
                        // el usuario puede activar proyección con el botón ON/OFF.
                        proyeccionWindow?.MostrarImagenTexto(primeraImagen);

                        var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
                        if (txtInfo != null) txtInfo.Text = $"Primera imagen EPUB mostrada. Total de imágenes: {_epubImages.Count}";
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
                    var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
                    if (txtInfo != null) txtInfo.Text = $"Programa cargado (Offline) desde archivo: {Path.GetFileName(rutaArchivo)}";
                    MessageBox.Show($"Guía Semanal '{Path.GetFileName(rutaArchivo)}' cargada exitosamente. (Modo Offline)", "Carga Completa", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // Mostrar información completa del error para depuración
                MessageBox.Show($"Error al cargar o leer el archivo:\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
                if (txtInfo != null) txtInfo.Text = $"Error: {ex.Message}";
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

            // Conservar encabezado seleccionado para evitar "reordenamiento visual" al reconstruir.
            string? selectedHeader = (tabControl.SelectedItem as TabItem)?.Header?.ToString();

            tabControl.Items.Clear();

            // Intentar obtener el DataTemplate definido en XAML; si no existe, crear uno en tiempo de ejecución.
            DataTemplate? itemTemplate = this.TryFindResource("ImageItemDataTemplate") as DataTemplate;
            if (itemTemplate == null)
            {
                var factoryImg = new FrameworkElementFactory(typeof(Image));
                factoryImg.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding()); // binding directo al objeto (BitmapImage)
                factoryImg.SetValue(FrameworkElement.WidthProperty, 80.0);
                factoryImg.SetValue(FrameworkElement.HeightProperty, 80.0);
                factoryImg.SetValue(Image.StretchProperty, Stretch.UniformToFill);
                factoryImg.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
                factoryImg.SetValue(Image.CursorProperty, System.Windows.Input.Cursors.Hand);

                itemTemplate = new DataTemplate
                {
                    VisualTree = factoryImg
                };
            }

            const int pageSize = 20; // miniaturas por página

            for (int g = 0; g < _gruposImagenes.Count; g++)
            {
                var grupo = _gruposImagenes[g];

                // Asegurarse de que las miniaturas estén ordenadas descendente por resolución
                var orderedImages = grupo.Imagenes
                    .OrderByDescending(b => (long)b.PixelWidth * b.PixelHeight)
                    .ToList();

                // Paginación: calcular número de páginas
                int totalPages = Math.Max(1, (int)Math.Ceiling((double)orderedImages.Count / pageSize));
                int currentPage = 0; // captura local para cada grupo

                // ListBox que mostrará la página actual
                var listBox = new ListBox
                {
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    ItemTemplate = itemTemplate,
                    SelectionMode = SelectionMode.Single
                };

                // ItemsPanel: apilar verticalmente (una columna, desplazamiento arriba/abajo)
                var stackPanelTemplate = new ItemsPanelTemplate();
                var factoryPanel = new FrameworkElementFactory(typeof(StackPanel));
                factoryPanel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
                stackPanelTemplate.VisualTree = factoryPanel;
                listBox.ItemsPanel = stackPanelTemplate;

                // Método local para actualizar el ItemsSource según la página actual
                void UpdatePage()
                {
                    var pageItems = orderedImages.Skip(currentPage * pageSize).Take(pageSize).ToList();
                    listBox.ItemsSource = pageItems;
                }

                // Inicializar página 0
                UpdatePage();

                // SINGLE-CLICK -> vista previa (no proyectar)
                listBox.SelectionChanged += ListBoxImagenes_PreviewSelectionChanged;

                // DOBLE-CLICK -> proyectar (ya implementado)
                listBox.MouseDoubleClick += ListBoxImagenes_MouseDoubleClick;

                // Controles de paginación (Prev / PageInfo / Next)
                var prevBtn = new Button { Content = "◀", Width = 30, Height = 26, Margin = new Thickness(4) };
                var nextBtn = new Button { Content = "▶", Width = 30, Height = 26, Margin = new Thickness(4) };
                var pageInfo = new TextBlock
                {
                    Text = $"{currentPage + 1}/{totalPages}",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0),
                    Foreground = Brushes.Black
                };

                prevBtn.Click += (s, e) =>
                {
                    if (currentPage > 0)
                    {
                        currentPage--;
                        UpdatePage();
                        pageInfo.Text = $"{currentPage + 1}/{totalPages}";
                    }
                };

                nextBtn.Click += (s, e) =>
                {
                    if (currentPage < totalPages - 1)
                    {
                        currentPage++;
                        UpdatePage();
                        pageInfo.Text = $"{currentPage + 1}/{totalPages}";
                    }
                };

                // Construir contenido del tab: una Grid con ListBox (fila 0) y paginador (fila 1)
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // No envolver en ScrollViewer manual: dejar que el ListBox maneje el scroll vertical
                listBox.VerticalContentAlignment = VerticalAlignment.Top;
                Grid.SetRow(listBox, 0);
                grid.Children.Add(listBox);

                var pagerPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 6)
                };
                pagerPanel.Children.Add(prevBtn);
                pagerPanel.Children.Add(pageInfo);
                pagerPanel.Children.Add(nextBtn);
                Grid.SetRow(pagerPanel, 1);
                grid.Children.Add(pagerPanel);

                var tabItem = new TabItem
                {
                    Header = grupo.TituloPestana,
                    Foreground = Brushes.Black,
                    Padding = new Thickness(10, 5, 10, 5),
                    Content = grid
                };

                tabControl.Items.Add(tabItem);
            }

            // Restaurar la pestaña seleccionada por encabezado (si existe)
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

        // SINGLE-CLICK preview: muestra en PreviewImage sin proyectar
        private void ListBoxImagenes_PreviewSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is BitmapImage selectedImage)
            {
                var preview = FindControl<Image>("PreviewImage");
                if (preview != null)
                    preview.Source = selectedImage;

                var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
                if (txtInfo != null) txtInfo.Text = "Vista previa: imagen seleccionada (single-click)";
            }
        }

        // DOBLE-CLICK: proyectar en ventana externa (mantener)
        private void ListBoxImagenes_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var listBox = sender as ListBox;
            if (listBox?.SelectedItem is BitmapImage selectedImage)
            {
                var monitor = FindControl<Image>("MonitorDeSalida");
                if (monitor != null) monitor.Source = selectedImage;

                // Mostrar en la ventana de proyección (si existe)
                if (proyeccionWindow != null)
                {
                    proyeccionWindow.MostrarImagenTexto(selectedImage);

                    if (_isProjecting)
                    {
                        proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                        if (proyeccionWindow.WindowState == WindowState.Minimized)
                            proyeccionWindow.WindowState = WindowState.Normal;
                        proyeccionWindow.Show();
                    }
                }

                var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
                if (txtInfo != null) txtInfo.Text = "Reproduciendo: Imagen seleccionada del EPUB (doble click)";
            }
        }

        // SldVolume -> controla volumen del MediaElement en ProyeccionWindow
        private void SldVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (proyeccionWindow != null)
            {
                double vol = (SldVolume?.Value ?? 75) / 100.0;
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
                if (proyeccionWindow != null)
                {
                    proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                }
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
                btn.Content = "PROYECTAR ON/OFF (ON)";

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
                        if (!string.IsNullOrWhiteSpace(Settings.Default.ImagenTextoAnio))
                        {
                            var img = LoadBitmapFromFile(Settings.Default.ImagenTextoAnio);
                            if (img != null) proyeccionWindow.MostrarImagenTexto(img);
                        }
                    }
                }
            }
            else
            {
                // Desactivar proyección
                _isProjecting = false;
                btn.Content = "PROYECTAR ON/OFF (OFF)";
                if (proyeccionWindow != null)
                {
                    // Ocultar en lugar de cerrar para poder reutilizar la instancia
                    proyeccionWindow.Hide();
                }
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
                    var monitor = FindControl<Image>("MonitorDeSalida");
                    if (monitor != null) monitor.Source = img;

                    if (proyeccionWindow != null)
                    {
                        proyeccionWindow.MostrarImagenTexto(img);
                        // Asegurar que la ventana de proyección esté visible en el monitor seleccionado
                        proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                        proyeccionWindow.Show();
                        _isProjecting = true;

                        var btn = FindControl<Button>("BtnProyectarHDMI");
                        if (btn != null) btn.Content = "PROYECTAR ON/OFF (ON)";
                    }

                    return;
                }
            }

            MessageBox.Show("No hay imagen de 'Texto del año' configurada o no se encontró el archivo.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ListaPrograma_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // lógica opcional
        }

        private void ListaPrograma_Drop(object sender, DragEventArgs e)
        {
            // lógica opcional
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

                        // previsualizar en el monitor pequeño y en proyección
                        var monitor = FindControl<Image>("MonitorDeSalida");
                        if (monitor != null) monitor.Source = img;
                        proyeccionWindow?.MostrarImagenTexto(img);

                        var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
                        if (txtInfo != null) txtInfo.Text = $"Imagen cargada: {Path.GetFileName(ruta)}";
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
                    var lista = FindControl<ListBox>("ListaPrograma");
                    if (lista != null)
                    {
                        lista.Items.Add(new TextBlock { Text = $"Video: {vi.FileName}", Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });
                    }

                    // Intentar mostrar info/previsualización en UI si hay control para lista de videos
                    var listaVideos = FindControl<ListBox>("ListaVideos");
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

                    var txtInfo2 = FindControl<TextBlock>("TxtInfoMedia");
                    if (txtInfo2 != null) txtInfo2.Text = $"Video cargado: {vi.FileName}";

                    // NO reproducir automáticamente en la proyección.
                    // El video se reproducirá sólo cuando el usuario haga doble click en la lista de videos.
                }
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
        private class VideoItem
        {
            public string FilePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public BitmapImage? Thumbnail { get; set; } = null;
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
                var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
                if (txtInfo != null) txtInfo.Text = $"Reproduciendo video: {vi.FileName}";

                if (proyeccionWindow != null)
                {
                    try
                    {
                        proyeccionWindow.MostrarVideo(vi.FilePath);
                        proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                        if (proyeccionWindow.WindowState == WindowState.Minimized)
                            proyeccionWindow.WindowState = WindowState.Normal;
                        proyeccionWindow.Show();
                        _isProjecting = true;

                        var btn = FindControl<Button>("BtnProyectarHDMI");
                        if (btn != null) btn.Content = "PROYECTAR ON/OFF (ON)";
                    }
                    catch
                    {
                        // ignorar errores de reproducción
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
                    (sender as Button)!.Content = "Play";
                }
                else
                {
                    proyeccionWindow.PlayVideo();
                    (sender as Button)!.Content = "Pause";

                    // Asegurar que la ventana de proyección esté visible en el monitor seleccionado
                    proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
                    if (proyeccionWindow.WindowState == WindowState.Minimized)
                        proyeccionWindow.WindowState = WindowState.Normal;
                    proyeccionWindow.Show();
                    _isProjecting = true;

                    var btn = FindControl<Button>("BtnProyectarHDMI");
                    if (btn != null) btn.Content = "PROYECTAR ON/OFF (ON)";
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

                var playBtn = FindControl<Button>("BtnPreviewPlayPause");
                if (playBtn != null) playBtn.Content = "Play";
            }
            catch
            {
                // ignorar errores
            }
        }

        private void PreviewImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (PreviewImage == null) return;

            double oldScale = _previewScale;
            if (e.Delta > 0) _previewScale = Math.Min(PreviewMaxScale, _previewScale + PreviewScaleStep);
            else _previewScale = Math.Max(PreviewMinScale, _previewScale - PreviewScaleStep);

            double scaleFactor = _previewScale / oldScale;

            var pos = e.GetPosition(PreviewImage);

            // Ajustar translate para mantener el punto bajo el cursor
            PreviewTranslateTransform.X = (1 - scaleFactor) * (pos.X) + scaleFactor * PreviewTranslateTransform.X;
            PreviewTranslateTransform.Y = (1 - scaleFactor) * (pos.Y) + scaleFactor * PreviewTranslateTransform.Y;

            PreviewScaleTransform.ScaleX = _previewScale;
            PreviewScaleTransform.ScaleY = _previewScale;

            // NOTA: NO propagar al proyector — el zoom de previsualización es privado
            e.Handled = true;
        }

        private void PreviewImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningPreview = true;
                _lastPreviewMousePos = e.GetPosition(this);
                try { Mouse.Capture(PreviewImage); } catch { }
            }
        }

        private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanningPreview && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this);
                var dx = pos.X - _lastPreviewMousePos.X;
                var dy = pos.Y - _lastPreviewMousePos.Y;

                PreviewTranslateTransform.X += dx;
                PreviewTranslateTransform.Y += dy;

                _lastPreviewMousePos = pos;

                // NOTA: NO propagar al proyector — panning de previsualización es privado
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
            PreviewScaleTransform.ScaleX = 1.0;
            PreviewScaleTransform.ScaleY = 1.0;
            PreviewTranslateTransform.X = 0;
            PreviewTranslateTransform.Y = 0;

            // NOTA: NO propagar al proyector — reset de previsualización es privado
        }

        private void Monitor_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (MonitorDeSalida == null) return;

            double oldScale = _monitorScale;
            if (e.Delta > 0) _monitorScale = Math.Min(PreviewMaxScale, _monitorScale + PreviewScaleStep);
            else _monitorScale = Math.Max(PreviewMinScale, _monitorScale - PreviewScaleStep);

            double scaleFactor = _monitorScale / oldScale;
            var pos = e.GetPosition(MonitorDeSalida);

            MonitorTranslateTransform.X = (1 - scaleFactor) * (pos.X) + scaleFactor * MonitorTranslateTransform.X;
            MonitorTranslateTransform.Y = (1 - scaleFactor) * (pos.Y) + scaleFactor * MonitorTranslateTransform.Y;

            MonitorScaleTransform.ScaleX = _monitorScale;
            MonitorScaleTransform.ScaleY = _monitorScale;

            // Propagar la transformación a la ventana de proyección
            proyeccionWindow?.UpdateImageTransform(_monitorScale, MonitorTranslateTransform.X, MonitorTranslateTransform.Y);

            e.Handled = true;
        }

        private void Monitor_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningMonitor = true;
                _lastMonitorMousePos = e.GetPosition(this);
                try { Mouse.Capture(MonitorDeSalida); } catch { }
            }
        }

        private void Monitor_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanningMonitor && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this);
                var dx = pos.X - _lastMonitorMousePos.X;
                var dy = pos.Y - _lastMonitorMousePos.Y;

                MonitorTranslateTransform.X += dx;
                MonitorTranslateTransform.Y += dy;

                _lastMonitorMousePos = pos;

                // Propagar al proyector
                proyeccionWindow?.UpdateImageTransform(_monitorScale, MonitorTranslateTransform.X, MonitorTranslateTransform.Y);
            }
        }

        private void Monitor_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isPanningMonitor = false;
                try { Mouse.Capture(null); } catch { }
            }
        }
    }
}
