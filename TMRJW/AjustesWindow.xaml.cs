using System.Windows;
using TMRJW.Properties;
using Microsoft.Win32;
// 🌟 IMPORTE NECESARIO para acceder a la clase Screen 🌟
using System.Windows.Forms;

namespace  TMRJW
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

            // Cargar la Ruta de la Imagen guardada
            TxtImagenTextoAnioRuta.Text = Settings.Default.ImagenTextoAnio;

            // Cargar la selección del Monitor. Usamos el índice guardado.
            // Si el índice es válido (>= 0), lo selecciona; de lo contrario, se queda en el valor por defecto de XAML.
            if (Settings.Default.MonitorSalidaIndex >= 0 && Settings.Default.MonitorSalidaIndex < CboMonitorSalida.Items.Count)
            {
                CboMonitorSalida.SelectedIndex = Settings.Default.MonitorSalidaIndex;
            }
            else
            {
                // Si no hay índice guardado o es inválido, selecciona el primero (monitor principal)
                CboMonitorSalida.SelectedIndex = 0;
            }
        }

        // 🌟 NUEVO MÉTODO PARA CARGAR LOS MONITORES 🌟
        private void CargarMonitoresDisponibles()
        {
            CboMonitorSalida.Items.Clear();

            // Usamos System.Windows.Forms.Screen
            Screen[] screens = Screen.AllScreens;

            for (int i = 0; i < screens.Length; i++)
            {
                // Agregamos un item con el nombre del monitor (Principal o Secundario)
                string monitorName = $"Monitor {i + 1} ({screens[i].DeviceName.TrimEnd('\\', '.')})";
                if (screens[i].Primary)
                {
                    monitorName += " - Principal";
                }

                CboMonitorSalida.Items.Add(monitorName);
            }

            // Asegurar que haya al menos un elemento para seleccionar
            if (CboMonitorSalida.Items.Count > 0)
            {
                CboMonitorSalida.SelectedIndex = 0;
            }
        }

        // --- Métodos de Gestión de la Imagen del Texto del Año ---

        // Método para abrir el explorador y seleccionar la imagen
        private void BtnSeleccionarImagen_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Archivos de Imagen|*.jpg;*.jpeg;*.png;*.bmp"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtImagenTextoAnioRuta.Text = openFileDialog.FileName;
            }
        }

        // Método para borrar la ruta de la imagen
        private void BtnBorrarImagen_Click(object sender, RoutedEventArgs e)
        {
            TxtImagenTextoAnioRuta.Text = string.Empty;
            _ = System.Windows.MessageBox.Show("La ruta de la imagen ha sido borrada. Recuerda guardar los ajustes para que el cambio se aplique.", "Imagen Borrada", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        // --- Método para Guardar y Cerrar ---

        private void BtnGuardarAjustes_Click(object sender, RoutedEventArgs e)
        {
            // 1. Guardar la Ruta de la Imagen
            Settings.Default.ImagenTextoAnio = TxtImagenTextoAnioRuta.Text;

            // 2. Guardar la selección del Monitor (guardamos el índice seleccionado)
            Settings.Default.MonitorSalidaIndex = CboMonitorSalida.SelectedIndex;

            // 3. Persistir los cambios en el disco
            Settings.Default.Save();

            _ = System.Windows.MessageBox.Show("Configuración guardada.", "Guardado Exitoso", MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

            this.Close();
        }

        // --- Método de Administración de Semanas ---

        private void BtnBorrarSemanaActual_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "¿Está seguro que desea borrar la semana actual del programa cargado?",
                "Confirmar Borrado",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                _ = System.Windows.MessageBox.Show("Borrado de semana solicitado.", "Pendiente");
            }
        }
    }
}