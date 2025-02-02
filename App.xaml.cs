using BlockifyLauncher.Core.DiscordActivy;
using BlockifyLauncher.Resources;
using System.Diagnostics;
using System.Windows;

namespace BlockifyLauncher
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static DiscordController _discordController { get; private set; }

        public App()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Debug.WriteLine(new Properties.Settings().GetLanguage());

            ResxLocalizationProvider.Instance.ChangeLanguage(
                new Properties.Settings().GetLanguage()
                );

            try
            {
                _discordController = new DiscordController();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            _discordController?.Stop();
        }


        /*
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
