using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using CmlLib.Core;
using CmlLib.Core.Downloader;
using Newtonsoft.Json.Linq;

using BlockifyLauncher.MVVM.Views.Pages;
using CmlLib.Core.Auth;
using CmlLib.Core.Version;

namespace BlockifyLauncher
{
    public partial class MainWindow : Window
    {
        public Border MainBorder { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            this.Resources.Add("WindowTitle", this.Title);
            
            this.homeRadioButton.IsChecked = true;

            this.MainBorder = InnerBlurContainer;

            this.Width = Properties.Settings.Default.WidthProgram;
            this.Height = Properties.Settings.Default.HeightProgram;
        }

        private async void LoadingMainWindow(object sender, RoutedEventArgs e)
        {
            this.ProgressBarLoad.Activ = "None";
            try
            {
                CMLauncher launcher = new CMLauncher(new MinecraftPath());
                foreach (var version in await launcher.GetAllVersionsAsync())
                    MinecraftVerisonComboBox.Items.Add(version.Name);
                MinecraftVerisonComboBox.SelectedIndex = 0;
            } 
            catch (Exception ex)
            {
                new MessageBox(ex.Message, MessageBox.TypeMessage.Error).ShowDialog();
            }
        }

        private async void ButtonClickStartGame(object sender, RoutedEventArgs e)
        {
            /* This code would increase download speed. */
            System.Net.ServicePointManager.DefaultConnectionLimit = 256;

            MinecraftPath pathMin = SettingPage.settingLauncher.minecraftPath;
            CMLauncher cMLauncher = new CMLauncher(pathMin);
            cMLauncher.ProgressChanged += LauncherProgressChanged;
            cMLauncher.FileChanged += LauncherFileChanged;

            System.Diagnostics.Process process = await cMLauncher.CreateProcessAsync(MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex].ToString(),
                new MLaunchOption()
                {
                    StartVersion    = MVersion(new CMLauncher(new MinecraftPath()).GetVersion("1.17.1")), // TODO Error version connect.
                    Session         = MSession.GetOfflineSession("tester123"),

                    MaximumRamMb    = SettingPage.settingLauncher.RamMB,
                    JavaPath        = SettingPage.settingLauncher.JavaPath,
                    JVMArguments    = new string[] { },

                    VersionType = "BlockifyLauncher",
                    GameLauncherName = "BlockifyLauncher",
                    GameLauncherVersion = "1",

                    ScreenWidth     = SettingPage.settingLauncher.screadFormat.ScreenWidth,
                    ScreenHeight    = SettingPage.settingLauncher.screadFormat.ScreenHeight,
                    FullScreen      = SettingPage.settingLauncher.screadFormat.FullScrean

                }, checkAndDownload: false);
            process.Start();
        }

        private void LauncherFileChanged(DownloadFileChangedEventArgs e) 
        {
            ProgressBarLoad.Description = e.FileName;
        }

        private void LauncherProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            var obj = sender as CMLauncher;
            ProgressBarLoad.Title = obj.VersionLoader.ToString();
            ProgressBarLoad.Value = e.ProgressPercentage;
            if (99 < ProgressBarLoad.Value)
                ProgressBarLoad.Activ = "Close";
            else if (ProgressBarLoad.Activ == "None")
                ProgressBarLoad.Activ = "Use";
        }

        private void MainWindowsClose(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.Save();
            Application.Current.Shutdown();
        }

        private void MainWindowsMinimals(object sender, RoutedEventArgs e) => Application.Current.MainWindow.WindowState = WindowState.Minimized;
        private void HeaderMouse(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

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
                    Task.Delay(100);
                    new Properties.Settings().SettingsSavingSizeForms((int)this.Width, (int)this.Height);
                }
                catch (Exception)
                {
                    return;
                }
            }
        }


    }
}
