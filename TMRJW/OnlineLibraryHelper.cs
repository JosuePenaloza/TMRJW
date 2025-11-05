using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace TMRJW
{
    public static class OnlineLibraryHelper
    {
        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// Construye la URL semanal para wol.jw.org en la sección de Reunión de la semana.
        /// Ej: GetWeeklyUrl(2025, 44) -> https://wol.jw.org/es/wol/meetings/r4/lp-s/2025/44
        /// </summary>
        public static string GetWeeklyUrl(int year, int week) => $"https://wol.jw.org/es/wol/meetings/r4/lp-s/{year}/{week}";

        /// <summary>
        /// Extrae URIs de imágenes (jpg/png/gif/webp) encontradas en el HTML de la página.
        /// Usa un parseo sencillo mediante regex buscando recursos en el dominio wol.jw.org.
        /// </summary>
        public static async Task<List<Uri>> ExtractImageUrisFromPageAsync(string pageUrl, CancellationToken ct = default)
        {
            return await ExtractMediaUrisFromPageAsync(pageUrl, new[] { "png", "jpg", "jpeg", "gif", "webp" }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Extrae URIs de recursos cuya extensión esté en la lista "extensions".
        /// </summary>
        public static async Task<List<Uri>> ExtractMediaUrisFromPageAsync(string pageUrl, string[] extensions, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(pageUrl) || extensions == null || extensions.Length == 0) return new List<Uri>();
            try
            {
                string html = await _http.GetStringAsync(pageUrl, ct).ConfigureAwait(false);
                // construir una alternativa en regex como (?:png|jpg|mp4)
                var alt = string.Join("|", extensions.Select(e => Regex.Escape(e)));
                var pattern = $"https?:\\/\\/(?:\\w+\\.)?wol\\.jw\\.org\\/[^\\s'\"<>]+?\\.(?:{alt})";
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                var uris = matches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => Uri.TryCreate(m.Value, UriKind.Absolute, out var u) ? u : null)
                    .Where(u => u != null)
                    .Select(u => u!)
                    .Distinct(new UriComparer())
                    .ToList();
                return uris;
            }
            catch
            {
                return new List<Uri>();
            }
        }

        /// <summary>
        /// Descarga imágenes (stream) y crea BitmapImage congeladas listas para UI thread.
        /// </summary>
        public static async Task<List<BitmapImage>> DownloadImagesAsync(IEnumerable<Uri> uris, CancellationToken ct = default)
        {
            var result = new List<BitmapImage>();
            foreach (var uri in uris)
            {
                try
                {
                    using var resp = await _http.GetAsync(uri, ct).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) continue;
                    using var s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    var ms = new MemoryStream();
                    await s.CopyToAsync(ms, ct).ConfigureAwait(false);
                    ms.Position = 0;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    result.Add(bi);
                }
                catch
                {
                    // ignorar imagen con error y continuar
                }
            }
            return result;
        }

        private class UriComparer : IEqualityComparer<Uri>
        {
            public bool Equals(Uri x, Uri y) => x?.AbsoluteUri == y?.AbsoluteUri;
            public int GetHashCode(Uri obj) => obj?.AbsoluteUri?.GetHashCode() ?? 0;
        }
    }
}