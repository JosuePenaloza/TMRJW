using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
                // asegurarse de que el checkbox (palomeo) refleje el setting si existe
                try
                {
                    var chk = this.FindName("ChkFloatingProjection") as CheckBox;
                    if (chk != null) chk.IsChecked = settings.UseFloatingProjectionWindow;
                }
                catch { }
                // mostrar ruta guardada si existe
                if (!string.IsNullOrEmpty(settings.ImagenTextoAnio))
                    TxtImagenTextoAnioRuta.Text = settings.ImagenTextoAnio;

                // mostrar ruta FFmpeg si existe
                if (!string.IsNullOrEmpty(settings.FfmpegPath))
                    try { TxtFfmpegPath.Text = settings.FfmpegPath; } catch { }
            }
            catch { }
        }

        // Handler para checkbox que selecciona modo flotante
        private void ChkFloatingProjection_CheckedChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                var chk = sender as CheckBox;
                var s = SettingsHelper.Load();
                s.UseFloatingProjectionWindow = chk != null && chk.IsChecked == true;
                SettingsHelper.Save(s);
            }
            catch { }
        }

        // Botón que mueve la proyección al monitor seleccionado inmediatamente
        private void BtnMoveProjectionNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = SettingsHelper.Load();
                var monitors = PlatformInterop.GetMonitorsNative();
                PlatformInterop.MonitorInfo? target = null;
                if (CboMonitorSalida.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
                {
                    target = monitors.Find(m => string.Equals(m.DeviceName, tag, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    int idx = CboMonitorSalida.SelectedIndex;
                    if (idx >= 0 && idx < monitors.Count) target = monitors[idx];
                }

                if (target == null) return;

                // Find projection window and move it
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is ProyeccionWindow pw)
                    {
                        try
                        {
                            if (s.UseFloatingProjectionWindow)
                                pw.MoveToMonitor(target);
                            else
                                pw.ConfigureFullscreenOnMonitor(target);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void PopulateMonitorsListNative(string savedDeviceName = null)
        {
            try
            {
                CboMonitorSalida.Items.Clear();

                var screens = PlatformInterop.GetMonitorsNative();
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
                try { AlertHelper.ShowSilentInfo(this, $"Error al listar monitores: {ex.Message}", "Error"); } catch { }
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
                    AlertHelper.ShowSilentInfo(this, "No se encontró carpeta de caché.", "Información");
                    return;
                }

                var result = AlertHelper.ShowSilentConfirm(this, "¿Deseas eliminar todos los archivos de la caché de imágenes?\nEsta acción no se puede deshacer.", "Confirmar borrado de caché");
                if (!result) return;

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

                AlertHelper.ShowSilentInfo(this, $"Eliminados {deleted} archivos de la caché.", "Operación completada");
            }
            catch (Exception ex)
            {
                try { AlertHelper.ShowSilentInfo(this, $"Error al borrar la caché: {ex.Message}", "Error"); } catch { }
            }
        }
    }
}