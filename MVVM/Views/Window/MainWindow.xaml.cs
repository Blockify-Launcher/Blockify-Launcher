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

        public Border MainBorder { get; set; }

        private Settings setting = new Settings();
        private Account? account;

        public MainWindow()
        {
            InitializeComponent();
            this.Resources.Add("WindowTitle", this.Title);

            this.homeRadioButton.IsChecked = true;

            this.MainBorder = InnerBlurContainer;

            this.Width = Settings.Default.WidthProgram;
            this.Height = Settings.Default.HeightProgram;
        }

        private async void LoadingMainWindow(object sender, RoutedEventArgs e)
        {
            ApplyVibeBackground();
            ApplyParadeSetting();
            PlayDockAssembly();

            this.ProgressBarLoad.Activ = "None";
            try
            {
                await InitializeAccountsAsync();
                await InitializeVersionsAsync();

                MinecraftAccountComboBox.SelectionChanged += MinecraftAccountSelectionChanged;
                setting.launcher.FileChanged += LauncherFileChanged;
            }
            catch (Exception ex)
            {
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
            else
                MinecraftAccountComboBox.Text = ResxLocalizationProvider.Instance["none_account"];

            MinecraftAccountComboBox.Items.Add(new Separator()
            {
                Style = (Style)Application.Current.FindResource("MenuSeparatorStyle")
            });

            var navButton_account = new Button()
            {
                Style = (Style)Application.Current.FindResource("ButtonComboBoxAccount"),
                Content = ResxLocalizationProvider.Instance["account_setting"]
            };
            navButton_account.SetBinding(Button.CommandProperty, new Binding("AccountCommand"));

            MinecraftAccountComboBox.Items.Add(navButton_account);
        }

        // Set information for last user.
        private void MinecraftAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                ComboBox senderBox = (ComboBox)sender;
                SessionStruct sessionUsed = (account.GetAllUserArray())[senderBox.SelectedIndex];
                setting.SetLastUser(sessionUsed.Id);
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

        // One-click relaunch of the last played version (quick-row "Continue").
        public void QuickLaunchLast()
        {
            string last = new Properties.Settings().GetLastVersion();
            if (string.IsNullOrEmpty(last)) return;

            for (int i = 0; i < MinecraftVerisonComboBox.Items.Count; i++)
                if (MinecraftVerisonComboBox.Items[i]?.ToString() == last)
                {
                    MinecraftVerisonComboBox.SelectedIndex = i;
                    break;
                }

            ButtonClickStartGame(Start, new RoutedEventArgs());
        }

        // Diorama on the dock: baked pixel art + four kinds of living particles.
        private void DockSizeChanged(object sender, SizeChangedEventArgs e) => RebuildDockDiorama();

        private void RebuildDockDiorama()
        {
            int w = (int)DockBorder.ActualWidth;
            if (w < 200) return;

            var art = Core.Vibe.DockDiorama.Render(w);
            DockDecorImage.Source = art.Bitmap;
            System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                DockDecorImage, System.Windows.Media.BitmapScalingMode.NearestNeighbor);

            var fx = DockFxCanvas;
            fx.Children.Clear();
            var rnd = new Random(42);

            System.Windows.Shapes.Ellipse Dot(double size, System.Windows.Media.Color color, double glow)
            {
                var el = new System.Windows.Shapes.Ellipse
                {
                    Width = size, Height = size,
                    Fill = new System.Windows.Media.SolidColorBrush(color),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { BlurRadius = glow, ShadowDepth = 0, Color = color, Opacity = 0.85 }
                };
                return el;
            }

            void Twinkle(System.Windows.Shapes.Ellipse el, double x, double y, double durSec)
            {
                Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
                fx.Children.Add(el);
                el.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0.06, 0.95, TimeSpan.FromSeconds(durSec))
                    {
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        AutoReverse = true,
                        BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * 4)
                    });
            }

            // sculk souls (left zone)
            for (int i = 0; i < 5; i++)
                Twinkle(Dot(3, System.Windows.Media.Color.FromRgb(0x3D, 0xE8, 0xFF), 6),
                    w * (0.01 + rnd.NextDouble() * 0.24), 38 + rnd.NextDouble() * 52, 2.4 + rnd.NextDouble() * 2);

            // amethyst glints
            for (int i = 0; i < 3; i++)
                Twinkle(Dot(3, System.Windows.Media.Color.FromRgb(0xE3, 0xD0, 0xFF), 7),
                    w * (0.315 + rnd.NextDouble() * 0.09), 32 + rnd.NextDouble() * 58, 3.5 + rnd.NextDouble() * 3);

            // glow berry pulse at vine tips
            foreach (var tip in art.BerryTips)
                Twinkle(Dot(4, System.Windows.Media.Color.FromRgb(0xFF, 0xB0, 0x2E), 8),
                    tip.X - 1, tip.Y, 2 + rnd.NextDouble() * 2);

            // spore blossom particles drifting down
            for (int i = 0; i < 8; i++)
            {
                var spore = Dot(3, rnd.NextDouble() < 0.7
                    ? System.Windows.Media.Color.FromRgb(0xF2, 0xA1, 0xC0)
                    : System.Windows.Media.Color.FromRgb(0xCD, 0xEA, 0x7A), 3);
                double sx = art.SporeOrigin.X - 10 + rnd.NextDouble() * 26;
                Canvas.SetLeft(spore, sx); Canvas.SetTop(spore, art.SporeOrigin.Y);
                fx.Children.Add(spore);

                var move = new System.Windows.Media.TranslateTransform();
                spore.RenderTransform = move;
                double dur = 5 + rnd.NextDouble() * 4;
                var begin = TimeSpan.FromSeconds(-rnd.NextDouble() * 8);
                var forever = System.Windows.Media.Animation.RepeatBehavior.Forever;

                move.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 72, TimeSpan.FromSeconds(dur))
                    { RepeatBehavior = forever, BeginTime = begin });
                move.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 14, TimeSpan.FromSeconds(dur))
                    { RepeatBehavior = forever, BeginTime = begin });

                var fade = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
                { Duration = TimeSpan.FromSeconds(dur), RepeatBehavior = forever, BeginTime = begin };
                fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0,
                    System.Windows.Media.Animation.KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.9,
                    System.Windows.Media.Animation.KeyTime.FromPercent(0.1)));
                fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.7,
                    System.Windows.Media.Animation.KeyTime.FromPercent(0.85)));
                fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0,
                    System.Windows.Media.Animation.KeyTime.FromPercent(1)));
                spore.BeginAnimation(OpacityProperty, fade);
            }
        }

        // Dock "assembly" entrance: the panel rises, then account → version →
        // Start drop onto it one by one, like items into a crafting grid.
        private void PlayDockAssembly()
        {
            var ease = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            };

            var dockMove = new System.Windows.Media.TranslateTransform();
            DockBorder.RenderTransform = dockMove;
            DockBorder.Opacity = 0;
            DockBorder.BeginAnimation(OpacityProperty,
                new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
            dockMove.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(40, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = ease });

            void DropPart(FrameworkElement part, double delaySec)
            {
                var move = new System.Windows.Media.TranslateTransform();
                part.RenderTransform = move;
                part.Opacity = 0;

                var begin = TimeSpan.FromSeconds(delaySec);
                part.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.16)) { BeginTime = begin });

                var y = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { BeginTime = begin };
                y.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(-26,
                    System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
                y.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(4,
                    System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.2)), ease));
                y.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(0,
                    System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.32)), ease));
                move.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, y);
            }

            DropPart(AccountField, 0.22);
            DropPart(VersionField, 0.34);
            DropPart(Start, 0.48);
        }

        // Mob parade on/off from settings, applied live.
        public void ApplyParadeSetting()
        {
            ParadeCanvas.Children.Clear();
            if (new Properties.Settings().GetShowParade())
                Core.Vibe.MobParade.Start(ParadeCanvas);
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

        // Pixel-art vibe background: from settings, "auto" = by time of day.
        public void ApplyVibeBackground()
        {
            try
            {
                var kind = new Properties.Settings().GetVibe()?.ToLowerInvariant() switch
                {
                    "ocean" => Core.Vibe.VibeKind.Ocean,
                    "sunset" => Core.Vibe.VibeKind.Sunset,
                    "nether" => Core.Vibe.VibeKind.Nether,
                    "cherry" => Core.Vibe.VibeKind.Cherry,
                    "end" => Core.Vibe.VibeKind.End,
                    _ => Core.Vibe.VibeScene.ForNow()
                };
                var scene = Core.Vibe.VibeScene.Render(kind);
                var brush = new System.Windows.Media.ImageBrush(scene)
                {
                    Stretch = System.Windows.Media.Stretch.UniformToFill
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    brush, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
                InnerBlurContainer.Background = brush;
            }
            catch
            {
                // keep the default wallpaper from WindowBorderStyle
            }
        }

        // Error message box.
        private void HandleException(Exception ex) =>
            new MessageBox(ex.Message, MessageBox.TypeMessage.Error).ShowDialog();

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

                    VersionType = this.GameLauncherName,
                    GameLauncherName = this.GameLauncherName,
                    GameLauncherVersion = this.GameLauncherVersion,

                    ScreenWidth = display.w,
                    ScreenHeight = display.h,
                    FullScreen = setting.GetFullScrean(),
                });
        }

        private async void ButtonClickStartGame(object sender, RoutedEventArgs e)
        {
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

            Start.IsEnabled = false;
            ProgressBarLoad.Activ = "Use";

            try
            {
                var process = await StartGame();
                if (process == null)
                    throw new InvalidOperationException(lang["error_start_failed"]);

                process.Start();

                new Properties.Settings().RegisterLaunch(
                    MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex].ToString() ?? "");

                App._discordController?.UpdateDiscordActivity("play");

                /*Closing the Launcher after launching minecraft.*/
                if (new Properties.Settings().GetHideLauncher() == 0)
                    this.Close();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                ProgressBarLoad.Activ = "Сlose";
                Start.IsEnabled = true;
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
