using System.ComponentModel;
using System.Resources;

namespace BlockifyLauncher.Resources
{
    public class ResxLocalizationProvider : INotifyPropertyChanged
    {
        private static ResxLocalizationProvider _instance;

        public static ResxLocalizationProvider Instance =>
            _instance ??= new ResxLocalizationProvider();

        private ResourceManager _resourceManager = new ResourceManager("BlockifyLauncher.Resources.Strings", typeof(ResxLocalizationProvider).Assembly);

        public event PropertyChangedEventHandler PropertyChanged;

        public string this[string key] =>
            _resourceManager.GetString(key, Thread.CurrentThread.CurrentUICulture);

        public void ChangeLanguage(string culture)
        {
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(culture);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }
}
