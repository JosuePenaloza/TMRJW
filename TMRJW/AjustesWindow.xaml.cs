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

            // 🌟 0. LLENAR COMBOBOX CON MONITORES DISPONIBLES 🌟
            CargarMonitoresDisponibles();

            // 1. Cargar la configuración al abrir la ventana
            TxtImagenTextoAnioRuta.Text = Settings.Default.ImagenTextoAnio;

            if (Settings.Default.MonitorSalidaIndex >= 0 &&
                Settings.Default.MonitorSalidaIndex < CboMonitorSalida.Items.Count)
            {
                CboMonitorSalida.SelectedIndex = Settings.Default.MonitorSalidaIndex;
            }
            else
            {
                CboMonitorSalida.SelectedIndex = 0;
            }
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
            MessageBox.Show(
                "La ruta de la imagen ha sido borrada. Recuerda guardar los ajustes para que el cambio se aplique.",
                "Imagen Borrada",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // =====================================================
        // 💾 GUARDAR AJUSTES
        // =====================================================

        private void BtnGuardarAjustes_Click(object sender, RoutedEventArgs e)
        {
            Settings.Default.ImagenTextoAnio = TxtImagenTextoAnioRuta.Text;
            Settings.Default.MonitorSalidaIndex = CboMonitorSalida.SelectedIndex;
            Settings.Default.Save();

            MessageBox.Show("Configuración guardada.", "Guardado Exitoso",
                MessageBoxButton.OK, MessageBoxImage.Information);

            this.Close();
        }

        // =====================================================
        // 📅 ADMINISTRACIÓN DE SEMANAS
        // =====================================================

        private void BtnBorrarSemanaActual_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "¿Está seguro que desea borrar la semana actual del programa cargado?",
                "Confirmar Borrado",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                MessageBox.Show("Borrado de semana solicitado.", "Pendiente");
            }
        }
    }
}
