using System;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace TMRJW
{
    public class WebScraper
    {
        private const string BaseUrl = "https://wol.jw.org/es/wol/meetings/r4/lp-s/";

        private string ObtenerUrlSemana(int anio, int semana)
        {
            return $"{BaseUrl}{anio}/{semana}";
        }

        public async Task<string> ObtenerContenidoSemanal(int anio, int semana)
        {
            string url = ObtenerUrlSemana(anio, semana);
            string htmlContent = await DescargarHtml(url);

            if (string.IsNullOrEmpty(htmlContent))
            {
                return "❌ No se pudo conectar o descargar la página web.";
            }

            return ProcesarHtml(htmlContent);
        }

        private async Task<string> DescargarHtml(string url)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TMRJWApp/1.0)");
                    return await client.GetStringAsync(url);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al descargar HTML: {ex.Message}");
                return string.Empty;
            }
        }

        private string ProcesarHtml(string html)
        {
            HtmlAgilityPack.HtmlDocument htmlDoc = new HtmlAgilityPack.HtmlDocument();
            htmlDoc.LoadHtml(html);

            // Intentamos encontrar el contenedor principal del contenido de la reunión
            var meetingContentNode = htmlDoc.DocumentNode.SelectSingleNode("//div[@id='p1']");

            if (meetingContentNode != null)
            {
                // Extraer el título de la semana
                var titleNode = meetingContentNode.SelectSingleNode(".//div[@id='p2']/h2");
                string titulo = titleNode != null ? titleNode.InnerText.Trim() : "Programa Semanal";

                // Concatenar el contenido de las secciones principales
                var sections = meetingContentNode.SelectNodes(".//div[contains(@class, 'themeContainer')]");

                string contenido = "\n\n";
                if (sections != null)
                {
                    foreach (var section in sections)
                    {
                        // Limpiamos y agregamos saltos de línea
                        string sectionText = section.InnerText.Replace("&nbsp;", " ").Trim();
                        contenido += sectionText + "\n\n";
                    }
                }

                return $"{titulo}\n\n======================\n\n{contenido.Trim()}";
            }
            else
            {
                return "⚠️ No se encontró el contenido principal en la página.";
            }
        }
    }
}