using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.Win32;
using BlockifyLauncher.Resources;
using System.Globalization;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Settings with sidebar sections: Game / Java & files / Launcher /
    /// Versions / Data. Everything applies live except the game directory.
    /// </summary>
    public partial class SettingPage : Page
    {
        private const int MinMemoryRam = 1024;
        private MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        private Properties.Settings setting = new Properties.Settings();
        private bool _loaded;

        public SettingPage()
        {
            InitializeComponent();
        }

        private void LoadingPages(object sender, RoutedEventArgs e)
        {
            this.SliderRam.Minimum = MinMemoryRam;
            this.SliderRam.Maximum = (int)GetMaxRamMb();
            this.SliderRam.Value = setting.GetMemoryRAM();
            UpdateRamText();

            var display = setting.GetSettingDisplayGame();
            this.WidthScrean.Text = display.w.ToString();
            this.HeightScrean.Text = display.h.ToString();

            this.FullScreanCheckBox.IsChecked = setting.GetFullScrean();
            this.ComboBoxDisplayForm.SelectedIndex = setting.GetHideLauncher();

            string dir = setting.GetMinecraftDir();
            this.MinecraftPath_TextBox.Text = string.IsNullOrEmpty(dir)
                ? BlockifyLib.Launcher.Minecraft.MinecraftPath.GetOSDefaultPath()
                : dir;

            this.FavoriteServerTextBox.Text = setting.GetFavoriteServer();

            this.SwitchSnapshots.IsChecked = setting.GetShowSnapshots();
            this.SwitchBetas.IsChecked = setting.GetShowBetas();
            this.SwitchAlphas.IsChecked = setting.GetShowAlphas();
            this.SwitchAikar.IsChecked = setting.GetJvmAikar();
            this.SwitchParade.IsChecked = setting.GetShowParade();
            this.SwitchDiscord.IsChecked = setting.GetDiscordRpc();

            VersionLabel.Text = "Blockify " +
                (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.1");
            UpdateCacheSize();

            FillJavaComboBox();
            FillLanguageComboBox();
            FillVibeComboBox();

            _loaded = true;
        }

        private void SectionChecked(object sender, RoutedEventArgs e)
        {
            if (PaneGame == null) return; // fires during InitializeComponent
            string tag = (string)((RadioButton)sender).Tag;
            PaneGame.Visibility = tag == "game" ? Visibility.Visible : Visibility.Collapsed;
            PaneJava.Visibility = tag == "java" ? Visibility.Visible : Visibility.Collapsed;
            PaneLauncher.Visibility = tag == "launcher" ? Visibility.Visible : Visibility.Collapsed;
            PaneVersions.Visibility = tag == "versions" ? Visibility.Visible : Visibility.Collapsed;
            PaneData.Visibility = tag == "data" ? Visibility.Visible : Visibility.Collapsed;
        }

        #region game
        private static readonly Regex onlyNumbers = new Regex("[^0-9.-]+");
        private void NumsPreviewTextInput(object sender, TextCompositionEventArgs e) =>
            e.Handled = onlyNumbers.IsMatch(e.Text);

        private void ApplySettingScrean(object sender, RoutedEventArgs e)
        {
            try
            {
                setting.SetFullScrean(FullScreanCheckBox.IsChecked == true);
                setting.SetSettingDisplayGame(new Properties.Display
                {
                    w = Convert.ToInt32(WidthScrean.Text),
                    h = Convert.ToInt32(HeightScrean.Text)
                });
                Notify(ResxLocalizationProvider.Instance["saved"],
                       $"{WidthScrean.Text} ⨉ {HeightScrean.Text}");
            }
            catch (Exception ex)
            {
                new MessageBox(ex.Message, MessageBox.TypeMessage.Error).ShowDialog();
            }
        }

        private void SliderRAMValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_loaded) return;
            setting.SetMemoryRAM((int)((Slider)sender).Value);
            UpdateRamText();
        }

        private void UpdateRamText() =>
            RamValueText.Text = $"{(int)SliderRam.Value} {ResxLocalizationProvider.Instance["mb"]}";

        private void AikarChanged(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            setting.SetJvmAikar(SwitchAikar.IsChecked == true);
        }
        #endregion

        #region java & files
        private void FillJavaComboBox()
        {
            JavaVersion.Items.Clear();
            JavaVersion.Items.Add("javaw.exe");

            foreach (var path in FindJavaInstalls())
                JavaVersion.Items.Add(path);

            string saved = setting.GetJavaPath();
            if (!JavaVersion.Items.Contains(saved))
                JavaVersion.Items.Add(saved);
            JavaVersion.SelectedItem = saved;
        }

        private static IEnumerable<string> FindJavaInstalls()
        {
            var found = new List<string>();

            void Probe(string binDir)
            {
                string javaw = Path.Combine(binDir, "javaw.exe");
                if (File.Exists(javaw) && !found.Contains(javaw))
                    found.Add(javaw);
            }

            string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
                Probe(Path.Combine(javaHome, "bin"));

            foreach (var root in new[]
            {
                @"C:\Program Files\Java",
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Microsoft",
                @"C:\Program Files (x86)\Java",
                @"C:\Program Files\Zulu"
            })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var dir in Directory.GetDirectories(root))
                        Probe(Path.Combine(dir, "bin"));
                }
                catch { /* no access — skip root */ }
            }
            return found;
        }

        private void JavaVersionSelect(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded || JavaVersion.SelectedItem == null) return;
            setting.SetJavaPath(JavaVersion.SelectedItem.ToString() ?? "javaw.exe");
        }

        private void PathMinecraft(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                InitialDirectory = MinecraftPath_TextBox.Text
            };
            if (dialog.ShowDialog() != true) return;

            setting.SetMinecraftDir(dialog.FolderName);
            MinecraftPath_TextBox.Text = dialog.FolderName;
            Notify(ResxLocalizationProvider.Instance["saved"],
                   ResxLocalizationProvider.Instance["restart_required"]);
        }
        #endregion

        #region launcher
        private void ComboBoxDisplayFormSelect(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            setting.SetHideLauncher(((ComboBox)sender).SelectedIndex);
        }

        private void FillLanguageComboBox()
        {
            var availableCultures = new List<CultureInfo>
            {
                new CultureInfo("en-US"),
                new CultureInfo("ru-RU"),
                new CultureInfo("uk-UA")
            };

            ComboBoxLauncherLanguage.Items.Clear();
            foreach (var culture in availableCultures)
                ComboBoxLauncherLanguage.Items.Add(new ComboBoxItem
                {
                    Content = culture.NativeName,
                    Tag = culture.Name
                });

            foreach (ComboBoxItem item in ComboBoxLauncherLanguage.Items)
                if ((string)item.Tag == setting.GetLanguage())
                {
                    ComboBoxLauncherLanguage.SelectedItem = item;
                    break;
                }
        }

        private void ComboBoxLauncherLanguageSelect(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            if (((ComboBox)sender).SelectedItem is not ComboBoxItem item) return;

            ResxLocalizationProvider.Instance.ChangeLanguage((string)item.Tag);
            setting.SetLauguage((string)item.Tag);
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

            string current = setting.GetVibe() ?? "auto";
            foreach (ComboBoxItem item in ComboBoxVibe.Items)
                if ((string)item.Tag == current)
                {
                    ComboBoxVibe.SelectedItem = item;
                    break;
                }
        }

        private void ComboBoxVibeSelect(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
            if (((ComboBox)sender).SelectedItem is not ComboBoxItem item) return;

            setting.SetVibe((string)item.Tag);
            mainWindow.ApplyVibeBackground();
        }

        private void ParadeChanged(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            setting.SetShowParade(SwitchParade.IsChecked == true);
            mainWindow.ApplyParadeSetting();
        }

        private void DiscordChanged(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            bool on = SwitchDiscord.IsChecked == true;
            setting.SetDiscordRpc(on);
            App.SetDiscordEnabled(on);
        }

        private void FavoriteServerChanged(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            string host = FavoriteServerTextBox.Text.Trim();
            if (host == setting.GetFavoriteServer()) return;

            setting.SetFavoriteServer(host);
            Notify(ResxLocalizationProvider.Instance["saved"], host);
        }
        #endregion

        #region version filters
        private async void VersionFilterChanged(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;

            setting.SetShowSnapshots(SwitchSnapshots.IsChecked == true);
            setting.SetShowBetas(SwitchBetas.IsChecked == true);
            setting.SetShowAlphas(SwitchAlphas.IsChecked == true);

            try { await mainWindow.InitializeVersionsAsync(); }
            catch { /* offline — the filter still applies on next refresh */ }
        }
        #endregion

        #region data
        private static string CacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlockifyLauncher");

        private void UpdateCacheSize()
        {
            long bytes = 0;
            try
            {
                if (Directory.Exists(CacheDir))
                    foreach (var f in Directory.EnumerateFiles(CacheDir, "*", SearchOption.AllDirectories))
                        bytes += new FileInfo(f).Length;
            }
            catch { /* partial size is fine */ }
            CacheSizeText.Text = (bytes / 1024.0 / 1024.0).ToString("0.#") + " " + ResxLocalizationProvider.Instance["mb"];
        }

        private void ClearCacheClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(CacheDir))
                    Directory.Delete(CacheDir, true);
            }
            catch { /* files in use — best effort */ }
            UpdateCacheSize();
            Notify(ResxLocalizationProvider.Instance["saved"], CacheSizeText.Text);
        }

        private void OpenDataClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CacheDir)
                {
                    UseShellExecute = true
                });
            }
            catch { /* explorer unavailable */ }
        }

        private static readonly HttpClient Http = CreateHttp();
        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlockifyLauncher/0.2");
            return http;
        }

        private async void CheckUpdatesClick(object sender, RoutedEventArgs e)
        {
            var lang = ResxLocalizationProvider.Instance;
            try
            {
                string json = await Http.GetStringAsync(
                    "https://api.github.com/repos/Blockify-Launcher/Blockify-Launcher/releases/latest");
                var release = Newtonsoft.Json.Linq.JObject.Parse(json);
                string tag = release.Value<string>("tag_name") ?? "";
                string url = release.Value<string>("html_url") ?? "";

                var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 1);
                if (Version.TryParse(tag.TrimStart('v', 'V'), out var latest) && latest > current)
                {
                    Notify(lang["check_updates"], string.Format(lang["update_available"], tag));
                    if (!string.IsNullOrEmpty(url))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
                else
                {
                    Notify(lang["check_updates"], lang["up_to_date"]);
                }
            }
            catch
            {
                Notify(lang["check_updates"], lang["no_releases"]);
            }
        }
        #endregion

        private void Notify(string title, string description) =>
            _ = mainWindow.NotificationElement.GetNotification(title, description);

        #region max RAM (WinAPI)
        private static ulong GetMaxRamMb()
        {
            try
            {
                var memoryStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memoryStatus))
                    return memoryStatus.ullTotalPhys / (1024 * 1024);
            }
            catch { /* fall through */ }
            return 8192;
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
        #endregion
    }
}
