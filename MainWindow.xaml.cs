// Agrega este m�todo en tu clase MainWindow

private void BtnProyectarHDMI_Click(object sender, RoutedEventArgs e)
{
    // L�gica para manejar el evento del bot�n PROYECTAR ON/OFF
    // Por ejemplo, puedes alternar el texto del bot�n o realizar alguna acci�n
    if (BtnProyectarHDMI.Content.ToString().Contains("OFF"))
    {
        BtnProyectarHDMI.Content = "PROYECTAR ON/OFF (ON)";
    }
    else
    {
        BtnProyectarHDMI.Content = "PROYECTAR ON/OFF (OFF)";
    }
}