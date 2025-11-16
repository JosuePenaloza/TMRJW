using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TMRJW
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Register global handlers to capture crashes and help diagnostics
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            try
            {
                var settings = SettingsHelper.Load();
                var dict = new ResourceDictionary();
                dict.Source = new System.Uri(settings.IsDarkTheme ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", System.UriKind.Relative);
                this.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                LogException(ex, "OnStartup.ThemeLoad");
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                LogException(e.Exception, "DispatcherUnhandledException");
                MessageBox.Show($"Se ha producido un error inesperado. Se ha guardado el registro en %LOCALAPPDATA%\\TMRJW\\crash.log.\n\nMensaje:\n{e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            // terminate
            Environment.Exit(1);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown");
                LogException(ex, "CurrentDomain_UnhandledException");
            }
            catch { }
        }

        private static void LogException(Exception ex, string context)
        {
            try
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(local, "TMRJW");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "crash.log");
                var txt = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n{ex}\n\n";
                File.AppendAllText(path, txt);
            }
            catch { }
        }
    }
}
