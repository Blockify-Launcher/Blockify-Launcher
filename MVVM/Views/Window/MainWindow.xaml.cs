using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace BlockifyLauncher
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
            this.Resources.Add("WindowTitle", this.Title);
        }

        private void LoadingMainWindow(object sender, RoutedEventArgs e)
        {
            //this.MinecraftVerisonComboBox.Items.Add("Version 1.17.1");
            //MinecraftVerisonComboBox.SelectedIndex = 0;
        }

        private void MainWindowsClose(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        private void MainWindowsMinimals(object sender, RoutedEventArgs e) => Application.Current.MainWindow.WindowState = WindowState.Minimized;
        private void HeaderMouse(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

    }
}
