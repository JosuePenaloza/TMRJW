using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop; // Para obtener el handle de la ventana
// Nota: Ya no usamos System.Windows.Forms

namespace TMRJW
{
    public partial class ProyeccionWindow : Window
    {
        // ... (otros miembros existentes)

        /// <summary>
        /// Posiciona la ventana en el monitor especificado por su índice.
        /// Implementación sin System.Windows.Forms (uso de P/Invoke Win32).
        /// </summary>
        public void ActualizarMonitor(int monitorIndex)
        {
            var screens = NativeMonitor.GetMonitors();
            if (monitorIndex < 0 || monitorIndex >= screens.Count)
                monitorIndex = 0; // fallback al principal

            var screen = screens[monitorIndex];
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = screen.WorkArea.X;
            this.Top = screen.WorkArea.Y;
            this.Width = screen.WorkArea.Width;
            this.Height = screen.WorkArea.Height;
        }
    }

    internal static class NativeMonitor
    {
        internal struct ScreenInfo
        {
            internal Rect WorkArea;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left, top, right, bottom;
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

        internal static List<ScreenInfo> GetMonitors()
        {
            var list = new List<ScreenInfo>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr hMonitor, IntPtr hdc, ref RECT r, IntPtr d)
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    var work = mi.rcWork;
                    var rect = new Rect(work.left, work.top, work.right - work.left, work.bottom - work.top);
                    list.Add(new ScreenInfo { WorkArea = rect });
                }
                return true; // continuar enumeración
            }, IntPtr.Zero);
            return list;
        }
    }
}