using BlockifyLauncher.MVVM.Views.Pages.Func.Setting;
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.Win32;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Логика взаимодействия для SettingPage.xaml
    /// </summary>
    public partial class SettingPage : Page
    {

        public struct Display
        {
            public int ScreenWidth;
            public int ScreenHeight;

            public bool FullScrean;
        }

        private int MinMemoryRam = 1024;
        private MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        public static Setting settingLauncher = new Setting();
        public SettingPage()
        {
            InitializeComponent();
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

            this.ComboBoxDisplayForm.SelectedIndex = new Properties.Settings().GetHideLauncher();
            this.ComboBoxLauncherLanguage.SelectedIndex = new Properties.Settings().GetLanguage();
        }

        private static readonly Regex onlyNumbers = new Regex("[^0-9.-]+");
        private static bool IsTextAllowed(string text)
        {
            return !onlyNumbers.IsMatch(text);
        }

        private void NumsPreviewTextInput(object sender, TextCompositionEventArgs e) =>
            e.Handled = !IsTextAllowed(e.Text);

        private void PathMinecraft(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.InitialDirectory = MinecraftPath_TextBox.Text ?? @"C:\";
            saveFileDialog.RestoreDirectory = true;

            if (saveFileDialog.ShowDialog() == true)
            {
                new MessageBox(saveFileDialog.FileName, MessageBox.TypeMessage.Error).ShowDialog();
            }
        }

        private void ApplySettingScrean(object sender, RoutedEventArgs e)
        {
            if (settingLauncher.screadFormat.FullScrean != FullScreanCheckBox.IsChecked)
                settingLauncher.SetFullScrean(FullScreanCheckBox.IsChecked.Value);
            if (settingLauncher.screadFormat.ScreenWidth.ToString() != WidthScrean.Text ||
                settingLauncher.screadFormat.ScreenHeight.ToString() != HeightScrean.Text)
                settingLauncher.SetDisplay(
                    Convert.ToInt32(WidthScrean.Text), Convert.ToInt32(HeightScrean.Text));
            SaveProperties("Save", "Changing screen settings.");
        }

        private void SliderRAMValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                settingLauncher.SetMemoryRAM(Convert.ToInt32((sender as Slider).Value));
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
                if (GlobalMemoryStatusEx(memoryStatus))
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

        private void SaveProperties(string Title = "Save", string Description = "\0") =>
            _ = mainWindow.NotificationElement.GetNotification(Title, Description);

        private void ComboBoxDisplayFormSelect(object sender, SelectionChangedEventArgs e)
        {
            new Properties.Settings().SetHideLauncher(
                ((ComboBox)sender).SelectedIndex
                );
        }

        private void ComboBoxLauncherLanguageSelect(object sender, SelectionChangedEventArgs e)
        {
            new Properties.Settings().SetLauguage(
                ((ComboBox)sender).SelectedIndex
                );
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
