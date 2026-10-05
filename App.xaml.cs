using System.IO;
using BlockifyLauncher.Core;
using BlockifyLauncher.Core.DiscordActivy;
using BlockifyLauncher.Resources;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace BlockifyLauncher
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// Start order: one instance per session → the exe isn't running from inside a zip → WebView2 is installed
    /// → settings / Minecraft folder → main window. The process lives exactly as long as the main window:
    /// closing it (✕, Alt+F4, «закрывать после запуска игры») ends the process; the game is a separate process
    /// and keeps running.
    /// </summary>
    public partial class App : Application
    {
        public static DiscordController _discordController { get; private set; }

        public const string WebView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        /// <summary>Command line of this process (a blockify:// link when the browser started the launcher).</summary>
        public static string[] PendingArgs { get; private set; } = Array.Empty<string>();

        /// <summary>A later start of the exe handed over its command line. Raised on the UI thread.</summary>
        public static event Action<string[]>? ExternalArgsReceived;

        private static readonly object _warningsLock = new();
        private static readonly List<string> _startupWarnings = new();

        /// <summary>Problems found while starting (unreachable Minecraft folder, damaged settings/accounts file).</summary>
        public static string[] StartupWarnings { get { lock (_warningsLock) return _startupWarnings.ToArray(); } }

        public static void AddStartupWarning(string message)
        {
            lock (_warningsLock) _startupWarnings.Add(message);
        }

        /// <summary>Returns the warnings not shown yet and forgets them.</summary>
        public static string[] TakeStartupWarnings()
        {
            lock (_warningsLock)
            {
                var all = _startupWarnings.ToArray();
                _startupWarnings.Clear();
                return all;
            }
        }

        private static volatile bool _shuttingDown;

        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // last line of defence: log, tell the user in Russian, keep running where possible
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            PendingArgs = e.Args ?? Array.Empty<string>();
            AppLog.Info($"start {AppInfo.Version} dir='{AppContext.BaseDirectory}' args=[{string.Join(" ", PendingArgs)}]");

            // a second start only forwards its arguments (e.g. a blockify:// link) to the running launcher
            if (!SingleInstance.TryAcquire(PendingArgs)) { ExitNow(0); return; }

            if (!CheckInstallLocation()) { ExitNow(1); return; }
            if (!CheckWebView2Runtime()) { ExitNow(1); return; }

            // the UI is Russian-only for now (the WebUI has no translations), so the leftover
            // WPF strings follow it instead of the old en-US default
            try { ResxLocalizationProvider.Instance.ChangeLanguage("ru-RU"); } catch (Exception ex) { AppLog.Warn("language: " + ex.Message); }

            ValidateMinecraftDir();
            string? settingsWarning = JsonSettingsProvider.TakeWarning();
            if (settingsWarning != null) AddStartupWarning(settingsWarning);

            bool discord = false;
            try { discord = BlockifyLauncher.Properties.Settings.Default.GetDiscordRpc(); } catch { }
            if (discord) SetDiscordEnabled(true);

            SingleInstance.StartServer(args =>
            {
                if (_shuttingDown || Dispatcher.HasShutdownStarted) return false;
                Dispatcher.InvokeAsync(() => OnExternalArgs(args));
                return true;
            });

            try
            {
                var window = new BlockifyLauncher.MainWindow();
                this.MainWindow = window;
                // OnExplicitShutdown: without this a closed window left an invisible launcher process behind
                window.Closed += (_, _) => ExitNow(0);
                window.Show();
            }
            catch (Exception ex)
            {
                AppLog.Error("main window failed to open", ex);
                ShowStartupProblem("Не удалось открыть окно лаунчера.\n\n" + ex.Message +
                                   "\n\nПодробности записаны в журнал:\n" + AppLog.LogFile);
                ExitNow(1);
            }
        }

        private void ExitNow(int code)
        {
            _shuttingDown = true;
            SingleInstance.StopServer();
            if (!Dispatcher.HasShutdownStarted) Shutdown(code);
        }

        // ── second start: bring the window up and pass the arguments on ──
        private void OnExternalArgs(string[] args)
        {
            AppLog.Info($"second start, args=[{string.Join(" ", args)}]");
            if (this.MainWindow is Window w)
            {
                try
                {
                    if (!w.IsVisible) w.Show();
                    if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                    w.Activate();
                    w.Topmost = true;    // Activate alone may only flash the taskbar button
                    w.Topmost = false;
                    w.Focus();
                }
                catch (Exception ex) { AppLog.Warn("activate window: " + ex.Message); }
            }
            try { ExternalArgsReceived?.Invoke(args); }
            catch (Exception ex) { AppLog.Error("external args handler failed", ex); }
        }

        // ── environment checks (before any window exists) ──

        // started straight from the zip (WinRAR / 7-Zip / Explorer unpack just the exe into %TEMP%) or
        // unpacked without the WebUI folder: the window would be blank and everything saved would vanish
        private static bool CheckInstallLocation()
        {
            bool fromTemp = AppPaths.IsRunningFromTemp();
            bool webUiMissing = !File.Exists(Path.Combine(AppContext.BaseDirectory, "WebUI", "index.html"));
            if (!fromTemp && !webUiMissing) return true;

            AppLog.Warn($"bad install location: fromTemp={fromTemp} webUiMissing={webUiMissing} dir='{AppContext.BaseDirectory}'");
            string reason = fromTemp
                ? "Похоже, Blockify запущен прямо из архива. Так лаунчер не увидит своих файлов, а настройки и аккаунты пропадут."
                : "Рядом с BlockifyLauncher.exe нет папки WebUI с файлами интерфейса — архив распакован не полностью.";
            System.Windows.MessageBox.Show(
                reason + "\n\nРаспакуй архив в отдельную папку и запусти оттуда.",
                "Blockify", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // the whole UI is a WebView2 page: without the runtime the window would stay black and buttonless
        private static bool CheckWebView2Runtime()
        {
            string? version = null;
            Exception? error = null;
            try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (WebView2RuntimeNotFoundException) { }
            catch (Exception ex) { error = ex; }

            if (!string.IsNullOrEmpty(version))
            {
                AppLog.Info("WebView2 runtime " + version);
                return true;
            }

            if (error is DllNotFoundException or BadImageFormatException)
            {
                AppLog.Error("WebView2 loader is missing or broken", error);
                System.Windows.MessageBox.Show(
                    "Не хватает файлов лаунчера (WebView2Loader.dll) — возможно, их удалил антивирус или архив распакован не полностью.\n\n" +
                    "Распакуй архив в отдельную папку и запусти оттуда.",
                    "Blockify", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (error != null)
            {
                // unknown failure of the check itself: let the window try, it reports its own errors
                AppLog.Warn("WebView2 runtime check failed: " + error.Message);
                return true;
            }

            AppLog.Warn("WebView2 runtime is not installed");
            ShowStartupProblem(
                "Для работы Blockify нужен компонент Microsoft Edge WebView2 Runtime, а на этом компьютере его нет.\n\n" +
                "Он бесплатный и ставится за минуту с сайта Microsoft. После установки запусти лаунчер снова.",
                offerWebView2Download: true);
            return false;
        }

        /// <summary>
        /// Native message for problems that leave no usable launcher window (WebView2 missing or broken).
        /// With <paramref name="offerWebView2Download"/> the user can open the WebView2 download page.
        /// </summary>
        public static void ShowStartupProblem(string message, bool offerWebView2Download = false)
        {
            try
            {
                if (!offerWebView2Download)
                {
                    System.Windows.MessageBox.Show(message, "Blockify", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var answer = System.Windows.MessageBox.Show(
                    message + "\n\nОткрыть страницу загрузки WebView2?\n" + WebView2DownloadUrl,
                    "Blockify", MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (answer == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(WebView2DownloadUrl) { UseShellExecute = true });
            }
            catch (Exception ex) { AppLog.Warn("startup message failed: " + ex.Message); }
        }

        // a folder on an unplugged drive or a pasted path in quotes used to brick the launcher on every start
        private static void ValidateMinecraftDir()
        {
            try
            {
                var s = BlockifyLauncher.Properties.Settings.Default;
                string raw = s.MinecraftDir ?? "";
                string dir = AppPaths.NormalizeDir(raw);
                if (dir.Length == 0) return;

                if (AppPaths.TryValidateMinecraftDir(dir, out string error))
                {
                    if (dir != raw) s.MinecraftDir = dir;   // store the cleaned path (no quotes / spaces)
                    return;
                }

                BlockifyLauncher.Properties.Settings.MinecraftDirFallback = true;
                AppLog.Warn($"Minecraft folder '{dir}' is unusable ({error}), using the default {AppPaths.DefaultMinecraftDir} this session");
                AddStartupWarning($"Папка Minecraft «{dir}» недоступна: {error}. Пока используется стандартная папка " +
                                  $"{AppPaths.DefaultMinecraftDir}. Подключи диск или выбери другую папку в настройках.");
            }
            catch (Exception ex)
            {
                AppLog.Error("Minecraft folder check failed", ex);
            }
        }

        // ── global exception handlers ──
        private static bool _inErrorDialog;
        private static int _uiErrorCount;
        private static DateTime _uiErrorWindowStart = DateTime.MinValue;

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            AppLog.Error("UNHANDLED (UI thread)", e.Exception);

            // the same error firing over and over (layout / render loop): stop instead of an endless stream of dialogs
            var now = DateTime.Now;
            if (now - _uiErrorWindowStart > TimeSpan.FromMinutes(1)) { _uiErrorWindowStart = now; _uiErrorCount = 0; }
            bool tooMany = ++_uiErrorCount > 5;

            bool windowAlive = this.MainWindow is { IsVisible: true } && !tooMany;
            if (!_inErrorDialog)
            {
                _inErrorDialog = true;
                try
                {
                    string text = windowAlive
                        ? "Что-то пошло не так, но лаунчер продолжит работу.\n\n"
                        : "Лаунчер столкнулся с ошибкой и будет закрыт.\n\n";
                    var answer = System.Windows.MessageBox.Show(
                        text + e.Exception.Message + "\n\nПодробности записаны в журнал:\n" + AppLog.LogFile +
                        "\n\nОткрыть папку с журналом?",
                        "Blockify — ошибка", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (answer == MessageBoxResult.Yes) OpenLogFolder();
                }
                catch { }
                finally { _inErrorDialog = false; }
            }

            if (!windowAlive) ExitNow(1);
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            AppLog.Error("UNHANDLED (" + (e.IsTerminating ? "fatal" : "non-fatal") + ")", e.ExceptionObject as Exception);
            if (!e.IsTerminating) return;
            // an exception on a background thread can't be survived in .NET — at least say what happened
            try
            {
                string msg = (e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject?.ToString() ?? "";
                System.Windows.MessageBox.Show(
                    "Лаунчер столкнулся с критической ошибкой и будет закрыт.\n\n" + msg +
                    "\n\nПодробности записаны в журнал:\n" + AppLog.LogFile,
                    "Blockify — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        }

        // a failed fire-and-forget task: nothing to show the user, but it belongs in the journal
        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLog.Error("UNOBSERVED task exception", e.Exception);
            e.SetObserved();
        }

        private static void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(AppLog.LogDir);
                Process.Start(new ProcessStartInfo(AppLog.LogDir) { UseShellExecute = true });
            }
            catch { }
        }

        // Discord Rich Presence toggle (settings page applies it live).
        public static void SetDiscordEnabled(bool enabled)
        {
            try
            {
                if (enabled)
                    _discordController ??= new DiscordController();
                else
                {
                    _discordController?.Stop();
                    _discordController = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _shuttingDown = true;
            base.OnExit(e);
            try { _discordController?.Stop(); } catch { }
            SingleInstance.Release();
            AppLog.Info("exit code=" + e.ApplicationExitCode);
        }
    }

}
