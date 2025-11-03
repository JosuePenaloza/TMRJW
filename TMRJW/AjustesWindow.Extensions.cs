using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace TMRJW
{
    public partial class AjustesWindow : Window
    {
        private void AjustesWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // cargar settings y rellenar lista de monitores
                var settings = SettingsHelper.Load();
                PopulateMonitorsListNative(settings.SelectedMonitorDeviceName);
                // mostrar ruta guardada si existe
                if (!string.IsNullOrEmpty(settings.ImagenTextoAnio))
                    TxtImagenTextoAnioRuta.Text = settings.ImagenTextoAnio;
            }
            catch { }
        }

        private void PopulateMonitorsListNative(string savedDeviceName = null)
        {
            try
            {
                CboMonitorSalida.Items.Clear();

                var screens = GetMonitorsNative();
                for (int i = 0; i < screens.Count; i++)
                {
                    var s = screens[i];
                    var displayName = $"{i + 1}: {s.DeviceName} ({s.Width}x{s.Height}){(s.IsPrimary ? " (Primary)" : "")}";
                    var item = new ComboBoxItem { Content = displayName, Tag = s.DeviceName };
                    CboMonitorSalida.Items.Add(item);

                    // seleccionar si coincide con el guardado
                    if (!string.IsNullOrEmpty(savedDeviceName) && string.Equals(savedDeviceName, s.DeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        CboMonitorSalida.SelectedItem = item;
                    }
                }

                // si no hubo coincidencia, seleccionar el último (proyector) o el primario si no hay ultimo
                if (CboMonitorSalida.SelectedItem == null && CboMonitorSalida.Items.Count > 0)
                {
                    CboMonitorSalida.SelectedIndex = Math.Max(0, CboMonitorSalida.Items.Count - 1);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al listar monitores: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnBorrarCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var cacheDir = Path.Combine(local, "TMRJW", "cache");

                if (!Directory.Exists(cacheDir))
                {
                    MessageBox.Show("No se encontró carpeta de caché.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show("¿Deseas eliminar todos los archivos de la caché de imágenes?\nEsta acción no se puede deshacer.", "Confirmar borrado de caché", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                var files = Directory.GetFiles(cacheDir);
                var deleted = 0;
                foreach (var f in files)
                {
                    try { File.Delete(f); deleted++; } catch { }
                }

                foreach (var d in Directory.GetDirectories(cacheDir))
                {
                    try { Directory.Delete(d, true); } catch { }
                }

                MessageBox.Show($"Eliminados {deleted} archivos de la caché.", "Operación completada", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al borrar la caché: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Nuevo nombre del handler para evitar duplicado con el otro partial
        private void BtnGuardarAjustes_SaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsHelper.Load();

                // monitor seleccionado (tag)
                if (CboMonitorSalida.SelectedItem is ComboBoxItem sel && sel.Tag is string devName)
                    settings.SelectedMonitorDeviceName = devName;
                else
                    settings.SelectedMonitorDeviceName = null;

                // imagen de Texto del Año
                settings.ImagenTextoAnio = TxtImagenTextoAnioRuta.Text;

                SettingsHelper.Save(settings);

                MessageBox.Show("Ajustes guardados.", "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar ajustes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ----- Native monitor enumeration -----
        private class MonitorInfoSimple { public string DeviceName = ""; public int Width; public int Height; public bool IsPrimary; }

        private List<MonitorInfoSimple> GetMonitorsNative()
        {
            var list = new List<MonitorInfoSimple>();

            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr lParam) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int w = mi.rcMonitor.right - mi.rcMonitor.left;
                    int h = mi.rcMonitor.bottom - mi.rcMonitor.top;
                    bool primary = (mi.dwFlags & 1) != 0;
                    list.Add(new MonitorInfoSimple { DeviceName = mi.szDevice.Trim('\0'), Width = w, Height = h, IsPrimary = primary });
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

    // Helper sencillo para persistir ajustes en JSON dentro de %LocalAppData%\TMRJW\settings.json
    internal static class SettingsHelper
    {
        internal record AppSettings
        {
            public string? SelectedMonitorDeviceName { get; set; }
            public string? ImagenTextoAnio { get; set; }
        }

        private static string GetPath()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(local, "TMRJW");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        public static (string? SelectedMonitorDeviceName, string? ImagenTextoAnio) Load()
        {
            try
            {
                var p = GetPath();
                if (!File.Exists(p)) return (null, null);
                var json = File.ReadAllText(p);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                return (s?.SelectedMonitorDeviceName, s?.ImagenTextoAnio);
            }
            catch { return (null, null); }
        }

        public static void Save((string? SelectedMonitorDeviceName, string? ImagenTextoAnio) tuple)
        {
            var s = new AppSettings { SelectedMonitorDeviceName = tuple.SelectedMonitorDeviceName, ImagenTextoAnio = tuple.ImagenTextoAnio };
            var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetPath(), json);
        }

        // overloads for convenience
        public static (string? SelectedMonitorDeviceName, string? ImagenTextoAnio) LoadSettings() => Load();
        public static void Save(string? selectedMonitorDeviceName, string? imagenTextoAnio) => Save((selectedMonitorDeviceName, imagenTextoAnio));
        internal static void Save(AppSettings s) => Save((s.SelectedMonitorDeviceName, s.ImagenTextoAnio));
    }
}