using System;
using System.Windows;
using Microsoft.Win32;
using TMRJW.Properties;
using TMRJW.Models; // ✅ Importamos la clase Monitors

namespace TMRJW
{
    /// <summary>
    /// Lógica de interacción para AjustesWindow.xaml
    /// </summary>
    public partial class AjustesWindow : Window
    {
        public AjustesWindow()
        {
            InitializeComponent();

            // 1. Cargar la configuración al abrir la ventana
            var settings = SettingsHelper.Load();
            TxtImagenTextoAnioRuta.Text = settings.ImagenTextoAnio ?? string.Empty;
            TxtFfmpegPath.Text = settings.FfmpegPath ?? string.Empty;
            // Set toggle initial state (use FindName to avoid direct generated field dependency)
            try
            {
                var tb = this.FindName("BtnToggleTheme") as System.Windows.Controls.Primitives.ToggleButton;
                if (tb != null) tb.IsChecked = settings.IsDarkTheme;
            }
            catch { }

            // La lista de monitores se completa en AjustesWindow_Loaded (PopulateMonitorsListNative)
        }

        // =====================================================
        // ✅ NUEVA VERSIÓN - USANDO LA CLASE MODELS.MONITORS
        // =====================================================
        private void CargarMonitoresDisponibles()
        {
            CboMonitorSalida.Items.Clear();

            var screens = Monitors.GetAllMonitors();

            for (int i = 0; i < screens.Count; i++)
            {
                var monitor = screens[i];
                string monitorName = $"Monitor {i + 1} ({monitor.DeviceName})";

                if (monitor.IsPrimary)
                    monitorName += " - Principal";

                monitorName += $" [{monitor.Width}x{monitor.Height}]";

                CboMonitorSalida.Items.Add(monitorName);
            }

            if (CboMonitorSalida.Items.Count > 0)
                CboMonitorSalida.SelectedIndex = 0;
        }

        // =====================================================
        // 📷 GESTIÓN DE IMAGEN DEL TEXTO DEL AÑO
        // =====================================================

        private void BtnSeleccionarImagen_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Archivos de Imagen|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtImagenTextoAnioRuta.Text = openFileDialog.FileName;
            }
        }

        private void BtnBorrarImagen_Click(object sender, RoutedEventArgs e)
        {
            TxtImagenTextoAnioRuta.Text = string.Empty;
            AlertHelper.ShowSilentInfo(this, "La ruta de la imagen ha sido borrada. Recuerda guardar los ajustes para que el cambio se aplique.", "Imagen Borrada");
        }

        private void BtnSeleccionarFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "FFmpeg executable|ffmpeg.exe",
                Title = "Seleccionar ffmpeg.exe"
            };

            if (dlg.ShowDialog() == true)
            {
                // Guardar la carpeta que contiene bin (path hasta carpeta bin)
                var exePath = dlg.FileName;
                var binDir = System.IO.Path.GetDirectoryName(exePath) ?? exePath;
                TxtFfmpegPath.Text = binDir;
            }
        }

        // =====================================================
        // 💾 GUARDAR AJUSTES
        // =====================================================

        private void BtnGuardarAjustes_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.ImagenTextoAnio = TxtImagenTextoAnioRuta.Text;
            Settings.Default.MonitorSalidaIndex = CboMonitorSalida.SelectedIndex;
            Settings.Default.Save();

            AlertHelper.ShowSilentInfo(this, "Configuración guardada.", "Guardado Exitoso");

            this.Close();
        }

        // Handler renameado para compatibilidad con otro partial
        private void BtnGuardarAjustes_SaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsHelper.Load();

                // monitor seleccionado (tag) - compatibilidad con earlier partial
                if (CboMonitorSalida.SelectedItem is System.Windows.Controls.ComboBoxItem sel && sel.Tag is string devName)
                    settings.SelectedMonitorDeviceName = devName;
                else
                    settings.SelectedMonitorDeviceName = null;

                settings.ImagenTextoAnio = TxtImagenTextoAnioRuta.Text;

                // Guardar ruta FFmpeg si el usuario la especificó
                if (!string.IsNullOrWhiteSpace(TxtFfmpegPath.Text))
                    settings.FfmpegPath = TxtFfmpegPath.Text;

                SettingsHelper.Save(settings);

                AlertHelper.ShowSilentInfo(this, "Ajustes guardados.", "Información");
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar ajustes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =====================================================
        // 📅 ADMINISTRACIÓN DE SEMANAS
        // =====================================================

        private void BtnBorrarSemanaActual_Click(object sender, RoutedEventArgs e)
        {
            var result = AlertHelper.ShowSilentConfirm(this, "¿Está seguro que desea borrar la semana actual del programa cargado?", "Confirmar Borrado");
            if (result)
            {
                AlertHelper.ShowSilentInfo(this, "Borrado de semana solicitado.", "Pendiente");
            }
        }

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isDark = false;
                try { isDark = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true; } catch { }

                // Swap merged dictionary in App resources
                var app = Application.Current as App;
                if (app == null) return;
                var dicts = app.Resources.MergedDictionaries;
                // remove existing theme dictionaries
                for (int i = dicts.Count - 1; i >= 0; i--)
                {
                    var src = dicts[i].Source?.OriginalString ?? string.Empty;
                    if (src.Contains("Themes/DarkTheme.xaml") || src.Contains("Themes/LightTheme.xaml"))
                        dicts.RemoveAt(i);
                }
                var newDict = new ResourceDictionary();
                newDict.Source = new System.Uri(isDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", System.UriKind.Relative);
                dicts.Add(newDict);


                // Persist preference
                try
                {
                    var s = SettingsHelper.Load();
                    s.IsDarkTheme = isDark;
                    SettingsHelper.Save(s);
                }
                catch { }
            }
            catch { }
        }
    }
}
