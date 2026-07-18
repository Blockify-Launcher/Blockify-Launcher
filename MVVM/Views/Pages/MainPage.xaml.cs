using BlockifyLauncher.Core.News;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Home page (Liquid Glass): hero with Play + version selector, a live
    /// news list and a side card (continue / stats / favorite server).
    /// The window owns launch/version/account state; this page drives it.
    /// </summary>
    public partial class MainPage : Page
    {
        private MainWindow Win => (MainWindow)Application.Current.MainWindow;
        private bool _syncing;

        public MainPage()
        {
            InitializeComponent();
        }

        private async void LoadingMainPage(object sender, RoutedEventArgs e)
        {
            RefreshVersions();
            Win.ShellReady += RefreshVersions;
            Win.LaunchStateChanged += OnLaunchState;

            await InitializeNewsAsync();
        }

        private void OnLaunchState(bool busy) => Dispatcher.Invoke(() =>
        {
            PlayButton.IsEnabled = !busy;
            PlayButton.Content = busy ? "ЗАПУСК…" : "▶  ИГРАТЬ";
        });

        #region versions
        private void RefreshVersions()
        {
            _syncing = true;
            VersionCombo.Items.Clear();
            foreach (var name in Win.GetVersionNames())
                VersionCombo.Items.Add(name);
            VersionCombo.SelectedIndex = Win.SelectedVersionIndex >= 0 ? Win.SelectedVersionIndex : 0;
            _syncing = false;

            var lang = BlockifyLauncher.Resources.ResxLocalizationProvider.Instance;
            HeroTitle.Text = lang["hero_ready"];
            HeroSub.Text = VersionCombo.SelectedItem is string v
                ? $"Minecraft {v}"
                : lang["quick_no_launches"];
        }

        private void VersionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || VersionCombo.SelectedItem is not string v) return;
            Win.SelectVersion(v);
            HeroSub.Text = $"Minecraft {v}";
        }
        #endregion

        private void PlayClick(object sender, RoutedEventArgs e) => Win.LaunchSelected();

        #region news
        private async System.Threading.Tasks.Task InitializeNewsAsync()
        {
            // cached feed paints instantly; the live fetch refreshes it in the background
            RenderNews(NewsService.GetCached(4));

            List<NewsItem> news;
            try { news = await NewsService.GetAsync(4); }
            catch { news = new List<NewsItem>(); }

            if (news.Count > 0)
                RenderNews(news);
        }

        private void RenderNews(List<NewsItem> news)
        {
            if (news.Count == 0) return;
            NewsList.Children.Clear();
            foreach (var item in news)
                NewsList.Children.Add(BuildNewsItem(item));
        }

        private UIElement BuildNewsItem(NewsItem item)
        {
            var row = new Border
            {
                Padding = new Thickness(0, 11, 0, 11),
                BorderBrush = (Brush)FindResource("G.Line"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var thumb = new Border { Width = 64, Height = 44, CornerRadius = new CornerRadius(9) };
            if (!string.IsNullOrEmpty(item.LocalImagePath) && File.Exists(item.LocalImagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(item.LocalImagePath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160;
                bmp.EndInit(); bmp.Freeze();
                thumb.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            else thumb.Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x4A, 0x1D));
            Grid.SetColumn(thumb, 0);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = (Brush)FindResource("G.Text"),
                FontSize = 13, FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = item.Date == default ? item.Subtitle : item.Date.ToString("d MMM yyyy"),
                Foreground = (Brush)FindResource("G.Muted"),
                FontSize = 11, Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(text, 2);

            grid.Children.Add(thumb);
            grid.Children.Add(text);
            row.Child = grid;

            if (!string.IsNullOrEmpty(item.Link))
                row.MouseLeftButtonUp += (_, _) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Link) { UseShellExecute = true });
                    }
                    catch { }
                };
            return row;
        }
        #endregion
    }
}
