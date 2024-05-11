using BlockifyLib.Launcher.Microsoft.Auth;
using System.Windows;
using System.Windows.Input;

namespace BlockifyLauncher
{
    public partial class CustomWindow : Window
    {
        private Login _login;

        public CustomWindow()
        {
            InitializeComponent();
        }

        public CustomWindow(Login loginSession, string title)
        {
            InitializeComponent();
            TitleCustomForm.Content = title;

            _login = loginSession;
        }

        private void MainWindowsClose(object sender, RoutedEventArgs e) =>
            Application.Current.Shutdown();

        private void HeaderMouse(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }
    }
}
