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
        }

        private async void LoadingMainWindow(object sender, RoutedEventArgs e)
        {
            this.ProgressBarLoad.Activ = "None";
            try
            {
                await InitWebAsync();

                await InitializeAccountsAsync();
                await InitializeVersionsAsync();

                MinecraftAccountComboBox.SelectionChanged += MinecraftAccountSelectionChanged;
                setting.launcher.FileChanged += LauncherFileChanged;

                ShellReady?.Invoke();
                MarkDataReady();
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "blockify_err.txt"), ex.ToString()); } catch { }
                HandleException(ex);
            }
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
                MinecraftAccountComboBox.Items.Add(accountItem.Username);
                if (accountItem.Id == setting.GetLastUser() && IsUser)
                    IsUser = false;
                else if (IsUser)
                    index++;
            }

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
            "-XX:+UnlockExperimentalVMOptions", "-XX:+DisableExplicitGC", "-XX:+AlwaysPreTouch",
            "-XX:G1NewSizePercent=30", "-XX:G1MaxNewSizePercent=40", "-XX:G1HeapRegionSize=8M",
            "-XX:G1ReservePercent=20", "-XX:G1HeapWastePercent=5", "-XX:G1MixedGCCountTarget=4",
            "-XX:InitiatingHeapOccupancyPercent=15", "-XX:G1MixedGCLiveThresholdPercent=90",
            "-XX:G1RSetUpdatingPauseTimePercent=5", "-XX:SurvivorRatio=32",
            "-XX:+PerfDisableSharedMem", "-XX:MaxTenuringThreshold=1"
        };

        // Error message box — routed into the web UI's glass dialog when it's ready
        // so all popups share the launcher's style; native box only as a fallback.
        private void HandleException(Exception ex)
        {
            LogDiag("EXCEPTION " + ex);
            try
            {
                if (_webReady && Web?.CoreWebView2 != null)
                {
                    if (Dispatcher.CheckAccess()) Post(new { type = "error", message = ex.Message });
                    else Dispatcher.Invoke(() => Post(new { type = "error", message = ex.Message }));
                    return;
                }
            }
            catch { }
            new MessageBox(ex.Message, MessageBox.TypeMessage.Error).ShowDialog();
        }

        // append a diagnostic line to %TEMP%/blockify_err.txt (for debugging launch issues)
        private static void LogDiag(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "blockify_err.txt"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\n");
            }
            catch { }
        }

        private string GameLauncherName = "BlockifyLauncher";
        private string GameLauncherVersion = "1";

        // Resolve the launch session for the selected account:
        // offline accounts as-is, Microsoft accounts get a fresh token.
        private async Task<Session> ResolveLaunchSession()
        {
            SessionStruct selected = account!.GetAllUserArray()[MinecraftAccountComboBox.SelectedIndex];

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
            string javaPath = new Properties.Settings().GetJavaPath();
            return await setting.launcher
                .CreateProcessAsync(MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex].ToString(),
                new LaunchOption
                {
                    Session = session,
                    MaximumRamMb = setting.GetMemoryRAM(),
                    JavaPath = javaPath != "javaw.exe" && System.IO.File.Exists(javaPath) ? javaPath : null,
                    JVMArguments = new Properties.Settings().GetJvmAikar() ? AikarFlags : null,
                    GameDirectory = _packGameDir,   // isolated instance when launching a modpack

                    VersionType = this.GameLauncherName,
                    GameLauncherName = this.GameLauncherName,
                    GameLauncherVersion = this.GameLauncherVersion,

                    ScreenWidth = display.w,
                    ScreenHeight = display.h,
                    FullScreen = setting.GetFullScrean(),
                });
        }

        private bool _launching;
        public event Action<bool>? LaunchStateChanged;

        // Launch the selected version. Called by the home page Play button.
        public async void LaunchSelected()
        {
            if (_launching) return;
            var lang = ResxLocalizationProvider.Instance;

            if (MinecraftVerisonComboBox.SelectedIndex < 0)
            {
                new MessageBox(lang["error_no_version"], MessageBox.TypeMessage.Error).ShowDialog();
                return;
            }

            if (MinecraftAccountComboBox.SelectedIndex < 0 ||
                MinecraftAccountComboBox.Items[MinecraftAccountComboBox.SelectedIndex] is not string)
            {
                new MessageBox(lang["error_no_account"], MessageBox.TypeMessage.Error).ShowDialog();
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

            try
            {
                var process = await StartGame();
                if (process == null)
                    throw new InvalidOperationException(lang["error_start_failed"]);

                process.Start();
                Post(new { type = "installDone", id = "launch", ok = true });

                new Properties.Settings().RegisterLaunch(
                    MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex].ToString() ?? "");

                // Discord Rich Presence is optional — never let it break a launch
                if (new Properties.Settings().GetDiscordRpc())
                    try { App._discordController?.UpdateDiscordActivity("play"); } catch { }

                /*Closing the Launcher after launching minecraft.*/
                if (new Properties.Settings().GetHideLauncher() == 0)
                    this.Close();
            }
            catch (Exception ex)
            {
                Post(new { type = "installDone", id = "launch", ok = false, error = ex.Message });
                HandleException(ex);
            }
            finally
            {
                ProgressBarLoad.Activ = "Сlose";
                _launching = false;
                _packGameDir = null;   // clear instance override after a launch attempt
                _activeJob = null;
                LaunchStateChanged?.Invoke(false);
            }
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
