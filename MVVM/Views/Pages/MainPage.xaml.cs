using BlockifyLauncher.MVVM.Views.Components;
using BlockifyLauncher.MVVM.Views.Pages.Func.Main;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Логика взаимодействия для MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {
        private NewsStr[] _news;

        private const int ItemWidth = 250;
        private const int ItemMargin = 10;

        public MainPage()
        {
            InitializeComponent();
            
            SetBlurContainer();
        }


        private void SetBlurContainer()
        {
            MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
            Blur_WindowBlur.BlurContainer = mainWindow.MainBorder;
            Blur_WindowBlur_Two.BlurContainer = mainWindow.MainBorder;
        }
        

        /*private async void UpdateStackPanelNews()
        {
            var news = new News();
            _news = news.getAllNews();

            int maxItems = (int)this.ActualWidth / (ItemWidth + ItemMargin);
            StackPanelNews.Children.Clear();
            
            for (int i = 0; i < maxItems && i < _news.Length; i++)
                StackPanelNews.Children.Add(CreateNewsItem(_news[i]));
        }*/

        private NewsComponent CreateNewsItem(NewsStr __news)
        {
            var newsItem = new NewsComponent(__news);
            newsItem.Width = ItemWidth;
            return newsItem;
        }

        private async void LoadingMainPage(object sender, RoutedEventArgs e)
        {
            FillQuickRow();
            var newsTask = InitializeNewsAsync();
            var pingTask = PingFavoriteServerAsync();
            await newsTask;
            await pingTask;
        }

        // quick-row: continue / stats / favorite server
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

                ServerDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x7D, 0xFF, 0x5F));
                ServerStatus.Text = string.Format(lang["server_online"], watch.ElapsedMilliseconds);
            }
            catch
            {
                ServerDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B));
                ServerStatus.Text = lang["server_offline"];
            }
        }

        // Live news: Mojang launcher feed + Blockify feed, with offline cache.
        // Falls back to the bundled local file when nothing is available.
        private async Task InitializeNewsAsync()
        {
            List<Core.News.NewsItem> items;
            try
            {
                items = await Core.News.NewsService.GetAsync();
            }
            catch
            {
                items = new List<Core.News.NewsItem>();
            }

            StackPanelNews.Children.Clear();

            if (items.Count > 0)
            {
                foreach (var item in items)
                    StackPanelNews.Children.Add(new NewsComponent(item) { Width = ItemWidth });
            }
            else
            {
                InitializeNewsFromLocalFile();
            }

            UpdateStackPanelNews(); // Update visual element.
        }

        // Legacy bundled news file (offline last resort).
        private void InitializeNewsFromLocalFile()
        {
            try
            {
                var news = new News();
                _news = news.getAllNews();
                if (_news == null) return;

                for (int i = 0; i < _news.Length; i++)
                    StackPanelNews.Children.Add(CreateNewsItem(_news[i]));
            }
            catch { /* no local news either — empty panel */ }
        }

        // Count visible panel element.
        private static int GetVisibleElementCount(StackPanel panel)
        {
            int visibleElementCount = 0;

            foreach (UIElement element in panel.Children)
                if (element.Visibility == Visibility.Visible)
                    visibleElementCount++;

            return visibleElementCount;
        }

        // Resize static panel.
        private async void ResizeStackPanelNew(object sender, SizeChangedEventArgs e)
        {
            if ((int)this.ActualWidth / (ItemWidth + ItemMargin) != GetVisibleElementCount(StackPanelNews))
                if (e.WidthChanged)
                {
                    UpdateStackPanelNews();
                }
        }

        // Update resize.
        private async void UpdateStackPanelNews()
        {
            int maxElement = (int)this.ActualWidth / (ItemWidth + ItemMargin);
            for (int i = 0; i < StackPanelNews.Children.Count; i++)
                StackPanelNews.Children[i].Visibility =
                    i < maxElement ? Visibility.Visible : Visibility.Collapsed;
        }

    }
}
