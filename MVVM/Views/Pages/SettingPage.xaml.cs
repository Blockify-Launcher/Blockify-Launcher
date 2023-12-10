using CmlLib.Core;
using CmlLib.Core.Files;
using CmlLib.Utils;
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
    /// Логика взаимодействия для SettingPage.xaml
    /// </summary>
    public partial class SettingPage : Page
    {
        public struct Display
        {
            public int ScreenWidth; 
            public int ScreenHeight;

            public bool FullScrean;
        }

        public class Setting
        {
            CmlLib.Core.Version.MVersion    Version;
            CmlLib.Core.Auth.MSession       Session; 

            MinecraftPath       MinecraftPath;
            string              JavaPath;

            private int _maximumRamMb;
            public  int MaximumRamMB {
                get { return _maximumRamMb; } 
                set { _maximumRamMb = 1024 < value ? value : 1024; } 
            }

            Display screadFormat { get; set; }

            public Setting()
            {

            }
        }

        public SettingPage()
        {
            InitializeComponent();
            
            MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
            Blur_WindowBlur.BlurContainer = mainWindow.MainBorder;
        }
    }
}
