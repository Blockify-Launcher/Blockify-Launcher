using BlockifyLauncher.MVVM.Views.Style.Effect;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Логика взаимодействия для MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {

        public MainPage()
        {
            InitializeComponent();

            MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
            Blur_WindowBlur.BlurContainer       = mainWindow.MainBorder;
            Blur_WindowBlur_Two.BlurContainer   = mainWindow.MainBorder;
        }

        private void ResizeStackPanelNew(object sender, SizeChangedEventArgs e)
        {
            if(e.WidthChanged)
            {
                int MaxWidth = (int)this.DesiredSize.Width;
                StackPanelNews.Children.Clear();
                for (int Width_Panel = 0; Width_Panel + 250 < MaxWidth; Width_Panel += 270)
                {
                    StackPanelNews.Children.Add(new Border()
                    {
                        Width = 250,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Margin = new Thickness(10, 20, 10, 20),
                        Style = FindResource("CustomItemNews") as System.Windows.Style
                    });
                }
            }
        }

        private void LoadingMainPage(object sender, RoutedEventArgs e)
        {
            
        }
    }
}
