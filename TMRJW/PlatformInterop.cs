using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;

namespace TMRJW
{
    internal static class PlatformInterop
    {
        // Simple DTO para monitores
        public sealed class MonitorInfo { public string DeviceName = ""; public int X; public int Y; public int Width; public int Height; public bool IsPrimary; }

        // Obtener monitores nativos
        public static List<MonitorInfo> GetMonitorsNative()
        {
            var list = new List<MonitorInfo>();
            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr lParam) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int left = mi.rcMonitor.left;
                    int top = mi.rcMonitor.top;
                    int right = mi.rcMonitor.right;
                    int bottom = mi.rcMonitor.bottom;
                    int w = right - left;
                    int h = bottom - top;
                    bool primary = (mi.dwFlags & 1) != 0;
                    list.Add(new MonitorInfo
                    {
                        DeviceName = mi.szDevice.Trim('\0'),
                        X = left,
                        Y = top,
                        Width = w,
                        Height = h,
                        IsPrimary = primary
                    });
                }
                return true;
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return list;
        }

        // Cargar BitmapImage desde archivo (rápido, seguro)
        public static BitmapImage? LoadBitmapFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var bi = new BitmapImage();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = fs;
                    bi.EndInit();
                    bi.Freeze();
                }
                return bi;
            }
            catch { return null; }
        }

        // Obtener icono (thumbnail) de fichero como BitmapImage
        public static BitmapImage? GetFileIconAsBitmap(string path, int width = 48, int height = 48)
        {
            try
            {
                if (!File.Exists(path)) return null;
                SHFILEINFO shfi;
                IntPtr res = SHGetFileInfo(path, 0, out shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
                if (shfi.hIcon == IntPtr.Zero) return null;
                try
                {
                    var bmpSource = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(width, height));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmpSource));
                    using (var ms = new MemoryStream())
                    {
                        encoder.Save(ms);
                        ms.Position = 0;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                }
                finally
                {
                    DestroyIcon(shfi.hIcon);
                }
            }
            catch { return null; }
        }

        // Generar thumbnail de frame de video (usa MediaPlayer en dispatcher).
        // Si falla, intenta extraer el frame usando ffmpeg embebido en la carpeta "ffmpeg/bin/x64" o en la ruta especificada en settings.
        public static BitmapImage? GenerateVideoFrameThumbnail(string path, int width, int height)
        {
            // Intentar método nativo con MediaPlayer primero
            try
            {
                BitmapImage? result = null;
                var mre = new System.Threading.AutoResetEvent(false);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var mp = new MediaPlayer();
                    TimeSpan? chosenPosition = null;

                    // Establecer handler para MediaOpened
                    mp.MediaOpened += (s, e) =>
                    {
                        try
                        {
                            if (mp.NaturalDuration.HasTimeSpan)
                            {
                                var dur = mp.NaturalDuration.TimeSpan.TotalMilliseconds;
                                if (dur > 0)
                                {
                                    double posMs = dur * 0.5; // frame del centro
                                    posMs = Math.Max(200, Math.Min(dur - 100, posMs));
                                    chosenPosition = TimeSpan.FromMilliseconds(posMs);
                                }
                            }
                            else
                            {
                                chosenPosition = TimeSpan.FromMilliseconds(300);
                            }
                        }
                        catch { chosenPosition = TimeSpan.FromMilliseconds(300); }

                        mre.Set();
                    };

                    try
                    {
                        mp.Open(new Uri(path));
                        if (!mre.WaitOne(4000)) { mp.Close(); return; }

                        if (!chosenPosition.HasValue) chosenPosition = TimeSpan.FromMilliseconds(300);

                        try { mp.Position = chosenPosition.Value; } catch { }

                        try { mp.Volume = 0.0; } catch { }
                        try { mp.Play(); } catch { }

                        System.Threading.Thread.Sleep(400);

                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            var vb = new VideoDrawing { Rect = new Rect(0, 0, width, height), Player = mp };
                            dc.DrawDrawing(vb);
                        }

                        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        mp.Close();

                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(rtb));
                        using (var ms = new MemoryStream())
                        {
                            encoder.Save(ms);
                            ms.Position = 0;
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.StreamSource = ms;
                            bi.EndInit();
                            bi.Freeze();
                            result = bi;
                        }
                    }
                    catch
                    {
                        try { mp.Close(); } catch { }
                    }
                });

                if (result != null)
                    return result;
            }
            catch { /* Ignorar y pasar al fallback */ }

            // Fallback: usar ffmpeg embebido o ruta en settings
            try
            {
                var settings = SettingsHelper.Load();
                BitmapImage? ff = null;

                // Priorizar ruta configurada por el usuario
                if (!string.IsNullOrWhiteSpace(settings.FfmpegPath))
                {
                    var candidate = Path.Combine(settings.FfmpegPath, "ffmpeg.exe");
                    if (File.Exists(candidate)) ff = GenerateVideoFrameThumbnailWithFfmpegExecutable(candidate, path, width, height);
                }

                // Si no hay ruta configurada o falló, intentar carpeta bundlada
                if (ff == null)
                {
                    ff = GenerateVideoFrameThumbnailWithBundledFfmpeg(path, width, height);
                }

                if (ff != null) return ff;
            }
            catch { }

            return null;
        }

        private static BitmapImage? GenerateVideoFrameThumbnailWithFfmpegExecutable(string ffmpegExe, string path, int width, int height)
        {
            if (!File.Exists(path) || !File.Exists(ffmpegExe)) return null;
            string tempOut = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".png");
            string[] offsets = new[] { "00:00:01.000", "00:00:00.500", "00:00:02.000" };

            foreach (var offset in offsets)
            {
                try
                {
                    var args = $"-ss {offset} -i \"{path}\" -frames:v 1 -vf \"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2\" -y \"{tempOut}\"";

                    var psi = new ProcessStartInfo(ffmpegExe, args)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };

                    using (var proc = Process.Start(psi))
                    {
                        if (proc == null) continue;
                        proc.StandardError.ReadToEndAsync();
                        proc.StandardOutput.ReadToEndAsync();
                        if (!proc.WaitForExit(5000))
                        {
                            try { proc.Kill(); } catch { }
                            continue;
                        }

                        if (!File.Exists(tempOut)) continue;
                        var fi = new FileInfo(tempOut);
                        if (fi.Length < 200) { try { File.Delete(tempOut); } catch { } continue; }

                        var bi = LoadBitmapFromFile(tempOut);
                        try { File.Delete(tempOut); } catch { }
                        if (bi != null) return bi;
                    }
                }
                catch
                {
                    try { if (File.Exists(tempOut)) File.Delete(tempOut); } catch { }
                }
            }

            return null;
        }

        // Usa ffmpeg.exe ubicado en <appdir>/ffmpeg/bin/x64/ffmpeg.exe para extraer un frame.
        private static BitmapImage? GenerateVideoFrameThumbnailWithBundledFfmpeg(string path, int width, int height)
        {
            if (!File.Exists(path)) return null;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
            string ffmpegExe = Path.Combine(baseDir, "ffmpeg", "bin", "x64", "ffmpeg.exe");
            if (!File.Exists(ffmpegExe)) return null;

            return GenerateVideoFrameThumbnailWithFfmpegExecutable(ffmpegExe, path, width, height);
        }

        // --- P/Invoke y structs (únicos en el proyecto) ---
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
    }
}