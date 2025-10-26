using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TMRJW.Models
{
    public class MonitorInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public int X { get; set; }  // ✅ Nueva propiedad
        public int Y { get; set; }  // ✅ Nueva propiedad
        public int Width { get; set; }
        public int Height { get; set; }

        public override string ToString()
        {
            string tipo = IsPrimary ? "Principal" : "Secundario";
            return $"{DeviceName} ({Width}x{Height}) Pos({X},{Y}) - {tipo}";
        }
    }

    public static class Monitors
    {
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
            MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoEx
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        private const int MONITORINFOF_PRIMARY = 1;

        public static List<MonitorInfo> GetAllMonitors()
        {
            var result = new List<MonitorInfo>();

            // Cambia la lambda en EnumDisplayMonitors para que todos los parámetros sean explícitos
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) =>
            {
                MonitorInfoEx mi = new MonitorInfoEx();
                mi.cbSize = Marshal.SizeOf(typeof(MonitorInfoEx));

                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int width = mi.rcMonitor.Right - mi.rcMonitor.Left;
                    int height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                    int x = mi.rcMonitor.Left;   // ✅ Coordenada X del monitor
                    int y = mi.rcMonitor.Top;    // ✅ Coordenada Y del monitor

                    result.Add(new MonitorInfo
                    {
                        DeviceName = mi.szDevice.Trim(),
                        IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height
                    });
                }

                return true;
            }, IntPtr.Zero);

            return result;
        }
    }
}
