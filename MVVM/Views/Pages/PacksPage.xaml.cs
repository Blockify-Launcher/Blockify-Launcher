using BlockifyLauncher.Core.Modrinth;
using BlockifyLauncher.Resources;
using System.Windows;
using System.Windows.Controls;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Modpack catalog (Modrinth). Browsing and opening pack pages for now;
    /// one-click .mrpack install is the next phase.
    /// </summary>
    public partial class PacksPage : Page
    {
        private MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
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
            if (!_loaded) return; // initial IsChecked fires before Loaded
            _currentLoader = (string)((RadioButton)sender).Tag;
            await LoadPacksAsync();
        }

        private async Task LoadPacksAsync()
        {
            StatusText.Text = "…";
            try
            {
                var packs = await ModrinthService.SearchAsync(
                    string.IsNullOrEmpty(_currentLoader) ? null : _currentLoader);
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
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pack.PageUrl)
                {
                    UseShellExecute = true
                });
            }
            catch { /* no browser handler */ }
        }
    }
}
