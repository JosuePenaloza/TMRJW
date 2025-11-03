using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace TMRJW
{
    public partial class MainWindow : Window
    {
        // Llama a este método cuando quieras abrir la proyección en el monitor seleccionado.
        // Asegúrate de invocarlo desde el handler que actualmente abre la proyección.
        public void OpenProyeccionOnSelectedMonitor()
        {
            try
            {
                var settings = SettingsHelper.Load();
                var selectedDevice = settings.SelectedMonitorDeviceName;

                var monitors = GetMonitorsNative();
                MonitorInfo? target = null;

                // Si hay selección guardada, intentar usarla
                if (!string.IsNullOrEmpty(selectedDevice))
                    target = monitors.Find(m => string.Equals(m.DeviceName, selectedDevice, StringComparison.OrdinalIgnoreCase));

                // Si no hay selección o no se encontró: preferir primer monitor NO primario (externo)
                if (target == null)
                {
                    target = monitors.Find(m => !m.IsPrimary);
                }

                // Si todavía no hay target, usar el primario o el primero disponible
                if (target == null)
                    target = monitors.Find(m => m.IsPrimary) ?? (monitors.Count > 0 ? monitors[0] : null);

                if (target == null)
                {
                    // fallback: crear proyección en pantalla actual
                    var fallback = new ProyeccionWindow
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };
                    proyeccionWindow = fallback;
                    proyeccionWindow.Show();
                    return;
                }

                // Obtener escala DPI de la ventana actual (WPF DIP = pixels / dpiScale)
                var dpiScale = VisualTreeHelper.GetDpi(this);
                double dpiScaleX = dpiScale.DpiScaleX;
                double dpiScaleY = dpiScale.DpiScaleY;

                // Convertir coordenadas de monitor (píxeles) a unidades WPF (DIP)
                double wpfLeft = target.X / dpiScaleX;
                double wpfTop = target.Y / dpiScaleY;
                double wpfWidth = target.Width / dpiScaleX;
                double wpfHeight = target.Height / dpiScaleY;

                // Crear la ventana de proyección y asignarla al campo proyeccionWindow
                var pw = new ProyeccionWindow
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = wpfLeft,
                    Top = wpfTop,
                    Width = wpfWidth,
                    Height = wpfHeight,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    Topmost = true,
                    ShowInTaskbar = false
                };

                // Si ya existía una proyección abierta, cerrarla antes de reemplazar
                try
                {
                    if (proyeccionWindow != null && !ReferenceEquals(proyeccionWindow, pw))
                    {
                        try { proyeccionWindow.Close(); } catch { }
                        proyeccionWindow = null;
                    }
                }
                catch { }

                proyeccionWindow = pw;
                proyeccionWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la proyección en el monitor seleccionado: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                // fallback
                try
                {
                    if (proyeccionWindow == null)
                    {
                        proyeccionWindow = new ProyeccionWindow();
                        proyeccionWindow.Show();
                    }
                }
                catch { }
            }
        }

        // ----- Native monitor enumeration (similar a AjustesWindow) -----
        private class MonitorInfo { public string DeviceName = ""; public int X; public int Y; public int Width; public int Height; public bool IsPrimary; }

        private List<MonitorInfo> GetMonitorsNative()
        {
            var list = new List<MonitorInfo>();

            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr lParam) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
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
    }
}