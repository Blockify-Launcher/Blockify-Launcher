using System.IO;
namespace BlockifyLauncher.Core
{
    /// <summary>
    /// Launcher journal: %APPDATA%\BlockifyLauncher\logs\launcher.log (rotated at 1 MB, one previous file kept).
    /// The first line of every session says which build / OS / RAM the report came from.
    /// Shared by the window (MainWindow.LogDiag) and the startup code / global exception handlers in App.
    /// </summary>
    public static class AppLog
    {
        public static string LogDir => AppPaths.LogsDir;
        public static string LogFile => Path.Combine(LogDir, "launcher.log");

        private static readonly object _lock = new();
        private static bool _headerDone;

        public static void Info(string message) => Write(message);
        public static void Warn(string message) => Write("WARN " + message);
        public static void Error(string message, Exception? ex = null)
            => Write("ERROR " + message + (ex != null ? " " + ex : ""));

        // never throws: a log that can't be written must not take the launcher down with it
        public static void Write(string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(LogDir);
                    if (!_headerDone)
                    {
                        _headerDone = true;
                        var fi = new FileInfo(LogFile);
                        if (fi.Exists && fi.Length > 1_000_000)
                            File.Move(LogFile, Path.Combine(LogDir, "launcher.1.log"), overwrite: true);
                        long ramMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                        File.AppendAllText(LogFile,
                            $"\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} Blockify {AppInfo.Version} · " +
                            $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} · " +
                            $".NET {Environment.Version} · RAM {ramMb} MB =====\n");
                    }
                    File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + "\n");
                }
            }
            catch { }
        }
    }
}
