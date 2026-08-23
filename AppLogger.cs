using System;
using System.IO;
using System.Text;

namespace SKD750Control
{
    public static class AppLogger
    {
        private static readonly object _sync = new object();
        private static string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");

        public static void SetLogFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            _logFilePath = path;
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);
        public static void Error(string message, Exception ex) => Write("ERROR", message + " | " + ex.Message);

        private static void Write(string level, string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                lock (_sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath) ?? AppDomain.CurrentDomain.BaseDirectory);
                    File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* avoid throwing from logger */ }
        }
    }
}
