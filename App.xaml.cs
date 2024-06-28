using BlockifyLauncher.Core.DiscordActivy;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace BlockifyLauncher
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {

        }


        /*private DiscordController _discordController;
        private MainWindow _mainWindow;

        //Intialization Aplication.
        public App()
        {
            // start discord information 
            _discordController = new DiscordController();
            _mainWindow = new MainWindow(_discordController);

            _mainWindow.Show();
        }
        /*[STAThread]
        private static void InitializSetting()
        {
            new Properties.Settings().SettingsInitialize();
        }*/
    }

}
