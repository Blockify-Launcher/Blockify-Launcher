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

        private void LoadingMainPage(object sender, RoutedEventArgs e)
        {
            InitializeNews();
        }

        // Initialize news list.
        private void InitializeNews()
        {
            var news = new News();
            _news = news.getAllNews();

            StackPanelNews.Children.Clear();

            for (int i = 0; i < _news.Length; i++)
                StackPanelNews.Children.Add(CreateNewsItem(_news[i]));

            UpdateStackPanelNews(); // Update visual element.
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
