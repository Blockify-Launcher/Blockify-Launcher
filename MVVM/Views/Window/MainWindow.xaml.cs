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
using CmlLib.Utils;
using System.Drawing.Printing;

namespace BlockifyLauncher
{
    public partial class MainWindow : Window
    {
        public Border MainBorder { get; set; }
        public CMLauncher launcher;

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
            this.UserName.Text = Properties.Settings.Default.UserName;
            this.ProgressBarLoad.Activ = "None";
            try
            {
                this.launcher = new CMLauncher(SettingPage.settingLauncher.minecraftPath);
                SettingPage.settingLauncher.CollectionVerion = await launcher.GetAllVersionsAsync();
                foreach (var version in SettingPage.settingLauncher.CollectionVerion)
                    MinecraftVerisonComboBox.Items.Add(version.Name);
                MinecraftVerisonComboBox.SelectedIndex = 0;

                this.launcher.FileChanged += LauncherFileChanged;
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

            ProgressBarLoad.Activ = "Use";

            var process = await launcher.CreateProcessAsync(MinecraftVerisonComboBox.Items[MinecraftVerisonComboBox.SelectedIndex].ToString(), new MLaunchOption
            {
                Session     = SettingPage.settingLauncher.Session,
                MaximumRamMb = SettingPage.settingLauncher.RamMB,

                //JavaPath = SettingPage.settingLauncher.javaPath,
                JVMArguments = new string[] { },

                VersionType = "BlockifyLauncher",
                GameLauncherName = "BlockifyLauncher",
                GameLauncherVersion = "1",

                ScreenWidth = SettingPage.settingLauncher.screadFormat.ScreenWidth,
                ScreenHeight = SettingPage.settingLauncher.screadFormat.ScreenHeight,
                FullScreen = SettingPage.settingLauncher.screadFormat.FullScrean,
            });

            var processUtil = new ProcessUtil(process);
            processUtil.StartWithEvents();
            ProgressBarLoad.Activ = "Сlose";
        }

        private void LauncherFileChanged(DownloadFileChangedEventArgs e) 
        {
            Dispatcher.Invoke((System.Windows.Forms.MethodInvoker)delegate () {
                ProgressBarLoad.Title = e.FileName;
                ProgressBarLoad.Description = e.FileKind.ToString();
                ProgressBarLoad.Maximum = e.TotalFileCount;
                ProgressBarLoad.Value = e.ProgressedFileCount;
            });
        }

        private void TextChangedUserName(object sender, TextChangedEventArgs e)
        {
            TextBox text = sender as TextBox;

            Properties.Settings.Default.UserName = text.Text;
            Properties.Settings.Default.Save();
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
