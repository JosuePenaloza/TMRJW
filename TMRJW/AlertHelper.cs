using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TMRJW
{
    public static class AlertHelper
    {
        public static void ShowSilentInfo(Window? owner, string message, string title = "Información")
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var w = new SilentAlertWindow(message, title);
                        if (owner != null) w.Owner = owner;
                        w.ShowDialog();
                    }
                    catch { }
                });
            }
            catch { }
        }

        public static bool ShowSilentConfirm(Window? owner, string message, string title = "Confirmar")
        {
            try
            {
                var result = false;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Crear ventana simple de confirmación sin sonido
                        var win = new Window
                        {
                            Title = title,
                            Width = 420,
                            Height = 160,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            ResizeMode = ResizeMode.NoResize,
                            WindowStyle = WindowStyle.ToolWindow,
                            Background = Brushes.White
                        };

                        var grid = new Grid { Margin = new Thickness(12) };
                        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        var txt = new TextBlock
                        {
                            Text = message,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(4),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetRow(txt, 0);
                        grid.Children.Add(txt);

                        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4) };
                        var btnYes = new Button { Content = "Sí", Width = 80, Margin = new Thickness(6) };
                        var btnNo = new Button { Content = "No", Width = 80, Margin = new Thickness(6) };

                        btnYes.Click += (s, e) => { win.DialogResult = true; win.Close(); };
                        btnNo.Click += (s, e) => { win.DialogResult = false; win.Close(); };

                        panel.Children.Add(btnYes);
                        panel.Children.Add(btnNo);

                        Grid.SetRow(panel, 1);
                        grid.Children.Add(panel);

                        win.Content = grid;
                        if (owner != null) win.Owner = owner;

                        var dlgRes = win.ShowDialog();
                        result = dlgRes == true;
                    }
                    catch { result = false; }
                });
                return result;
            }
            catch { return false; }
        }
    }
}
