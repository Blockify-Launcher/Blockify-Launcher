using System.ComponentModel;

namespace BlockifyLauncher.Properties {
    /// <summary>
    /// Launcher Setting.
    /// </summary>
    internal sealed partial class Settings {
        public Settings()
        {
            this.PropertyChanged += PropertyChangedEventHandler;
        }

        private void PropertyChangedEventHandler(object sender, PropertyChangedEventArgs e) => 
            this.Save();

        public void SetHideLauncher(int num) => this.HideLauncher = num;
        public int GetHideLauncher() => this.HideLauncher;

        public void SetLauguage(int num) => this.Language = num;
        public int GetLanguage() => this.Language;

        public void SetVersionDisplay(int num) => this.VersionDisplay = num;
        public int GetVersionDisplay() => this.VersionDisplay;

        public void SetUserName(string userName) => this.UserName = userName;
        public string GetUserName() => this.UserName;

        public void SettingsSavingSizeForms(int width, int height)
        {
            this.WidthProgram = width;
            this.HeightProgram = height;
        }
    }
}
