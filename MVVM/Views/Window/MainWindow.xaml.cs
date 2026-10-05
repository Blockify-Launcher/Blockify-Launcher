using BlockifyLauncher.Core.ResizeForm;
using BlockifyLauncher.MVVM.Views.Pages.Func.Setting;
using BlockifyLauncher.Properties;
using BlockifyLauncher.Resources;
using BlockifyLib.Launcher.Downloader;
using BlockifyLib.Launcher.Minecraft.Auth;
using BlockifyLib.Launcher.src;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;

namespace BlockifyLauncher
{
    public partial class MainWindow : Window
    {
        //private DiscordController _discordController;

        private Settings setting = new Settings();
        private Account? account;

        // Raised after accounts + versions finish loading — the home page
        // fills its version selector from this.
        public event Action? ShellReady;

        public MainWindow()
        {
            InitializeComponent();

            this.Width = Settings.Default.WidthProgram;
            this.Height = Settings.Default.HeightProgram;

            // blockify:// links from a second start of the exe (App single instance)
            InitExternalLinks();
        }

        private async void LoadingMainWindow(object sender, RoutedEventArgs e)
        {
            this.ProgressBarLoad.Activ = "None";
            try
            {
                await InitWebAsync();
            }
            catch (Exception ex)
            {
                // the window has no frame or buttons of its own — without the page it's a black rectangle
                // nobody can close: explain in Russian and quit (closing the window ends the process)
                LogDiag("STARTUP FAILED (WebView2) " + ex);
                App.ShowStartupProblem(
                    "Не удалось запустить интерфейс лаунчера (компонент Microsoft Edge WebView2).\n\n" + ex.Message +
                    "\n\nПерезагрузи компьютер или переустанови WebView2 Runtime. Подробности записаны в журнал:\n" + LogFile,
                    offerWebView2Download: true);
                Dispatcher.BeginInvoke(new Action(Close));
                return;
            }

            // each step on its own: a broken account file or no network must not leave the page without data
            try { await InitializeAccountsAsync(); }
            catch (Exception ex) { LogDiag("STARTUP accounts failed " + ex); }
            try { await InitializeVersionsAsync(); }
            catch (Exception ex) { LogDiag("STARTUP versions failed " + ex); }

            MinecraftAccountComboBox.SelectionChanged += MinecraftAccountSelectionChanged;
            try { setting.launcher.FileChanged += LauncherFileChanged; }
            catch (Exception ex) { LogDiag("STARTUP launcher failed " + ex); }

            try { ShellReady?.Invoke(); }
            catch (Exception ex) { LogDiag("STARTUP shell handlers failed " + ex); }
            MarkDataReady();
            ShowStartupWarningsWhenReady();
        }

        // App.StartupWarnings (unreachable Minecraft folder, damaged settings / accounts file) go to the page's
        // dialog once it has loaded; the native box only if the page never came up
        private void ShowStartupWarningsWhenReady()
        {
            int ticks = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += (_, _) =>
            {
                if (!_webReady && ++ticks < 60) return;   // wait up to 30 s for the page's "ready"
                timer.Stop();
                var warnings = App.TakeStartupWarnings();
                if (warnings.Length == 0) return;
                string text = string.Join("\n\n", warnings);
                LogDiag("startup warnings shown: " + text);
                if (_webReady && Web?.CoreWebView2 != null)
                    Post(new { type = "error", message = text });
                else
                    new MessageBox(text, MessageBox.TypeMessage.Warning).Show();
            };
            timer.Start();
        }

