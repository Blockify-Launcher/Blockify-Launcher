using BlockifyLauncher.Core.Modrinth;
using BlockifyLauncher.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Modpack catalog (Modrinth): banner cards, loader/category/version
    /// filters, sorting and search. A click opens the pack page;
    /// one-click .mrpack install is the next phase.
    /// </summary>
    public partial class PacksPage : Page
    {
        private string _currentLoader = "";
        private string _currentCategory = "";
        private string _query = "";
        private bool _loaded;

        private static readonly string[] GameVersions =
            { "", "1.21.5", "1.21.1", "1.21", "1.20.1", "1.19.2", "1.18.2", "1.16.5", "1.12.2" };

        private static readonly (string key, string index)[] Sorts =
        {
            ("sort_downloads", "downloads"),
            ("sort_newest", "newest"),
            ("sort_updated", "updated"),
            ("sort_follows", "follows")
        };

        public PacksPage()
        {
            InitializeComponent();
        }

        private async void LoadingPage(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;

            var lang = ResxLocalizationProvider.Instance;

            foreach (var v in GameVersions)
                VersionFilter.Items.Add(new ComboBoxItem
                {
                    Content = v == "" ? lang["versions_any"] : v,
                    Tag = v
                });
            VersionFilter.SelectedIndex = 0;

            foreach (var (key, index) in Sorts)
                SortFilter.Items.Add(new ComboBoxItem { Content = lang[key], Tag = index });
            SortFilter.SelectedIndex = 0;

            _loaded = true;
            await LoadPacksAsync();
        }

        private async void FilterChecked(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            _currentLoader = (string)((RadioButton)sender).Tag;
            await LoadPacksAsync();
        }

        private async void CategoryChecked(object sender, RoutedEventArgs e)
        {
            if (!_loaded) return;
            _currentCategory = (string)((RadioButton)sender).Tag;
            await LoadPacksAsync();
        }

        private async void RefineChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded) return;
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
                string gameVersion = (VersionFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                string sortIndex = (SortFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "downloads";

                var packs = await ModrinthService.SearchAsync(
                    loader: string.IsNullOrEmpty(_currentLoader) ? null : _currentLoader,
                    query: string.IsNullOrEmpty(_query) ? null : _query,
                    category: string.IsNullOrEmpty(_currentCategory) ? null : _currentCategory,
                    gameVersion: string.IsNullOrEmpty(gameVersion) ? null : gameVersion,
                    sortIndex: string.IsNullOrEmpty(_query) ? sortIndex : null,
                    limit: 21);

                PacksList.ItemsSource = packs;
                StatusText.Text = packs.Count == 0 ? "0" : "";
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
