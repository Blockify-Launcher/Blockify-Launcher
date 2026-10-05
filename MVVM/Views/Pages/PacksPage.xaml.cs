using BlockifyLauncher.Core.Modrinth;
using BlockifyLauncher.Resources;
using System.Windows;
using System.Windows.Controls;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>Modpack catalog (Modrinth) in the Liquid Glass style.</summary>
    public partial class PacksPage : Page
    {
        private string _currentLoader = "";
        private bool _loaded;

        public PacksPage()
        {
            InitializeComponent();
        }

        private async void LoadingPage(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;
            await LoadPacksAsync();
        }

        private async void FilterChecked(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            _currentLoader = (string)((RadioButton)sender).Tag;
            await LoadPacksAsync();
        }

        private async System.Threading.Tasks.Task LoadPacksAsync()
        {
            StatusText.Text = "…";
            try
            {
                var packs = await ModrinthService.SearchAsync(
                    string.IsNullOrEmpty(_currentLoader) ? null : _currentLoader, limit: 21);
                PacksList.ItemsSource = packs;
                StatusText.Text = "";
            }
            catch
            {
                PacksList.ItemsSource = null;
                StatusText.Text = ResxLocalizationProvider.Instance["packs_offline"];
            }
        }

        private void OpenPackPage(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not ModpackInfo pack) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pack.PageUrl) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
