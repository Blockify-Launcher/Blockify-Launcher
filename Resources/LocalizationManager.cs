using System.Globalization;
using System.Resources;
using System.Windows;

namespace BlockifyLauncher.Resources
{
    public static class LocalizationManager
    {
        private static readonly ResourceDictionary ResourceDict = new ResourceDictionary();

        private static readonly ResourceManager resourceManager = 
            new ResourceManager("BlockifyLauncher.Resources.Strings", typeof(MainWindow).Assembly);

        public static CultureInfo CurrentCulture { get; set; } = CultureInfo.CurrentCulture;

        public static void SetCulture(CultureInfo culture)
        {
            CurrentCulture = culture;
            ResourceDict.Source =
                new Uri($"/BlockifyLauncher;component/Resources/Strings.{culture.Name}.resx", UriKind.Relative);
            Application.Current.Resources.MergedDictionaries.Add(ResourceDict);
        }

        public static string GetString(string key, CultureInfo culture) =>
            resourceManager.GetString(key, culture);
    }
}
