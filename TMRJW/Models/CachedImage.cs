using System.IO;
using System.Windows.Media.Imaging;

namespace TMRJW
{
    public class CachedImage
    {
        public string FilePath { get; set; } = string.Empty;
        public BitmapImage Image { get; set; } = null!;
        public override string ToString() => Path.GetFileName(FilePath);
    }
}