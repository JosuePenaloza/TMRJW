using System;
using System.IO;
using System.Text.Json;

namespace TMRJW
{
    internal static class SettingsHelper
    {
        internal sealed class AppSettings
        {
            public string? SelectedMonitorDeviceName { get; set; }
            public string? ImagenTextoAnio { get; set; }
            public string? FfmpegPath { get; set; }
            public bool IsDarkTheme { get; set; } = true;
            // Default to floating projection window as requested
            public bool UseFloatingProjectionWindow { get; set; } = true;
        }

        private static string GetPath()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(local, "TMRJW");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }

        public static AppSettings Load()
        {
            try
            {
                var p = GetPath();
                if (!File.Exists(p)) return new AppSettings();
                var json = File.ReadAllText(p);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                return s ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save(AppSettings s)
        {
            var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GetPath(), json);
        }
    }
}