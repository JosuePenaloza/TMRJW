using System;
using System.Windows;
using System.Windows.Media;

namespace TMRJW
{
    public partial class MainWindow : Window
    {
        // Mueve el "monitor" local y sincroniza con la proyección
        private void PanMonitor(double dx, double dy)
        {
            try
            {
                if (this.FindName("MonitorTranslateTransform") is TranslateTransform mt)
                {
                    mt.X += dx;
                    mt.Y += dy;
                }
                ApplyClampMonitorTranslation();
                try
                {
                    proyeccionWindow?.UpdateImageTransform(
                        _monitorScale,
                        (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.X ?? 0,
                        (this.FindName("MonitorTranslateTransform") as TranslateTransform)?.Y ?? 0);
                }
                catch { }
            }
            catch { }
        }

        // Limita / centra la previsualización cuando no hay zoom
        private void ClampPreviewTranslation()
        {
            try
            {
                if (this.FindName("PreviewScaleTransform") is ScaleTransform pst &&
                    this.FindName("PreviewTranslateTransform") is TranslateTransform ptt)
                {
                    if (pst.ScaleX <= 1.0)
                    {
                        ptt.X = 0;
                        ptt.Y = 0;
                        return;
                    }
                    // Con zoom > 1 dejamos libertad de pan
                }
            }
            catch { }
        }

        // Aplica límites básicos al pan del monitor (cuando no haya zoom centra)
        private void ApplyClampMonitorTranslation()
        {
            try
            {
                if (this.FindName("MonitorScaleTransform") is ScaleTransform mst &&
                    this.FindName("MonitorTranslateTransform") is TranslateTransform mtt)
                {
                    if (mst.ScaleX <= 1.0)
                    {
                        mtt.X = 0;
                        mtt.Y = 0;
                        return;
                    }
                    // Con zoom > 1 no imponemos límites estrictos para permitir pan libre.
                }
            }
            catch { }
        }
    }
}