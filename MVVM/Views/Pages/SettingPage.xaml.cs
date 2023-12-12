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

        public class Setting
        {
            public CmlLib.Core.Version.MVersion    Version;
            public CmlLib.Core.Auth.MSession       Session; 

            public MinecraftPath       minecraftPath;
            public string              JavaPath;

            public Display screadFormat;

            private int _RamMb;
            public  int RamMB {
                get { return _RamMb; } 
                set { _RamMb = 1024 < value ? value : 1024; } 
            }

            public void GetSetting()
            {

            }

            public Setting()
            {
                minecraftPath = new MinecraftPath();
                screadFormat = new Display();
            }


            public void SetDisplay(int W, int H)
            {
                screadFormat.ScreenWidth = W;
                screadFormat.ScreenHeight = H;
            }

            public void SetFullScrean(bool fullScr) => 
                screadFormat.FullScrean = fullScr;

            public void SetMemoryRAM(int RAM) => 
                RamMB = RAM;
        }

        private int MinMemoryRam = 1024;
        private int MaxMemoryRam;
        public static Setting settingLauncher = new Setting();

        public SettingPage()
        {
            InitializeComponent();
            
            MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
            Blur_WindowBlur.BlurContainer = mainWindow.MainBorder;

            MaxMemoryRam = (int)getMaxRam();
        }

        private void LoadingPages(object sender, RoutedEventArgs e)
        {
            settingLauncher.RamMB = MinMemoryRam;
            this.JavaVersion.Items.Add("Default");
            this.SliderRam.Minimum = MinMemoryRam;
            this.SliderRam.Maximum = MaxMemoryRam;
            MinecraftPath_TextBox.Text = settingLauncher.minecraftPath.ToString();

            this.SliderRam.Value = MinMemoryRam; // TODO : Подключить из подгрузки конфига 
            this.WidthScrean.Text = 925.ToString();
            this.HeightScrean.Text = 530.ToString();
            this.FullScreanCheckBox.IsChecked = true;
            this.JavaVersion.SelectedIndex = 0;
        }

        private void SliderRAMValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                settingLauncher.SetMemoryRAM(Convert.ToInt32((sender as Slider)?.Value ?? 1024));
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
