using System.Windows;
using System.Windows.Controls;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Логика взаимодействия для MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {

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

        private void ResizeStackPanelNew(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
            {
                UpdateStackPanelNews();
            }
        }

        private void UpdateStackPanelNews()
        {
            int maxItems = (int)this.ActualWidth / (ItemWidth + ItemMargin);
            StackPanelNews.Children.Clear();

            for (int i = 0; i < maxItems; i++)
            {
                StackPanelNews.Children.Add(CreateNewsItem());
            }
        }

        private Border CreateNewsItem()
        {
            return new Border()
            {
                Width = ItemWidth,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(ItemMargin, 20, ItemMargin, 20),
                Style = FindResource("CustomItemNews") as System.Windows.Style
            };
        }

        private void LoadingMainPage(object sender, RoutedEventArgs e)
        {

        }
    }
}
