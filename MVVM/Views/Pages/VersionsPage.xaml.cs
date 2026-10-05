using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>Installed / available Minecraft versions and loaders (glass list).</summary>
    public partial class VersionsPage : Page
    {
        public class VRow
        {
            public string Name { get; set; } = "";
            public string Info { get; set; } = "";
            public string Badge { get; set; } = "";
            public string IconText { get; set; } = "";
            public Brush IconBrush { get; set; } = Brushes.Transparent;
            public Brush IconFg { get; set; } = Brushes.White;
            public string Kind { get; set; } = "release";
        }

        private List<VRow> _all = new();
        private bool _loaded;
        private string _filter = "all";

        public VersionsPage()
        {
            InitializeComponent();
        }

        private async void LoadingPage(object sender, RoutedEventArgs e)
        {
            if (_loaded) return;
            _loaded = true;

            var setting = new Properties.Settings();

            // 1) local versions render instantly — never blocked by the Mojang manifest
            try
            {
                var local = new BlockifyLib.Launcher.Version.Load.LocalVersionLoader(setting.launcher.MinecraftPath)
                    .GetVersionMetadatas();
                Rebuild(local);
                Apply(_filter);
            }
            catch { /* no local versions yet */ }

            // 2) merge in the full Mojang list once it arrives (may be slow / offline)
            StatusText.Text = "…";
            try
            {
                var all = await setting.launcher.GetAllVersionsAsync();
                Rebuild(all);
                Apply(_filter);
                StatusText.Text = "";
            }
            catch
            {
                StatusText.Text = BlockifyLauncher.Resources.ResxLocalizationProvider.Instance["packs_offline"];
            }
        }

        private static Brush Rgb(byte r, byte g, byte b, byte a = 0xFF) =>
            new SolidColorBrush(Color.FromArgb(a, r, g, b));

        private void Rebuild(IEnumerable<BlockifyLib.Launcher.Version.Metadata.VersionMetadata> versions)
        {
            _all.Clear();
            foreach (var v in versions)
            {
                string kind = v.Type ?? "release";
                (string icon, Brush bg, Brush fg) = kind switch
                {
                    "snapshot"  => ("SNP", Rgb(0x5E, 0xB8, 0xE6, 0x24), Rgb(0x5E, 0xB8, 0xE6)),
                    "old_beta"  => ("BETA", Rgb(0xF2, 0xC1, 0x4E, 0x24), Rgb(0xF2, 0xC1, 0x4E)),
                    "old_alpha" => ("ALFA", Rgb(0xF2, 0xC1, 0x4E, 0x24), Rgb(0xF2, 0xC1, 0x4E)),
                    _           => ("REL", Rgb(0x6B, 0xBF, 0x3B, 0x28), Rgb(0x6B, 0xBF, 0x3B)),
                };

                _all.Add(new VRow
                {
                    Name = v.Name,
                    Info = v.IsLocalVersion ? "Установлена локально" : kind,
                    Badge = v.IsLocalVersion ? "установлена" : "доступна",
                    IconText = icon,
                    IconBrush = bg,
                    IconFg = fg,
                    Kind = v.IsLocalVersion ? "release" : kind
                });
            }
        }

        private void FilterChecked(object sender, RoutedEventArgs e)
        {
            _filter = (string)((RadioButton)sender).Tag;
            if (!_loaded) return;
            Apply(_filter);
        }

        private void Apply(string filter)
        {
            IEnumerable<VRow> rows = filter switch
            {
                "release" => _all.Where(r => r.Kind == "release"),
                "snapshot" => _all.Where(r => r.Kind == "snapshot"),
                "old" => _all.Where(r => r.Kind is "old_beta" or "old_alpha"),
                _ => _all
            };
            VersionsList.ItemsSource = rows.Take(120).ToList();
        }
    }
}
