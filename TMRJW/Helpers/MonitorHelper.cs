using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace TMRJW
{
    // Información pública por monitor usada en AjustesWindow.xaml.cs
    public class MonitorInfo
    {
        // Índice asignado en la enumeración (0,1,2...)
        public int Index { get; set; }

        // Nombre del dispositivo (p. ej. \\.\DISPLAY1)
        public string DeviceName { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }

        // Coordenadas y tamaño del monitor (área completa)
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public override string ToString()
        {
            string tipo = IsPrimary ? "Principal" : "Secundario";
            return $"{DeviceName} [{Width}x{Height}] Pos({X},{Y}) - {tipo}";
        }
    }

    internal static class MonitorHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

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

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        private const uint MONITORINFOF_PRIMARY = 0x00000001;

        // Devuelve la lista de monitores en el orden en que EnumDisplayMonitors los enumera.
        public static List<MonitorInfo> GetAllMonitors()
        {
            var list = new List<MonitorInfo>();
            int idx = 0;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(mi);
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int width = mi.rcMonitor.right - mi.rcMonitor.left;
                    int height = mi.rcMonitor.bottom - mi.rcMonitor.top;
                    int x = mi.rcMonitor.left;
                    int y = mi.rcMonitor.top;

                    list.Add(new MonitorInfo
                    {
                        Index = idx,
                        DeviceName = (mi.szDevice ?? $"Display{idx}").TrimEnd('\0'),
                        IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height
                    });
                }

                idx++;
                return true; // continuar enumeración
            }, IntPtr.Zero);

            // Mantener el orden tal cual (si prefieres mostrar principal primero, puedes ordenar aquí)
            return list;
        }
    }
}