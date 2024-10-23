using BlockifyLib.Launcher.Downloader;
using BlockifyLib.Launcher.Minecraft.Auth;
using BlockifyLib.Launcher.src;

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;

using BlockifyLauncher.MVVM.Views.Pages.Func.Setting;
using BlockifyLauncher.Properties;
using BlockifyLauncher.Core.DiscordActivy;
using System.Drawing;
using System.Security.Cryptography.Xml;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Forms.VisualStyles;
using BlockifyLauncher.Core.ResizeForm;

namespace BlockifyLauncher
{
    public partial class MainWindow : Window
    {
        private DiscordController _discordController;

        public Border MainBorder { get; set; }

        private Settings setting = new Settings();
        private Account? account;

        public MainWindow()
        {
            InitializeComponent();
            this.Resources.Add("WindowTitle", this.Title);

            this.homeRadioButton.IsChecked = true;

            this.MainBorder = InnerBlurContainer;

            this.Width  = Settings.Default.WidthProgram;
            this.Height = Settings.Default.HeightProgram;
        }
        
        private async void LoadingMainWindow(object sender, RoutedEventArgs e)
        {
            // initialization component discord.
            _discordController = new DiscordController();

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
                MinecraftAccountComboBox.Text = "None...";

            MinecraftAccountComboBox.Items.Add(new Separator() {
                Style = (Style)Application.Current.FindResource("MenuSeparatorStyle")
            });

            var navButton_account = new Button()
            {
                Style = (Style)Application.Current.FindResource("ButtonComboBoxAccount"),
                Content = "Account Setting"
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

        // Initializa version comboBox
        private async Task InitializeVersionsAsync()
        {
            MinecraftVerisonComboBox.Items.Clear();
            var version = await setting.launcher.GetAllVersionsAsync();
            foreach (var versionItem in version)
                MinecraftVerisonComboBox.Items.Add(versionItem.Name);
            MinecraftVerisonComboBox.SelectedIndex = 0;
        }

        // Error message box.
        private void HandleException(Exception ex) =>
            new MessageBox(ex.Message, MessageBox.TypeMessage.Error).ShowDialog();

        private string GameLauncherName = "BlockifyLauncher";
        private string GameLauncherVersion = "1";
        private async Task<Process> StartGame()
        {
            Display display = setting.GetSettingDisplayGame();
            Process process = await setting.launcher
                .CreateProcessAsync(MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex].ToString(),
                new LaunchOption
                {
                    Session = Session.GetOfflineSession(MinecraftAccountComboBox.Items[MinecraftAccountComboBox.SelectedIndex].ToString()),
                    MaximumRamMb = setting.GetMemoryRAM(),

                    VersionType = this.GameLauncherName,
                    GameLauncherName = this.GameLauncherName,
                    GameLauncherVersion = this.GameLauncherVersion,

                    ScreenWidth     =   display.w,
                    ScreenHeight    =   display.h,
                    FullScreen = setting.GetFullScrean(),
                });
            return process ?? new Process();
        }

        private async void ButtonClickStartGame(object sender, RoutedEventArgs e)
        {
            /* This code would increase download speed. */
            System.Net.ServicePointManager.DefaultConnectionLimit = 256;

            ProgressBarLoad.Activ = "Use";

            //var processUtil = new ProcessUtil(await StartGame());
            //processUtil.StartWithEvents();
            var process = await StartGame();
            process.Start();

            ProgressBarLoad.Activ = "Сlose";

            _discordController.UpdateStartGame(MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex].ToString());

            /*Closing the Launcher after launching minecraft.*/
            if (new Properties.Settings().GetHideLauncher() == 0)
                this.Close();
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
