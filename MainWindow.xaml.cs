// Agrega este método en tu clase MainWindow

private void BtnProyectarHDMI_Click(object sender, RoutedEventArgs e)
{
    // Lógica para manejar el evento del botón PROYECTAR ON/OFF
    // Por ejemplo, puedes alternar el texto del botón o realizar alguna acción
    if (BtnProyectarHDMI.Content.ToString().Contains("OFF"))
    {
        BtnProyectarHDMI.Content = "PROYECTAR ON/OFF (ON)";
    }
    else
    {
        BtnProyectarHDMI.Content = "PROYECTAR ON/OFF (OFF)";
    }
}