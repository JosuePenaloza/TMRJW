using System.Windows;
using TMRJW.Properties;
using System.Windows.Media.Imaging;
using System.IO;
using System.Windows.Controls; // Para TextBlock y ListBox items
using System.Windows.Input; // Necesario para los stubs (MouseDoubleClick)
using VersOne.Epub;

namespace TMRJW
{
    /// <summary>
    /// Lógica de interacción para MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Variable para controlar la ventana de proyección (la segunda pantalla)
        private ProyeccionWindow proyeccionWindow = null;
        private bool _isProjecting = false; // Estado maestro de la proyección

        // 🌟 AGREGAR: Instancia del WebScraper 🌟
        private WebScraper _scraper = new WebScraper();

        public MainWindow()
        {
            InitializeComponent();

            // Inicializa la ventana de proyección al inicio (pero oculta)
            proyeccionWindow = new ProyeccionWindow();

            // 🌟 Paso 3 (Monitor): Coloca la ventana de proyección en el monitor seleccionado al inicio (aunque esté oculta)
            proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);

            // 🌟 LLAMADA INICIAL PARA CARGAR EL PROGRAMA DE LA SEMANA ACTUAL 🌟
            // Usamos un ejemplo: la semana 41 de 2025.
            CargarProgramaSemana(2025, 41);
        }

        // EN MainWindow.xaml.cs

        // 🌟 NUEVO MÉTODO CENTRAL: Intenta cargar de la Web, si falla, usa Offline 🌟
        private async void CargarProgramaSemana(int anio, int semana)
        {
            // Opcional: Deshabilita los botones mientras carga
            // BtnCargarArchivo.IsEnabled = false;

            TxtInfoMedia.Text = $"Cargando programa para {anio}/{semana}...";

            // 1. Intentar obtener el contenido en línea
            string contenidoWeb = await _scraper.ObtenerContenidoSemanal(anio, semana);

            if (contenidoWeb.StartsWith("❌") || contenidoWeb.StartsWith("⚠️"))
            {
                // 2. Si falla (Sin Internet o error de parsing), usar la opción offline (EPUB)
                System.Windows.MessageBox.Show(
                    "Fallo al obtener el programa web. Active el modo offline (EPUB) manualmente.",
                    "Advertencia de Conexión / Parsing",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning
                );

                // 💡 Por ahora, solo actualizamos el mensaje de la lista con el error
                LlenarListaProgramaDesdeError(contenidoWeb);
                TxtInfoMedia.Text = "Programa Offline (Pendiente de cargar EPUB)";
            }
            else
            {
                // 3. Mostrar el contenido web y llenar la lista
                LlenarListaProgramaDesdeTexto(contenidoWeb);
                TxtInfoMedia.Text = $"Programa cargado (Web) para {anio}/{semana}";

                // 🌟 AJUSTE CRÍTICO APLICADO AQUÍ 🌟
                if (_isProjecting && proyeccionWindow != null)
                {
                    // Llama al nuevo método para enviar el texto a la ventana de proyección
                    proyeccionWindow.MostrarTextoPrograma(contenidoWeb);
                }
            }

            // BtnCargarArchivo.IsEnabled = true; // Habilita de nuevo
        }


        // 1. Método para abrir la Ventana de Ajustes (BtnAjustes)
        private void BtnAjustes_Click(object sender, RoutedEventArgs e)
        {
            // Instancia y muestra la ventana de ajustes
            AjustesWindow ajustes = new AjustesWindow();
            ajustes.ShowDialog();

            // Si el usuario cambia el monitor de salida, actualizamos la posición si la proyección está ON.
            if (_isProjecting)
            {
                proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);
            }
        }

        // 2. Método para el botón PROYECTAR TEXTO DEL AÑO
        private void BtnTextoDelAnio_Click(object sender, RoutedEventArgs e)
        {
            // VERIFICACIÓN: Sólo proyectar si el interruptor maestro está en ON
            if (!_isProjecting)
            {
                System.Windows.MessageBox.Show("Active el interruptor 'PROYECTAR ON/OFF' para iniciar la transmisión.", "Transmisión Inactiva", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string rutaImagen = Settings.Default.ImagenTextoAnio;

            if (string.IsNullOrEmpty(rutaImagen) || !File.Exists(rutaImagen))
            {
                System.Windows.MessageBox.Show("No se ha configurado la ruta de la imagen del Texto del Año en Ajustes, o el archivo no existe.", "Error de Configuración", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                BitmapImage bitmap = new BitmapImage(new Uri(rutaImagen));
                MonitorDeSalida.Source = bitmap; // VISTA PREVIA
                proyeccionWindow.MostrarImagenTexto(bitmap);
                TxtInfoMedia.Text = "Reproduciendo: Texto del Año";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error al cargar la imagen: {ex.Message}", "Error de Imagen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 3. Método para el interruptor ON/OFF de la proyección (Control Maestro)
        private void BtnProyectarHDMI_Click(object sender, RoutedEventArgs e)
        {
            _isProjecting = !_isProjecting;

            if (_isProjecting)
            {
                // Paso 3 (Monitor): ASEGURAR que la ventana esté en el monitor correcto antes de mostrarla
                proyeccionWindow.ActualizarMonitor(Settings.Default.MonitorSalidaIndex);

                proyeccionWindow.Show();
                BtnProyectarHDMI.Content = "PROYECTAR ON/OFF (ON)";
                BtnProyectarHDMI.Background = System.Windows.Media.Brushes.Green;

                // Lógica de carga automática del Texto del Año (como antes)
                string rutaImagen = Settings.Default.ImagenTextoAnio;
                BitmapImage bitmap = null;

                if (!string.IsNullOrEmpty(rutaImagen) && File.Exists(rutaImagen))
                {
                    try
                    {
                        bitmap = new BitmapImage(new Uri(rutaImagen));
                        MonitorDeSalida.Source = bitmap;
                        proyeccionWindow.MostrarImagenTexto(bitmap);
                        TxtInfoMedia.Text = "Reproduciendo: Texto del Año";
                    }
                    catch (Exception)
                    {
                        MonitorDeSalida.Source = null;
                        proyeccionWindow.MostrarImagenTexto(null);
                        TxtInfoMedia.Text = "Reproduciendo: Ninguno";
                    }
                }
                else
                {
                    MonitorDeSalida.Source = null;
                    proyeccionWindow.MostrarImagenTexto(null);
                    TxtInfoMedia.Text = "Reproduciendo: Ninguno";
                }
            }
            else
            {
                proyeccionWindow.Hide();
                BtnProyectarHDMI.Content = "PROYECTAR ON/OFF (OFF)";
                BtnProyectarHDMI.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x8C, 0x00)); // Naranja

                MonitorDeSalida.Source = null;
                proyeccionWindow.MostrarImagenTexto(null);
                TxtInfoMedia.Text = "Reproduciendo: Ninguno";
            }
        }

        // 🌟 PASO 4 IMPLEMENTADO 🌟

        private void BtnCargarArchivo_Click(object sender, RoutedEventArgs e)
        {
            // Solución CS0104: Usar el nombre completo para evitar ambigüedad
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Archivos EPUB/HTML (*.epub;*.html;*.htm)|*.epub;*.html;*.htm"
            }; // Solución IDE0017: Inicialización simplificada

            if (openFileDialog.ShowDialog() == true)
            {
                string rutaArchivo = openFileDialog.FileName;

                try
                {
                    if (Path.GetExtension(rutaArchivo).Equals(".epub", StringComparison.OrdinalIgnoreCase))
                    {
                        // Procesar EPUB
                        var epubBook = VersOne.Epub.EpubReader.ReadBook(rutaArchivo);

                        // Extraer imágenes
                        var imagenes = epubBook.Content.Images.Local;
                        int contador = 1;
                        foreach (var img in imagenes)
                        {
                            // Guardar cada imagen en disco temporalmente (puedes cambiar la ruta)
                            string tempPath = Path.Combine(Path.GetTempPath(), $"epub_img_{contador}.jpg");
                            File.WriteAllBytes(tempPath, img.Content);

                            // Mostrar la primera imagen como ejemplo
                            if (contador == 1)
                            {
                                BitmapImage bitmap = new BitmapImage(new Uri(tempPath));
                                MonitorDeSalida.Source = bitmap;
                                proyeccionWindow.MostrarImagenTexto(bitmap);
                                TxtInfoMedia.Text = $"Imagen EPUB mostrada: {img.Key}";
                            }
                            contador++;
                        }

                        // Extraer archivos multimedia (audio)
                        var audioFiles = epubBook.Content.Audio.Local
                            .Where(static f =>
                                f.ContentType == EpubContentType.AUDIO_MP3 ||
                                f.ContentType == EpubContentType.AUDIO_MP4 ||
                                f.ContentType == EpubContentType.AUDIO_OGG);
                        int audioCount = audioFiles.Count();

                        // Si tu versión de VersOne.Epub no tiene Video, solo procesa audio.
                        // Si tienes Video, puedes agregarlo de forma similar:
                        // var videoFiles = epubBook.Content.Video.Local.Values
                        //     .Where(f => f.ContentType.StartsWith("video"));
                        // int videoCount = videoFiles.Count();

                        // Mostrar resumen en la lista
                        LlenarListaProgramaDesdeTexto($"EPUB cargado: {epubBook.Title}\nImágenes encontradas: {imagenes.Count()}\nAudio: {audioCount}");
                        System.Windows.MessageBox.Show($"EPUB '{epubBook.Title}' cargado exitosamente.", "Carga Completa", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        // Procesar HTML/HTM como antes
                        string fileContent = File.ReadAllText(rutaArchivo);
                        LlenarListaProgramaDesdeTexto("Contenido cargado desde archivo:\n" + fileContent.Substring(0, Math.Min(500, fileContent.Length)) + "...");
                        HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
                        doc.LoadHtml(fileContent);
                        string titulo = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? "Guía Semanal (Offline)";
                        System.Windows.MessageBox.Show($"Guía Semanal '{titulo}' cargada exitosamente. (Modo Offline - Archivo)", "Carga Completa", MessageBoxButton.OK, MessageBoxImage.Information);
                        TxtInfoMedia.Text = $"Programa cargado (Offline) desde archivo: {Path.GetFileName(rutaArchivo)}";
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error al cargar o leer el archivo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        // 🌟 NUEVO MÉTODO AUXILIAR 1: Llenado de programa a partir del texto procesado 🌟
        private void LlenarListaProgramaDesdeTexto(string textoPrograma)
        {
            ListaPrograma.Items.Clear();

            // Dividir el texto en líneas para simular ítems de la lista
            string[] lineas = textoPrograma.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // Simulación simple de elementos
            foreach (var linea in lineas)
            {
                // Si la línea es muy corta (ej. un separador), ignorar o aplicar un formato
                if (linea.Length < 5 || linea.Contains("="))
                {
                    continue;
                }

                // Asignar un formato simple basado en el contenido
                if (linea.Contains("Cántico") || linea.Contains("Oración") || linea.Contains("Video") || linea.Contains("Tesoros"))
                {
                    ListaPrograma.Items.Add(new TextBlock { Text = linea.Trim(), Foreground = System.Windows.Media.Brushes.Gold, FontWeight = FontWeights.Bold });
                }
                else
                {
                    ListaPrograma.Items.Add(new TextBlock { Text = linea.Trim(), Margin = new Thickness(5, 0, 0, 0) });
                }
            }
        }

        // 🌟 NUEVO MÉTODO AUXILIAR 2: Llenado de programa con el mensaje de error 🌟
        private void LlenarListaProgramaDesdeError(string mensajeError)
        {
            ListaPrograma.Items.Clear();
            ListaPrograma.Items.Add(new TextBlock { Text = mensajeError, Foreground = System.Windows.Media.Brushes.Red, FontWeight = FontWeights.Bold });
            ListaPrograma.Items.Add(new TextBlock { Text = "Por favor, verifica tu conexión a Internet o usa el botón 'Cargar Archivo' para el modo Offline.", Margin = new Thickness(5, 5, 0, 0) });
        }


        // --- Métodos pendientes (stubs) ---
        private void BtnSemanaAnterior_Click(object sender, RoutedEventArgs e) { }
        private void CboSemanas_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
        private void BtnSemanaSiguiente_Click(object sender, RoutedEventArgs e) { }
        private void BtnAsociarMedia_Click(object sender, RoutedEventArgs e) { }
        private void ListaPrograma_Drop(object sender, System.Windows.DragEventArgs e) { }
        private void ListaPrograma_MouseDoubleClick(object sender, MouseButtonEventArgs e) { }
    }
}