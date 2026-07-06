using BlockifyLauncher.MVVM.Views.Pages.Func.Setting;
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.Win32;
using System.Collections.ObjectModel;
using BlockifyLib.Launcher.Version;
using System.Windows.Media;
using BlockifyLauncher.Resources;
using System.Globalization;

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

        // element
        public ObservableCollection<CheckBox> _version_list { get; set; }
            = new ObservableCollection<CheckBox>();

        public SettingPage()
        {
            InitializeComponent();
            DataContext = this;
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

            // Generate element
            GenarateElement_CheckBox();
            FillLanguageComboBox();
            FillVibeComboBox();
        }

        private static readonly string[] VibeKeys = { "auto", "ocean", "sunset", "nether", "cherry", "end" };

        private void FillVibeComboBox()
        {
            ComboBoxVibe.Items.Clear();
            foreach (var key in VibeKeys)
                ComboBoxVibe.Items.Add(new ComboBoxItem
                {
                    Content = ResxLocalizationProvider.Instance["vibe_" + key],
                    Tag = key
                });

            string current = new Properties.Settings().GetVibe() ?? "auto";
            foreach (ComboBoxItem item in ComboBoxVibe.Items)
                if ((string)item.Tag == current)
                {
                    ComboBoxVibe.SelectedItem = item;
                    break;
                }
        }

        private void ComboBoxVibeSelect(object sender, SelectionChangedEventArgs e)
        {
            if (((ComboBox)sender).SelectedItem is not ComboBoxItem item) return;

            new Properties.Settings().SetVibe((string)item.Tag);
            mainWindow.ApplyVibeBackground();
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

        private void GenarateElement_CheckBox()
        {
            int i = 0;

            foreach(var item in ProfileConverter.GetListVersion())
            {
                _version_list.Add(new CheckBox()
                {
                    Name = $"_version_checkBox_{i++}",
                    Content = $"{ResxLocalizationProvider.Instance["show_versions_prefix"]} {ProfileConverter.ToString(item)}",
                    Style = (System.Windows.Style)FindResource("CustomCheckBox"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                    FontSize = 14
                });
            }
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

        private void FillLanguageComboBox()
        {
            // TODO Нужно будет переделать реализацию
            var availableCultures = new List<CultureInfo>
            {
                new CultureInfo("en-US"),
                new CultureInfo("ru-RU"),
                new CultureInfo("uk-UA")
            };

            ComboBoxLauncherLanguage.Items.Clear();


            foreach (var culture in availableCultures)
            {
                var comboBoxItem = new ComboBoxItem
                {
                    Content = culture.NativeName, 
                    Tag = culture.Name 
                };

                ComboBoxLauncherLanguage.Items.Add(comboBoxItem);
            }

            foreach (ComboBoxItem item in ComboBoxLauncherLanguage.Items)
                if (item.Tag.ToString() == new Properties.Settings().GetLanguage())
                {
                    ComboBoxLauncherLanguage.SelectedItem = item;
                    break;
                }
        }

        private void ComboBoxLauncherLanguageSelect(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = (ComboBoxItem)((ComboBox)sender).SelectedItem;
            ResxLocalizationProvider.Instance.ChangeLanguage(
                selectedItem.Tag.ToString()
                );

            new Properties.Settings().SetLauguage(
                selectedItem.Tag.ToString()
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
