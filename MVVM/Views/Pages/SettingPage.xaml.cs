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
        private int MinMemoryRam = 1024;
        private MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        private Properties.Settings setting = new Properties.Settings();

        public SettingPage()
        {
            InitializeComponent();
            Blur_WindowBlur.BlurContainer = mainWindow.MainBorder;
        }

        private void LoadingPages(object sender, RoutedEventArgs e)
        {
            
            this.JavaVersion.Items.Add("javaw.exe");

            this.SliderRam.Value = setting.GetMemoryRAM();
            this.SliderRam.Minimum = MinMemoryRam;
            this.SliderRam.Maximum = (int)getMaxRam();

            
            //MinecraftPath_TextBox.Text = setting.minecraftPath.ToString();

            var display = setting.GetSettingDisplayGame();
            this.WidthScrean.Text = display.w.ToString();
            this.HeightScrean.Text = display.h.ToString();

            this.FullScreanCheckBox.IsChecked = setting.GetFullScrean();
            this.JavaVersion.SelectedIndex = 0;

            this.ComboBoxDisplayForm.SelectedIndex = setting.GetHideLauncher();
            this.ComboBoxLauncherLanguage.SelectedIndex = setting.GetLanguage();
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
            if (setting.GetFullScrean() != FullScreanCheckBox.IsChecked)
                setting.SetFullScrean(FullScreanCheckBox.IsChecked.Value);

            var display = setting.GetSettingDisplayGame();
            if (display.w.ToString() != WidthScrean.Text ||
                display.h.ToString() != HeightScrean.Text)
            {
                display.w = Convert.ToInt32(WidthScrean.Text);
                display.h = Convert.ToInt32(HeightScrean.Text);
            }
            setting.SetSettingDisplayGame(display);
            SaveProperties("Save", "Changing screen settings.");
        }

        private void SliderRAMValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                setting.SetMemoryRAM(Convert.ToInt32((sender as Slider).Value));
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
