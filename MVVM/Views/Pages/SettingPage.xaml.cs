﻿using BlockifyLauncher.MVVM.Views.Pages.Func.Setting;
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
