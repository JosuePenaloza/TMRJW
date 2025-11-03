using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TMRJW
{
    public partial class MainWindow : Window
    {
        // Asociar media (imágenes o vídeos) - abre diálogo, añade a listas y genera miniaturas en background.
        private async void BtnAsociarMedia_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Filter = "Videos e Imágenes (*.mp4;*.mkv;*.wmv;*.avi;*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.mp4;*.mkv;*.wmv;*.avi;*.jpg;*.jpeg;*.png;*.bmp;*.gif",
                    Multiselect = true
                };
                if (dlg.ShowDialog() != true) return;

                var listaVideos = FindControl<ListBox>("ListaVideos");
                var listaPrograma = FindControl<ListBox>("ListaPrograma");

                foreach (string ruta in dlg.FileNames)
                {
                    string ext = Path.GetExtension(ruta) ?? "";
                    if (IsImageExtension(ext))
                    {
                        var img = PlatformInterop.LoadBitmapFromFile(ruta);
                        if (img != null)
                        {
                            _userImages.Add(img);
                            AgruparImagenesPorSeccion();
                            MostrarImagenesEnPanelDinamico();
                            DisplayImageAndStopVideo(img, showInProjection: true);
                            FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, $"Imagen cargada: {Path.GetFileName(ruta)}");
                        }
                    }
                    else
                    {
                        var vi = new VideoItem { FilePath = ruta, FileName = Path.GetFileName(ruta) };
                        vi.Thumbnail = PlatformInterop.GetFileIconAsBitmap(ruta) ?? CreatePlaceholderThumbnail();
                        _videos.Add(vi);

                        // Añadir resumen al programa
                        listaPrograma?.Items.Add(new TextBlock { Text = $"Video: {vi.FileName}", Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });

                        if (listaVideos != null)
                        {
                            // Asegurar ItemTemplate y añadir item
                            if (listaVideos.ItemTemplate == null)
                            {
                                // Dejar como está: la UI principal puede usar DataTemplate declarada.
                            }

                            listaVideos.Items.Add(vi);

                            // Generar thumbnail de frame en background
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var generated = await GenerateVideoFrameThumbnailAsync(ruta, 160, 90);
                                    if (generated != null)
                                    {
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            vi.Thumbnail = generated;
                                            // Forzar refresh de item si es necesario
                                            var idx = listaVideos.Items.IndexOf(vi);
                                            if (idx >= 0)
                                            {
                                                listaVideos.Items.RemoveAt(idx);
                                                listaVideos.Items.Insert(idx, vi);
                                            }
                                        });
                                    }
                                }
                                catch { }
                            });
                        }

                        FindControl<TextBlock>("TxtInfoMedia")?.SetCurrentValue(TextBlock.TextProperty, $"Video cargado: {vi.FileName}");
                    }
                }

                if (listaVideos != null)
                {
                    UpdateWrapPanelItemSize(listaVideos);
                    if (listaVideos.SelectedItem == null && _videos.Count > 0) listaVideos.SelectedItem = _videos[0];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al asociar media: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Eliminar vídeo (botón en plantilla)
        private void BtnDeleteVideo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn) return;
                if (btn.DataContext is not VideoItem vi) return;

                var res = MessageBox.Show($"¿Eliminar el vídeo '{vi.FileName}' de la lista?", "Eliminar vídeo", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res != MessageBoxResult.Yes) return;

                var toRemove = _videos.Where(v => string.Equals(v.FilePath, vi.FilePath, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var r in toRemove) _videos.Remove(r);

                var listaVideos = FindControl<ListBox>("ListaVideos");
                if (listaVideos != null) UpdateWrapPanelItemSize(listaVideos);

                var listaPrograma = FindControl<ListBox>("ListaPrograma");
                if (listaPrograma != null)
                {
                    TextBlock? toRemoveTb = null;
                    foreach (var item in listaPrograma.Items)
                    {
                        if (item is TextBlock tb && tb.Text.Contains(vi.FileName)) { toRemoveTb = tb; break; }
                    }
                    if (toRemoveTb != null) listaPrograma.Items.Remove(toRemoveTb);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar vídeo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Mostrar imagen "Texto del año" en preview y proyección si existe la ruta en ajustes
        private void BtnTextoDelAnio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsHelper.Load();
                var ruta = settings.ImagenTextoAnio;
                if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta))
                {
                    var img = PlatformInterop.LoadBitmapFromFile(ruta);
                    if (img != null) { DisplayImageAndStopVideo(img, showInProjection: true); return; }
                }
                MessageBox.Show("No hay imagen de 'Texto del año' configurada o no se encontró el archivo.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al mostrar 'Texto del año': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Toggle proyección ON/OFF (abre/cierra ventana de proyección)
        private void BtnProyectarHDMI_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var btn = FindControl<Button>("BtnProyectarHDMI");
                if (btn == null) return;

                if (!_isProjecting)
                {
                    _isProjecting = true;
                    btn.SetCurrentValue(ContentProperty, "PROYECTAR ON/OFF (ON)");

                    OpenProyeccionOnSelectedMonitor();

                    // Mostrar la imagen seleccionada en pestañas si existe, sino la imagen de texto del año
                    var tabControl = FindControl<TabControl>("TabControlImagenes");
                    if (tabControl != null && tabControl.SelectedItem is TabItem ti &&
                        ti.Content is Grid grid && grid.Children.OfType<ScrollViewer>().FirstOrDefault() is ScrollViewer sview &&
                        sview.Content is ListBox lb && lb.SelectedItem is BitmapImage sel)
                    {
                        proyeccionWindow?.MostrarImagenTexto(sel);
                    }
                    else
                    {
                        var settings = SettingsHelper.Load();
                        var ruta = settings.ImagenTextoAnio;
                        var img = !string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta) ? PlatformInterop.LoadBitmapFromFile(ruta) : null;
                        if (img != null) proyeccionWindow?.MostrarImagenTexto(img);
                    }
                }
                else
                {
                    _isProjecting = false;
                    btn.SetCurrentValue(ContentProperty, "PROYECTAR ON/OFF (OFF)");
                    try { proyeccionWindow?.Hide(); } catch { }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cambiar proyección: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}