        // ✕ in the page, Alt+F4, «закрывать после запуска игры»: remember the window size; App ends the process
        // when this window is closed
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel) return;
            try
            {
                if (WindowState == WindowState.Normal && !WindowStateHelper.IsMaximized)
                    setting.SettingsSavingSizeForms((int)this.Width, (int)this.Height);
            }
            catch { }
        }

        // Initialize account comboBox
        private async Task InitializeAccountsAsync()
        {
            MinecraftAccountComboBox.Items.Clear();
            account = new Account();
            int index = 0;
            bool IsUser = true;
            foreach (var accountItem in account.GetAllUserArray())
            {
                MinecraftAccountComboBox.Items.Add(accountItem.Username ?? "");
                if (accountItem.Id == setting.GetLastUser() && IsUser)
                    IsUser = false;
                else if (IsUser)
                    index++;
            }

            // saved active id not found (account removed, first start): take the first account
            if (index >= MinecraftAccountComboBox.Items.Count) index = 0;
            if (MinecraftAccountComboBox.Items.Count > 0)
                MinecraftAccountComboBox.SelectedIndex = index;

            RefreshAccountCard();
        }

        // Push the active account (and the full list) into the web UI.
        public void RefreshAccountCard() => PushAccounts();

        public string ActiveAccountName =>
            MinecraftAccountComboBox.SelectedItem as string ?? "";

        // Set information for last user.
        private void MinecraftAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                ComboBox senderBox = (ComboBox)sender;
                SessionStruct sessionUsed = (account.GetAllUserArray())[senderBox.SelectedIndex];
                setting.SetLastUser(sessionUsed.Id);
                RefreshAccountCard();
            }
            catch { return; }
        }

        // Initializa version comboBox (filtered by version-type settings)
        public async Task InitializeVersionsAsync()
        {
            var filter = new Properties.Settings();
            string selected = MinecraftVerisonComboBox.SelectedIndex >= 0
                ? MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex]?.ToString() ?? ""
                : "";

            MinecraftVerisonComboBox.Items.Clear();
            var version = await setting.launcher.GetAllVersionsAsync();
            foreach (var versionItem in version)
            {
                bool show = (versionItem.Type ?? "release") switch
                {
                    "snapshot" => filter.GetShowSnapshots(),
                    "old_beta" => filter.GetShowBetas(),
                    "old_alpha" => filter.GetShowAlphas(),
                    _ => true
                };
                if (show || versionItem.IsLocalVersion)
                    MinecraftVerisonComboBox.Items.Add(versionItem.Name);
            }

            int index = string.IsNullOrEmpty(selected) ? 0 : Math.Max(0, MinecraftVerisonComboBox.Items.IndexOf(selected));
            if (MinecraftVerisonComboBox.Items.Count > 0)
                MinecraftVerisonComboBox.SelectedIndex = index;
        }

        // ── version selector data for the home page ──
        public IReadOnlyList<string> GetVersionNames()
        {
            var list = new List<string>();
            foreach (var item in MinecraftVerisonComboBox.Items)
                if (item is string s) list.Add(s);
            return list;
        }

        public int SelectedVersionIndex
        {
            get => MinecraftVerisonComboBox.SelectedIndex;
            set { if (value >= 0 && value < MinecraftVerisonComboBox.Items.Count) MinecraftVerisonComboBox.SelectedIndex = value; }
        }

        public void SelectVersion(string name)
        {
            for (int i = 0; i < MinecraftVerisonComboBox.Items.Count; i++)
                if (MinecraftVerisonComboBox.Items[i]?.ToString() == name)
                { MinecraftVerisonComboBox.SelectedIndex = i; return; }
        }

        // Aikar GC flags: community-standard JVM tuning for smooth Minecraft.
        private static readonly string[] AikarFlags =
        {
            "-XX:+UseG1GC", "-XX:+ParallelRefProcEnabled", "-XX:MaxGCPauseMillis=200",
            "-XX:+UnlockExperimentalVMOptions", "-XX:+DisableExplicitGC",
            "-XX:G1NewSizePercent=30", "-XX:G1MaxNewSizePercent=40", "-XX:G1HeapRegionSize=8M",
            "-XX:G1ReservePercent=20", "-XX:G1HeapWastePercent=5", "-XX:G1MixedGCCountTarget=4",
            "-XX:InitiatingHeapOccupancyPercent=15", "-XX:G1MixedGCLiveThresholdPercent=90",
            "-XX:G1RSetUpdatingPauseTimePercent=5", "-XX:SurvivorRatio=32",
            "-XX:+PerfDisableSharedMem", "-XX:MaxTenuringThreshold=1"
        };

        // Error message box — routed into the web UI's glass dialog when it's ready
        // so all popups share the launcher's style; native box only as a fallback.
        // Safe to call from any thread: WebView2 and WPF windows are touched on the UI thread only.
        private void HandleException(Exception ex)
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(new Action(() => HandleException(ex)));
                else LogDiag("EXCEPTION (during shutdown) " + ex);
                return;
            }

            LogDiag("EXCEPTION " + ex);
            try
            {
                if (_webReady && Web?.CoreWebView2 != null)
                {
                    Post(new { type = "error", message = ex.Message });
                    return;
                }
            }
            catch { }
            new MessageBox(ex.Message, MessageBox.TypeMessage.Error).ShowDialog();
        }

        // launcher journal: %APPDATA%\BlockifyLauncher\logs\launcher.log (rotated at 1 MB, one previous file kept);
        // one writer (Core/AppLog) shared with App's startup checks and global exception handlers
        internal static readonly string LogDir = BlockifyLauncher.Core.AppLog.LogDir;
        internal static string LogFile => BlockifyLauncher.Core.AppLog.LogFile;

        private static void LogDiag(string msg) => BlockifyLauncher.Core.AppLog.Write(msg);

        private string GameLauncherName = "BlockifyLauncher";
        private string GameLauncherVersion = "1";

        // Resolve the launch session for the selected account:
        // offline accounts as-is, Microsoft accounts get a fresh token.
        private async Task<Session> ResolveLaunchSession()
        {
            // the active account («Сделать активным») is the saved id; the combo index is only the fallback
            var all = account!.GetAllUserArray();
            string activeId = setting.GetLastUser() ?? "";
            SessionStruct selected = all.FirstOrDefault(a => a.Id == activeId)
                                     ?? all[MinecraftAccountComboBox.SelectedIndex];
            LogDiag($"launch account type={selected.UserType ?? "offline"} byId={selected.Id == activeId}");

            if (selected.UserType != "msa")
                return Session.GetOfflineSession(selected.Username ?? string.Empty);

            var loginHandler = BlockifyLib.Launcher.Microsoft.JELoginHandlerBuilder.BuildDefault();

            BlockifyLib.Launcher.XboxAuthNet.Game.Accounts.IXboxGameAccount? xboxAccount = null;
            if (!string.IsNullOrEmpty(selected.Xuid) &&
                loginHandler.AccountManager.GetAccounts().TryGetAccount(selected.Xuid, out var found))
                xboxAccount = found;

            try
            {
                return xboxAccount != null
                    ? await loginHandler.AuthenticateSilently(xboxAccount)
                    : await loginHandler.AuthenticateSilently();
            }
            catch
            {
                // Refresh token expired or missing — fall back to interactive sign-in.
                return xboxAccount != null
                    ? await loginHandler.AuthenticateInteractively(xboxAccount)
                    : await loginHandler.AuthenticateInteractively();
            }
        }

        private async Task<Process?> StartGame()
        {
            string vnDiag = MinecraftVerisonComboBox.SelectedIndex >= 0
                ? MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex]?.ToString() ?? "?"
                : "(none)";
            LogDiag($"LAUNCH version='{vnDiag}' selIdx={MinecraftVerisonComboBox.SelectedIndex} gameDir='{_packGameDir ?? "(base)"}'");

            Display display = setting.GetSettingDisplayGame();
            Session session = await ResolveLaunchSession();
            var globals = new Properties.Settings();
            string? javaPath = globals.GetJavaPath();
            if (javaPath == "javaw.exe" || !System.IO.File.Exists(javaPath)) javaPath = null;

            // per-pack overrides win over global settings (0 / empty = keep global)
            var pk = _packLaunch;
            int ramMb = pk?.RamMb > 0 ? pk.RamMb : setting.GetMemoryRAM();
            // never ask for more heap than the PC has: leave ~1.5 GB for Windows and the launcher
            long physMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            if (physMb > 0 && ramMb > physMb - 1536) ramMb = (int)Math.Max(1024, physMb - 1536);
            if (!string.IsNullOrWhiteSpace(pk?.JavaPath) && System.IO.File.Exists(pk!.JavaPath)) javaPath = pk.JavaPath;
            string[]? jvm = !string.IsNullOrWhiteSpace(pk?.JvmArgs)
                ? pk!.JvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : (globals.GetJvmAikar() || pk?.FpsBoost == true ? AikarFlags : null);   // boost forces Aikar
            int w = pk?.ScreenW > 0 ? pk.ScreenW : display.w;
            int h = pk?.ScreenH > 0 ? pk.ScreenH : display.h;
            if (pk != null) LogDiag($"pack overrides ram={ramMb} java='{javaPath}' jvm='{pk.JvmArgs}' screen={w}x{h}");

            return await setting.launcher
                .CreateProcessAsync(MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex].ToString(),
                new LaunchOption
                {
                    Session = session,
                    MaximumRamMb = ramMb,
                    JavaPath = javaPath,
                    JVMArguments = jvm,
                    GameDirectory = _packGameDir,   // isolated instance when launching a modpack

                    VersionType = this.GameLauncherName,
                    GameLauncherName = this.GameLauncherName,
                    GameLauncherVersion = this.GameLauncherVersion,

                    ScreenWidth = w,
                    ScreenHeight = h,
                    FullScreen = setting.GetFullScrean(),
                });
        }

        private bool _launching;
        public event Action<bool>? LaunchStateChanged;

        // ── running games (F008): instance folder → game process, filled on start, emptied on exit ──
        private readonly Dictionary<string, Process> _runningGames = new(StringComparer.OrdinalIgnoreCase);

        private static string GameDirKey(string dir)
        {
            try { return System.IO.Path.GetFullPath(dir).TrimEnd('\\', '/'); }
            catch { return dir.TrimEnd('\\', '/'); }
        }

        /// <summary>Is a game started by this launcher still running in this folder (pack instance or .minecraft)?</summary>
        public bool IsGameRunning(string gameDir)
        {
            if (string.IsNullOrWhiteSpace(gameDir)) return false;
            lock (_runningGames)
            {
                if (!_runningGames.TryGetValue(GameDirKey(gameDir), out var p)) return false;
                try { return !p.HasExited; } catch { return false; }
            }
        }

        // {type:'gameState', state:'starting'|'running'|'idle', slug:'<pack slug or "">'} — UI thread only
        private void PostGameState(string state, string slug)
        {
            try { Post(new { type = "gameState", state, slug }); }
            catch (Exception ex) { LogDiag("gameState post failed: " + ex.Message); }
        }

        // nothing to launch with: the page opens the accounts / versions screen instead of a native error box
        private void NotifyLaunchBlocked(string type, string fallbackText)
        {
            LogDiag("launch blocked: " + type);
            _packGameDir = null;   // a pack launch that stopped here must not leak into the next Play
            _packLaunch = null;
            if (Web?.CoreWebView2 != null) Post(new { type });
            else new MessageBox(fallbackText, MessageBox.TypeMessage.Error).ShowDialog();
        }

        // Launch the selected version. Called by the home page Play button.
        public async void LaunchSelected()
        {
            if (_launching) return;
            var lang = ResxLocalizationProvider.Instance;

            if (MinecraftAccountComboBox.SelectedIndex < 0 ||
                MinecraftAccountComboBox.Items[MinecraftAccountComboBox.SelectedIndex] is not string)
            {
                NotifyLaunchBlocked("needAccount", lang["error_no_account"]);
                return;
            }

            if (MinecraftVerisonComboBox.SelectedIndex < 0)
            {
                NotifyLaunchBlocked("needVersion", lang["error_no_version"]);
                return;
            }

            /* This code would increase download speed. */
            System.Net.ServicePointManager.DefaultConnectionLimit = 256;

            _launching = true;
            LaunchStateChanged?.Invoke(true);
            ProgressBarLoad.Activ = "Use";

            // surface file download for a normal launch in the web download panel too
            string launchVer = MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex]?.ToString() ?? "—";
            _activeJob = ("launch", "Запуск " + launchVer);
            PostJob("launch", "Запуск " + launchVer, "Проверка файлов…", 0, 0);

            var pack = _packLaunch;
            string gameSlug = pack?.Slug ?? "";
            string gameDir = _packGameDir ?? McBase();
            bool started = false, closeAfterStart = false;
            PostGameState("starting", gameSlug);

            try
            {
                var process = await StartGame();
                if (process == null)
                    throw new InvalidOperationException(lang["error_start_failed"]);

                process.Start();
                started = true;
                Post(new { type = "installDone", id = "launch", ok = true });
                PostGameState("running", gameSlug);
                lock (_runningGames) _runningGames[GameDirKey(gameDir)] = process;
                LogDiag($"game started slug='{gameSlug}' dir='{gameDir}'");

                int hideLauncher = setting.GetHideLauncher();

                // watch the game: "idle" for the page when it ends, Crash Doctor for a pack that crashed
                var launchedAt = DateTime.Now; var proc = process;
                try
                {
                    proc.EnableRaisingEvents = true;
                    proc.Exited += (_, __) =>
                    {
                        int code = 0; try { code = proc.ExitCode; } catch { }
                        Dispatcher.BeginInvoke(new Action(() => OnGameProcessExited(proc, gameDir, gameSlug, pack != null, launchedAt, code)));
                    };
                }
                catch (Exception ex) { LogDiag("cannot watch the game process: " + ex.Message); }

                setting.RegisterLaunch(launchVer);

                // Discord Rich Presence is optional — never let it break a launch
                if (setting.GetDiscordRpc())
                    try { App._discordController?.UpdateDiscordActivity("play"); } catch { }

                /*Closing the Launcher after launching minecraft.*/
                closeAfterStart = hideLauncher == 0;
            }
            catch (Exception ex)
            {
                if (!started) PostGameState("idle", gameSlug);
                // one message only: the page shows the failed launch job with its error
                if (Web?.CoreWebView2 != null)
                {
                    LogDiag("LAUNCH FAILED " + ex);
                    Post(new { type = "installDone", id = "launch", ok = false, error = ex.Message });
                }
                else HandleException(ex);
            }
            finally
            {
                ProgressBarLoad.Activ = "Сlose";
                _launching = false;
                _packGameDir = null;   // clear instance override after a launch attempt
                _packLaunch = null;
                _activeJob = null;
                LaunchStateChanged?.Invoke(false);
            }

            // «закрывать лаунчер после запуска игры»: close the window and with it the whole process (App shuts
            // down on MainWindow.Closed). The game is a separate process and keeps running. A bare Close() under
            // OnExplicitShutdown used to leave an invisible launcher process behind.
            if (closeAfterStart)
            {
                LogDiag("closing the launcher after the game started");
                Close();
            }
        }

        private void OnGameProcessExited(Process proc, string gameDir, string slug, bool isPack, DateTime launchedAt, int exitCode)
        {
            LogDiag($"game exited code={exitCode} slug='{slug}'");
            lock (_runningGames)
            {
                string key = GameDirKey(gameDir);
                if (_runningGames.TryGetValue(key, out var p) && ReferenceEquals(p, proc)) _runningGames.Remove(key);
            }
            PostGameState("idle", slug);

            // a crash while the launcher sits minimized: bring it back so the diagnosis is seen
            if (exitCode != 0 && WindowState == WindowState.Minimized)
            {
                try { WindowState = WindowState.Normal; Activate(); } catch { }
            }

            // Crash Doctor: diagnose a pack automatically when its game dies with a crash
            if (isPack) OnPackExited(slug, launchedAt, exitCode);
        }

        private void LauncherFileChanged(DownloadFileChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBarLoad.Description = e.FileName;
                ProgressBarLoad.Title = e.FileKind.ToString();
                ProgressBarLoad.Maximum = e.TotalFileCount;
                ProgressBarLoad.Value = e.ProgressedFileCount;

                // mirror into the web download panel while a background job is running
                if (_activeJob is { } job)
                    PostJob(job.id, job.title, e.FileKind.ToString(), e.ProgressedFileCount, e.TotalFileCount);
            });
        }

        private void TextChangedUserName(object sender, TextChangedEventArgs e)
        {
            /*new Properties.Settings().SetUserName(
                ((TextBox)sender).Text);*/
        }

        #region header nav
        private void MainWindowsClose(object sender, RoutedEventArgs e)
        {
            new Properties.Settings().SettingsSavingSizeForms((int)this.Width, (int)this.Height);
            Application.Current.Shutdown();
        }

        private void MainWindowsMinimals(object sender, RoutedEventArgs e) => Application.Current.MainWindow.WindowState = WindowState.Minimized;
        private void HeaderMouse(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
        #endregion

        #region WindowResizing func.
        private HwndSource _hwndSource;
        private void Window_OnSourceInitialized(object sender, EventArgs e) // call for resize effects.
            => _hwndSource = (HwndSource)PresentationSource.FromVisual(this);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]   // using *.dll
        private static extern IntPtr SendMessage
            (IntPtr hWmd, UInt32 msg, IntPtr wParam, IntPtr lParam);

        private void ResizeWindow(ResizeDirection direction)
            => SendMessage(_hwndSource.Handle, 0x112, (IntPtr)(61440 + direction), IntPtr.Zero);

        private enum ResizeDirection
        {
            Left = 1,
            Right = 2,
            Top = 3,
            TopLeft = 4,
            TopRight = 5,
            Bottom = 6,
            BottomLeft = 7,
            BottomRight = 8,
        }

        private void WindowResize_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var rectangle = (System.Windows.Shapes.Rectangle)sender;
            if (rectangle == null) return;

            if (WindowStateHelper.IsMaximized) return;

            switch (rectangle.Name)
            {
                case "WindowResizeBottom":
                    Cursor = Cursors.SizeNS;
                    ResizeWindow(ResizeDirection.Bottom);
                    break;
                case "WindowResizeLeft":
                    Cursor = Cursors.SizeWE;
                    ResizeWindow(ResizeDirection.Left);
                    break;
                case "WindowResizeRight":
                    Cursor = Cursors.SizeWE;
                    ResizeWindow(ResizeDirection.Right);
                    break;
                case "WindowResizeBottomLeft":
                    Cursor = Cursors.SizeNESW;
                    ResizeWindow(ResizeDirection.BottomLeft);
                    break;
                case "WindowResizeBottomRight":
                    Cursor = Cursors.SizeNWSE;
                    ResizeWindow(ResizeDirection.BottomRight);
                    break;
            }
        }

        private void Window_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                Cursor = Cursors.Arrow;
        }

        private void Window_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowStateHelper.SetWindowMaximized(this);
                WindowStateHelper.BlockStateChange = true;

                var screen = ScreenFinder.FindAppropriateScreen(this);
                if (screen != null)
                {
                    Top = screen.WorkingArea.Top;
                    Left = screen.WorkingArea.Left;
                    Width = screen.WorkingArea.Width;
                    Height = screen.WorkingArea.Height;
                }
            }
            else
            {
                if (WindowStateHelper.BlockStateChange)
                {
                    WindowStateHelper.BlockStateChange = false;
                    return;
                }

                WindowStateHelper.UpdateLastKnownNormalSize(Width, Height);
                WindowStateHelper.UpdateLastKnownLocation(Top, Left);
            }
        }
        #endregion.
    }
}
