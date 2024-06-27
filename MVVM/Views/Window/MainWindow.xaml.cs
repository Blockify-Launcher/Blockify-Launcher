﻿using BlockifyLib.Launcher.Downloader;
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
using BlockifyLauncher.Core;

namespace BlockifyLauncher
{
    public partial class MainWindow : Window
    {
        private DiscordController _discordController;

        public Border MainBorder { get; set; }

        private Settings setting = new Settings();
        private Account account;

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
            this.ProgressBarLoad.Activ = "None";
            try
            {
                await InitializeAccountsAsync(); 
                await InitializeVersionsAsync();

                MinecraftAccountComboBox.SelectionChanged += MinecraftAccountSelectionChanged;
                setting.launcher.FileChanged += LauncherFileChanged;

                _discordController = new DiscordController();
                _discordController.Start();
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

        // Resize mainWindow Form
        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            Thumb thumb = sender as Thumb;
            if (thumb != null)
            {
                double newWidth = this.Width;
                double newHeight = this.Height;

                try
                {
                    switch (thumb.Name)
                    {
                        case "LeftThumb":
                            if (this.Width - e.HorizontalChange > this.MinWidth)
                            {
                                newWidth -= e.HorizontalChange;
                                this.Left += e.HorizontalChange;
                                this.Width = newWidth;
                            }
                            break;
                        case "RightThumb":
                            if (this.Width + e.HorizontalChange > this.MinWidth)
                            {
                                newWidth += e.HorizontalChange;
                                this.Width = newWidth;
                            }
                            break;
                        case "BottomThumb":
                            if (this.Height + e.VerticalChange > this.MinHeight)
                            {
                                newHeight += e.VerticalChange;
                                this.Height = newHeight;
                            }
                            break;
                        case "BottomLeftThumb":
                            if (this.Width - e.HorizontalChange > this.MinWidth && this.Height + e.VerticalChange > this.MinHeight)
                            {
                                newWidth -= e.HorizontalChange;
                                newHeight += e.VerticalChange;
                                this.Left += e.HorizontalChange;
                                this.Width = newWidth;
                                this.Height = newHeight;
                            }
                            break;
                        case "BottomRightThumb":
                            if (this.Width + e.HorizontalChange > this.MinWidth && this.Height + e.VerticalChange > this.MinHeight)
                            {
                                newWidth += e.HorizontalChange;
                                newHeight += e.VerticalChange;
                                this.Width = newWidth;
                                this.Height = newHeight;
                            }
                            break;
                        default:
                            break;
                    }
                    Task.Delay(100).Wait();
                }
                catch (Exception)
                {
                    return;
                }
            }
        }
    }
}
