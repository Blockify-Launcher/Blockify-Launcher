using BlockifyLauncher.Core.Modrinth;
using BlockifyLauncher.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Modpack catalog (Modrinth): banner cards, loader filters and search.
    /// A click opens the pack page; one-click .mrpack install is the next phase.
    /// </summary>
    public partial class PacksPage : Page
    {
        private string _currentLoader = "";
        private string _query = "";
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

        private async void SearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _query = SearchBox.Text.Trim();
            await LoadPacksAsync();
        }

        private async Task LoadPacksAsync()
        {
            StatusText.Text = "…";
            try
            {
                var packs = await ModrinthService.SearchAsync(
                    string.IsNullOrEmpty(_currentLoader) ? null : _currentLoader,
                    string.IsNullOrEmpty(_query) ? null : _query,
                    limit: 21);
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
