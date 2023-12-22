using CmlLib.Core;
using CmlLib.Core.Files;
using CmlLib.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Animation;
using System.Resources;
using CmlLib.Core.Auth;
using System.Text.RegularExpressions;
using CmlLib.Core.Installer;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Логика взаимодействия для SettingPage.xaml
    /// </summary>
    public partial class SettingPage : Page
    {
        class MyMinecraftPath : MinecraftPath
        {
            public MyMinecraftPath(string p)
            {
                BasePath = NormalizePath(p);

                Library = NormalizePath(BasePath + "/libs");
                Versions = NormalizePath(BasePath + "/vers");
                Resource = NormalizePath(BasePath + "/resources");

                Runtime = NormalizePath(BasePath + "/java");
                Assets = NormalizePath(BasePath + "/assets");

                CreateDirs();
            }

            public override string GetVersionJarPath(string id)
                => NormalizePath($"{Versions}/{id}/client.jar");

            public override string GetVersionJsonPath(string id)
                => NormalizePath($"{Versions}/{id}/client.json");

            public override string GetAssetObjectPath(string assetId)
                => NormalizePath($"{Assets}/files");
        }

        public struct Display
        {
            public int ScreenWidth; 
            public int ScreenHeight;

            public bool FullScrean;
        }

        /// <summary>
        /// Project settings.
        /// </summary>
        public class Setting
        {
            public CmlLib.Core.Version.MVersionCollection CollectionVerion;
            public CmlLib.Core.Version.MVersion    Version;
            public CmlLib.Core.Auth.MSession       Session; 

            public MinecraftPath       minecraftPath;
            public string              javaPath;

            public Display screadFormat;

            private int _RamMb;
            public  int RamMB {
                get { return _RamMb; } 
                set { _RamMb = 1024 < value ? value : 1024; } 
            }
            public Setting()
            {
                javaPath = Properties.Settings.Default.JavaVersion;

                Session = MSession.GetOfflineSession(Properties.Settings.Default.UserName);
                RamMB   = Properties.Settings.Default.UseRam;

                screadFormat.ScreenWidth    = Properties.Settings.Default.ScreenWidth;
                screadFormat.ScreenHeight   = Properties.Settings.Default.ScreenHeight;
                screadFormat.FullScrean     = Properties.Settings.Default.FullScreanGame;

                minecraftPath = new MinecraftPath();
            }


            public void SetDisplay(int W, int H)
            {
                screadFormat.ScreenWidth = W;
                screadFormat.ScreenHeight = H;

                Properties.Settings.Default.ScreenWidth = W;
                Properties.Settings.Default.ScreenHeight = H;
            }

            public void SetFullScrean(bool fullScr)
            {
                screadFormat.FullScrean = fullScr;
                Properties.Settings.Default.FullScreanGame = fullScr;
            }

            public void SetMemoryRAM(int RAM)
            {
                RamMB = RAM;
                Properties.Settings.Default.UseRam = RAM;
            }
        }

        private int MinMemoryRam = 1024;
        public static Setting settingLauncher = new Setting();

        public SettingPage()
        {
            InitializeComponent();
            
            MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
            Blur_WindowBlur.BlurContainer = mainWindow.MainBorder;

        }

        private void LoadingPages(object sender, RoutedEventArgs e)
        {
            this.JavaVersion.Items.Add("javaw.exe");

            this.SliderRam.Value = settingLauncher.RamMB;
            this.SliderRam.Minimum = MinMemoryRam;
            this.SliderRam.Maximum = (int)getMaxRam();

            MinecraftPath_TextBox.Text = settingLauncher.minecraftPath.ToString();

            this.WidthScrean.Text = settingLauncher.screadFormat.ScreenWidth.ToString();
            this.HeightScrean.Text = settingLauncher.screadFormat.ScreenHeight.ToString();
            this.FullScreanCheckBox.IsChecked = settingLauncher.screadFormat.FullScrean;
            this.JavaVersion.SelectedIndex = 0;
        }

        private static readonly Regex onlyNumbers = new Regex("[^0-9.-]+");
        private static bool IsTextAllowed(string text)
        {
            return !onlyNumbers.IsMatch(text);
        }

        private void NumsPreviewTextInput(object sender, TextCompositionEventArgs e) =>
            e.Handled = !IsTextAllowed(e.Text);

        private void ApplySettingScrean(object sender, RoutedEventArgs e)
        {
            if (settingLauncher.screadFormat.FullScrean != FullScreanCheckBox.IsChecked)
                settingLauncher.SetFullScrean(FullScreanCheckBox.IsChecked.Value);
            if (settingLauncher.screadFormat.ScreenWidth.ToString() != WidthScrean.Text ||
                settingLauncher.screadFormat.ScreenHeight.ToString() != HeightScrean.Text)
                settingLauncher.SetDisplay(
                    Convert.ToInt32(WidthScrean.Text), Convert.ToInt32(HeightScrean.Text));
            Properties.Settings.Default.Save();
        }

        private void ButtonSaveRAMValue(object sender, RoutedEventArgs e) =>
            Properties.Settings.Default.Save();

        private void SliderRAMValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                settingLauncher.SetMemoryRAM(Convert.ToInt32((sender as Slider).Value));
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                new MessageBox(ex.Message, MessageBox.TypeMessage.Error).ShowDialog();
                return;
            }
        }

        private static ulong getMaxRam()
        {
            ulong maxMemory = 0;
            try
            {
                MEMORYSTATUSEX memoryStatus = new MEMORYSTATUSEX();
                if(GlobalMemoryStatusEx(memoryStatus))
                    maxMemory = memoryStatus.ullTotalPhys;
            }
            catch (Exception e)
            {
                new MessageBox("An error occurred while querying for WMI data: " + e.Message,
                    MessageBox.TypeMessage.Error).ShowDialog();
                return 0;
            }
            return maxMemory / (1024 * 1024);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX() =>
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    }
}
