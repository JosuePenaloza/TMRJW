using System.Windows.Media.Imaging;

public class ProgramaSemana
{
    public string Titulo { get; set; }
    public string ContenidoHtml { get; set; }
    public BitmapImage ImagenSemana { get; set; }
}

public class GrupoImagenes
{
    public string TituloPestana { get; set; }
    public List<BitmapImage> Imagenes { get; set; } = new List<BitmapImage>();
    public bool EsIntroduccion { get; set; } = false;
}
