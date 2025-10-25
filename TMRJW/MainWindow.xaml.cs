using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TMRJW.Properties;
using VersOne.Epub;
using System.Windows.Input; // añadido

namespace TMRJW
{
    public partial class MainWindow : Window
    {
        private ProyeccionWindow? proyeccionWindow;
        private bool _isProjecting = false;

        private List<BitmapImage> _epubImages = new List<BitmapImage>();
        private List<GrupoImagenes> _gruposImagenes = new List<GrupoImagenes>();

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
            _gruposImagenes.Clear();

            _gruposImagenes.Add(new GrupoImagenes
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
                _gruposImagenes.Add(new GrupoImagenes
                {
                    TituloPestana = $"Punto {groupNumber}",
                    Imagenes = _epubImages.Skip(imageIndex).Take(imagesPerGroup).ToList()
                });

                imageIndex += imagesPerGroup;
                groupNumber++;
            }
        }

        private void MostrarImagenesEnPanelDinamico()
        {
            var tabControl = FindControl<TabControl>("TabControlImagenes");
            if (tabControl == null) return;

            tabControl.Items.Clear();

            // Intentar obtener el DataTemplate definido en XAML; si no existe, crear uno en tiempo de ejecución.
            DataTemplate itemTemplate = this.TryFindResource("ImageItemDataTemplate") as DataTemplate;
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
            // Abrir diálogo para seleccionar video (mp4, mkv, etc.)
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Videos (*.mp4;*.mkv;*.wmv;*.avi)|*.mp4;*.mkv;*.wmv;*.avi",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            string ruta = dlg.FileName;
            if (proyeccionWindow != null)
            {
                try
                {
                    proyeccionWindow.MostrarVideo(ruta);
                    // Mostrar también en el monitor pequeño si desea previsualizar
                    var txtInfo = FindControl<TextBlock>("TxtInfoMedia");
                    if (txtInfo != null) txtInfo.Text = $"Video cargado: {Path.GetFileName(ruta)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"No se pudo cargar el video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}
