using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace TMRJW
{
    public partial class BrowserWindow
    {
        // Compatibilidad: exponer el callback para que MainWindow lo registre
        public void SetImageSelectedCallback(Action<BitmapImage> callback) => _onImageSelected = callback;

        // Compatibilidad: handler simple para el botón "Ir" en la barra de URL (si el XAML lo referencia)
        private void BtnGo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var url = UrlBox.Text?.Trim();
                if (!string.IsNullOrEmpty(url))
                    WebBrowserControl.Navigate(url);
            }
            catch { }
        }

        // Obtener el ListBox de la pestaña activa (si existe)
        private ListBox? GetActiveListBox()
        {
            try
            {
                if (InventoryTabs == null) return null;
                if (InventoryTabs.SelectedItem is TabItem ti)
                {
                    // En EnsureTabExists colocamos un Border cuyo Child es el ListBox
                    if (ti.Content is Border b && b.Child is ListBox lb) return lb;
                    // Si directamente es ListBox:
                    if (ti.Content is ListBox lb2) return lb2;
                }
            }
            catch { }
            return null;
        }

        // Propiedad de conveniencia para compatibilidad con código previo que refería ImagesListBox
        private ListBox? ImagesListBox => GetActiveListBox();

        // SelectionChanged handler usado por cada ListBox creado dinámicamente
        private void ImagesListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                var lb = sender as ListBox ?? ImagesListBox;
                var selected = lb?.SelectedItem;
                if (selected is BitmapImage bi)
                {
                    PreviewMedia.Visibility = Visibility.Collapsed;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewImage.Source = bi;
                    TxtPreviewInfo.Text = "Vista previa: imagen seleccionada (single click)";
                    try { BtnResetZoom_Click(null, null); } catch { }
                }
                else if (selected is CachedImage ci)
                {
                    PreviewMedia.Visibility = Visibility.Collapsed;
                    PreviewImage.Visibility = Visibility.Visible;
                    PreviewImage.Source = ci.Image;
                    TxtPreviewInfo.Text = $"Vista previa: {Path.GetFileName(ci.FilePath)}";
                    try { BtnResetZoom_Click(null, null); } catch { }
                }
                else if (selected is string s && File.Exists(s))
                {
                    PreviewImage.Visibility = Visibility.Collapsed;
                    PreviewMedia.Visibility = Visibility.Visible;
                    try { PreviewMedia.Source = new Uri(s); } catch { PreviewMedia.Source = null; }
                    TxtPreviewInfo.Text = $"Vista previa: vídeo {Path.GetFileName(s)}";
                }
                else
                {
                    PreviewMedia.Stop();
                    PreviewMedia.Source = null;
                    PreviewMedia.Visibility = Visibility.Collapsed;
                    PreviewImage.Source = null;
                    PreviewImage.Visibility = Visibility.Visible;
                    TxtPreviewInfo.Text = "Imagen / Video";
                }
            }
            catch { }
        }

        // Doble click -> proyectar
        private void ImagesListBox_MouseDoubleClick(object? sender, MouseButtonEventArgs e)
        {
            TryProjectSelectedItem();
        }

        // Enter -> proyectar
        private void ImagesListBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TryProjectSelectedItem();
                e.Handled = true;
            }
        }

        // Centraliza la lógica de proyectar el elemento seleccionado en la lista activa
        private void TryProjectSelectedItem()
        {
            try
            {
                var lb = ImagesListBox;
                if (lb == null) return;
                var selected = lb.SelectedItem;
                if (selected == null) return;

                BitmapImage? toProject = null;
                if (selected is BitmapImage bi) toProject = bi;
                else if (selected is CachedImage ci) toProject = ci.Image;
                else if (selected is string path && File.Exists(path))
                {
                    try { toProject = LoadBitmapFromFileCached(path); } catch { }
                }

                if (toProject != null)
                {
                    try { _onImageSelected?.Invoke(toProject); } catch { }
                }
            }
            catch { }
        }
    }
}