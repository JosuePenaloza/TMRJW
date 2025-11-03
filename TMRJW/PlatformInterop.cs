using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        // Generar thumbnail de frame de video (usa MediaPlayer en dispatcher)
        public static BitmapImage? GenerateVideoFrameThumbnail(string path, int width, int height)
        {
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
                            // Calcular posición representativa:50% del total (frame central) o300ms mínimo
                            if (mp.NaturalDuration.HasTimeSpan)
                            {
                                var dur = mp.NaturalDuration.TimeSpan.TotalMilliseconds;
                                if (dur > 0)
                                {
                                    double posMs = dur * 0.5; // frame del centro
                                    // Clamp para estar dentro de rango
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

                        // Notify waiter
                        mre.Set();
                    };

                    try
                    {
                        mp.Open(new Uri(path));
                        // Esperar a MediaOpened (o timeout)
                        if (!mre.WaitOne(4000)) { mp.Close(); return; }

                        // Si no se asignó posición, fallback
                        if (!chosenPosition.HasValue) chosenPosition = TimeSpan.FromMilliseconds(300);

                        // Asegurarse de que la posición sea válida
                        try { mp.Position = chosenPosition.Value; } catch { }

                        // Silenciar y reproducir brevemente para que el frame esté listo
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

                return result;
            }
            catch { return null; }
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