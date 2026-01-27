using System;
using System.IO;

namespace WINHOME
{
    internal static class Logger
    {
        private static readonly string LogPath;

        static Logger()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, "MyLauncher");
                Directory.CreateDirectory(dir);
                LogPath = Path.Combine(dir, "logs.txt");
            }
            catch
            {
                LogPath = string.Empty;
            }
        }

        public static void Log(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(LogPath)) return;
                File.AppendAllText(LogPath, DateTime.Now.ToString("o") + " " + text + Environment.NewLine);
            }
            catch { }
        }
    }
}
