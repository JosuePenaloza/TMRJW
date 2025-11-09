using System.Configuration;
using System.Data;
using System.Windows;

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
            try
            {
                var settings = SettingsHelper.Load();
                var dict = new ResourceDictionary();
                dict.Source = new System.Uri(settings.IsDarkTheme ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", System.UriKind.Relative);
                this.Resources.MergedDictionaries.Add(dict);
            }
            catch { }
        }
    }
}
