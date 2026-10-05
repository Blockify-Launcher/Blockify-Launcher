using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BlockifyLauncher.Core
{
    /// <summary>
    /// Where the launcher keeps its own data. Everything is per user and independent of the exe folder and of
    /// the working directory (a launch from a blockify:// link starts in the browser's folder).
    /// </summary>
    public static class AppPaths
    {
        /// <summary>%APPDATA%\BlockifyLauncher — settings.json, account.json, skins, logs.</summary>
        public static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlockifyLauncher");

        public static string LogsDir => Path.Combine(DataDir, "logs");

        /// <summary>%LOCALAPPDATA%\BlockifyLauncher\WebView2 — WebView2 user data (cache, local storage).</summary>
        public static string WebViewDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockifyLauncher", "WebView2");

        /// <summary>%APPDATA%\.minecraft — used when the «Папка Minecraft» setting is empty or unusable.</summary>
        public static string DefaultMinecraftDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

        /// <summary>
        /// Cleans a folder typed or pasted by the user: spaces, the quotes Explorer's «Копировать как путь» adds,
        /// %VARIABLES%, trailing slashes. Empty string means "default .minecraft".
        /// </summary>
        public static string NormalizeDir(string? path)
        {
            string p = (path ?? "").Trim().Trim('"', '\'').Trim();
            if (p.Length == 0) return "";
            try { p = Environment.ExpandEnvironmentVariables(p); } catch { }
            if (p.Length > 3) p = p.TrimEnd('\\', '/');   // keep a bare drive root like "D:\"
            return p;
        }

        /// <summary>
        /// Can the game live in this folder? Needs a full path on a reachable drive and write access.
        /// Creates the folder when it doesn't exist yet. Empty = default folder = valid.
        /// </summary>
        public static bool TryValidateMinecraftDir(string path, out string error)
        {
            error = "";
            string p = NormalizeDir(path);
            if (p.Length == 0) return true;

            if (p.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                error = "в пути есть недопустимые символы";
                return false;
            }
            if (!Path.IsPathFullyQualified(p))
            {
                error = @"нужен полный путь, например D:\Games\.minecraft";
                return false;
            }
            try
            {
                string? root = Path.GetPathRoot(p);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    error = $"диск {root} недоступен";
                    return false;
                }
                Directory.CreateDirectory(p);
                string probe = Path.Combine(p, ".blockify-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "нет прав на запись в эту папку";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// The exe runs from inside %TEMP% — opened straight from a zip (Explorer «Temp1_*», 7-Zip «7zO*»,
        /// WinRAR «Rar$EX*»). Such a folder is wiped later together with everything next to the exe.
        /// </summary>
        public static bool IsRunningFromTemp()
        {
            try
            {
                string baseDir = LongPath(Path.GetFullPath(AppContext.BaseDirectory));
                foreach (string? t in new[] { Path.GetTempPath(), Environment.GetEnvironmentVariable("TEMP"), Environment.GetEnvironmentVariable("TMP") })
                {
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    string temp = LongPath(Path.GetFullPath(t)).TrimEnd('\\') + "\\";
                    if (baseDir.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return true;
                }
                foreach (string marker in new[] { @"\Rar$EX", @"\7zO", @"\Temp1_" })
                    if (baseDir.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferSize);

        // %TEMP% is often stored in 8.3 form (C:\Users\IVANOV~1\...), the exe path never is
        private static string LongPath(string path)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint n = GetLongPathName(path, sb, (uint)sb.Capacity);
                if (n > 0 && n < sb.Capacity) return sb.ToString();
            }
            catch { }
            return path;
        }
    }
}
