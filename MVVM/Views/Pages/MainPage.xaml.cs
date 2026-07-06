using BlockifyLauncher.Core.News;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Home page, "magazine" layout: one featured story with artwork and
    /// description plus two side stories, then the quick row
    /// (continue / statistics / favorite server).
    /// </summary>
    public partial class MainPage : Page
    {
        private List<NewsItem> _news = new();

        public MainPage()
        {
            InitializeComponent();
        }

        private async void LoadingMainPage(object sender, RoutedEventArgs e)
        {
            FillQuickRow();
            var newsTask = InitializeNewsAsync();
            var pingTask = PingFavoriteServerAsync();
            await newsTask;
            await pingTask;
        }

        #region news (magazine)
        private async Task InitializeNewsAsync()
        {
            try
            {
                _news = await NewsService.GetAsync(3);
            }
            catch
            {
                _news = new List<NewsItem>();
            }

            FillCard(0, FeaturedCard, FeaturedBg, FeaturedBadge, FeaturedBadgeText, FeaturedTitle, FeaturedDate, FeaturedDesc);
            FillCard(1, Small1Card, Small1Bg, Small1Badge, Small1BadgeText, Small1Title, Small1Date, null);
            FillCard(2, Small2Card, Small2Bg, Small2Badge, Small2BadgeText, Small2Title, Small2Date, null);
        }

        private void FillCard(int index, Border card, Border bg, Border badge, TextBlock badgeText,
            TextBlock title, TextBlock date, TextBlock? desc)
        {
            if (index >= _news.Count)
            {
                card.Visibility = Visibility.Collapsed;
                return;
            }

            var item = _news[index];
            card.Visibility = Visibility.Visible;

            title.Text = item.Title;
            date.Text = item.Date == default
                ? item.Subtitle
                : item.Date.ToString("d MMMM yyyy").ToUpper();
            if (desc != null)
                desc.Text = item.Text;

            badgeText.Text = item.IsOwn ? "BLOCKIFY" : "MOJANG";
            if (item.IsOwn)
                badge.Background = (Brush)FindResource("GreenAccent");

            if (!string.IsNullOrEmpty(item.LocalImagePath) && File.Exists(item.LocalImagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(item.LocalImagePath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 900;
                bmp.EndInit();
                bmp.Freeze();
                bg.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }

            if (!string.IsNullOrEmpty(item.Link))
                card.ToolTip = item.Link;
        }

        private void NewsCardClick(object sender, RoutedEventArgs e)
        {
            int index = int.Parse((string)((FrameworkElement)sender).Tag);
            if (index >= _news.Count || string.IsNullOrEmpty(_news[index].Link)) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_news[index].Link)
                {
                    UseShellExecute = true
                });
            }
            catch { /* no browser handler */ }
        }
        #endregion

        #region quick row
        private void FillQuickRow()
        {
            var lang = BlockifyLauncher.Resources.ResxLocalizationProvider.Instance;
            var setting = new Properties.Settings();

            string lastVersion = setting.GetLastVersion();
            if (string.IsNullOrEmpty(lastVersion))
            {
                ContinueTitle.Text = "Minecraft";
                ContinueSub.Text = lang["quick_no_launches"];
                ContinueSection.IsEnabled = false;
                ContinueSection.Opacity = 0.55;
            }
            else
            {
                ContinueTitle.Text = "Minecraft " + lastVersion;
                ContinueSub.Text = setting.GetLastPlayed();
            }

            StatCount.Text = setting.GetLaunchCount().ToString();
            StatLabel.Text = lang["quick_launches"];
            StatSub.Text = string.IsNullOrEmpty(setting.GetLastPlayed())
                ? "" : lang["quick_last"] + " " + setting.GetLastPlayed();

            ServerHost.Text = setting.GetFavoriteServer();
        }

        private void ContinueClick(object sender, RoutedEventArgs e) =>
            ((MainWindow)Application.Current.MainWindow).QuickLaunchLast();

        // simple TCP reachability + latency check (full SLP protocol later)
        private async Task PingFavoriteServerAsync()
        {
            var lang = BlockifyLauncher.Resources.ResxLocalizationProvider.Instance;
            string host = new Properties.Settings().GetFavoriteServer();
            if (string.IsNullOrWhiteSpace(host)) return;

            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using var client = new System.Net.Sockets.TcpClient();
                var connect = client.ConnectAsync(host, 25565);
                if (await Task.WhenAny(connect, Task.Delay(4000)) != connect || !client.Connected)
                    throw new TimeoutException();
                watch.Stop();

                ServerDot.Fill = new SolidColorBrush(Color.FromRgb(0x7D, 0xFF, 0x5F));
                ServerStatus.Text = string.Format(lang["server_online"], watch.ElapsedMilliseconds);
            }
            catch
            {
                ServerDot.Fill = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
                ServerStatus.Text = lang["server_offline"];
            }
        }
        #endregion
    }
}
