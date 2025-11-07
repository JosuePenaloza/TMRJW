using System.Windows;

namespace TMRJW
{
    public partial class SilentAlertWindow : Window
    {
        public SilentAlertWindow(string message, string title = "Información")
        {
            InitializeComponent();
            this.Title = title;
            TxtMessage.Text = message;
            // Desactivar sonido en la ventana no es directo; MessageBox hace sonido por defecto.
            // Esta ventana personalizada no reproducirá sonido al mostrarse.
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
