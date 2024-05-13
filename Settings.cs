using BlockifyLauncher.MVVM.Views.Pages.Func.Setting;
using BlockifyLib;
using BlockifyLib.Launcher.Minecraft;
using BlockifyLib.Launcher.src;
using BlockifyLib.Launcher.Version;
using Microsoft.Windows.Themes;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Media.Media3D;

namespace BlockifyLauncher.Properties {
    
    /// <summary>
    /// Display struct;
    /// </summary>
    public struct Display { 
        public int w, h;
    }
    
    /// <summary>
    /// Launcher Setting.
    /// </summary>
    internal sealed partial class Settings {
        public Account accountSession;
        public BlockifyLibLauncher launcher; 
        public LaunchOption optionLaunch;
        public VersionCollection collectionVersion;
        public BlockifyLib.Launcher.Version.Version version;

        public MinecraftPath minecraftPath;
        public string javaPath;

        public Settings()
        {
            SettingsInitialize();
            this.PropertyChanged += PropertyChangedEventHandler;
        }

        public void SettingsInitialize()
        {
            this.launcher = new BlockifyLibLauncher(new MinecraftPath()); // TODO : Добавить смену расположения.
            this.accountSession = new Account();
        }
        
        private void PropertyChangedEventHandler(object sender, PropertyChangedEventArgs e) => 
            this.Save();

        /* Останній користувач в лаунчері */
        public void SetLastUser(string id) => this.UserLastID = id;
        public string GetLastUser() => this.UserLastID;

        /* Параметр відображення лаучнеру після відкривання гри */
        public void SetHideLauncher(int num) => this.HideLauncher = num;
        public int GetHideLauncher() => this.HideLauncher;

        /* Мова лаунчеру */
        public void SetLauguage(int num) => this.Language = num;
        public int GetLanguage() => this.Language;

        /* Хуй знает что за хуета */
        public void SetVersionDisplay(int num) => this.VersionDisplay = num;
        public int GetVersionDisplay() => this.VersionDisplay;

        /* Відкривання в повному екрані */
        public void SetFullScrean(bool val) => this.FullScreanGame = val;
        public bool GetFullScrean() => this.FullScreanGame;

        /* Виділення оперативної пам'яті */
        public void SetMemoryRAM(int RAM) => this.UseRam = RAM;
        public int  GetMemoryRAM() => this.UseRam;

        /* Розміри екрану гри */
        public void SetSettingDisplayGame(Display display)
        {
            this.ScreenWidth = display.w;
            this.ScreenHeight = display.h;
        }
        
        public Display GetSettingDisplayGame() => new Display { w = this.ScreenWidth, h = this.ScreenHeight };

        /* Розміри форми лаунчеру */
        public void SettingsSavingSizeForms(Display display)
        {
            this.WidthProgram = display.w;
            this.HeightProgram = display.h;
        }

        public void SettingsSavingSizeForms(int width, int height)
        {
            this.WidthProgram = width;
            this.HeightProgram = height;
        }
    }
}


/*
 
TODO : Задачи по настройкам

1. Добавить инициализацию переменных при открытие лаунчера.
 
 */