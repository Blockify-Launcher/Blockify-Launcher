using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Modpack builder: pick core, add mods from the catalog (dependencies
    /// pull in automatically), watch live stats. Interactive mock — a real
    /// .mrpack export/resolve backend comes later.
    /// </summary>
    public partial class BuilderPage : Page
    {
        public class ModEntry
        {
            public string Name { get; set; } = "";
            public string Icon { get; set; } = "";
            public string Meta { get; set; } = "";
            public double Size { get; set; }
            public string? Dep { get; set; }
        }

        private static readonly ModEntry[] Catalog =
        {
            new(){ Name="Sodium", Icon="⚡", Meta="оптимизация · 38M · 1.3 МБ", Size=1.3 },
            new(){ Name="Create", Icon="⚙", Meta="техника · 24M · 14 МБ · требует Flywheel", Size=14, Dep="Flywheel" },
            new(){ Name="Just Enough Items", Icon="📖", Meta="интерфейс · 31M · 2.1 МБ", Size=2.1 },
            new(){ Name="Applied Energistics 2", Icon="💾", Meta="техника · 18M · 9.4 МБ", Size=9.4 },
            new(){ Name="Botania", Icon="🌸", Meta="магия · 15M · 11 МБ · требует Patchouli", Size=11, Dep="Patchouli" },
            new(){ Name="Iris Shaders", Icon="🌈", Meta="графика · 22M · 3.2 МБ", Size=3.2 },
            new(){ Name="Lithium", Icon="🔋", Meta="оптимизация · 19M · 0.8 МБ", Size=0.8 },
            new(){ Name="Xaeros Minimap", Icon="🗺", Meta="интерфейс · 27M · 1.9 МБ", Size=1.9 },
        };

        private readonly HashSet<string> _added = new();
        private int _mods;
        private double _size;

        public BuilderPage()
        {
            InitializeComponent();
            ModList.ItemsSource = Catalog;
        }

        private void FilterMods(object sender, TextChangedEventArgs e)
        {
            string q = ModSearch.Text.Trim().ToLowerInvariant();
            ModList.ItemsSource = string.IsNullOrEmpty(q)
                ? Catalog
                : Catalog.Where(m => m.Name.ToLowerInvariant().Contains(q) || m.Meta.ToLowerInvariant().Contains(q)).ToList();
        }

        private void AddModClick(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).Tag is not ModEntry m) return;
            AddSlot(m.Name, m.Size, false);
            if (!string.IsNullOrEmpty(m.Dep) && !_added.Contains(m.Dep))
                AddSlot(m.Dep!, 0.6, true);
            UpdateStats();
            CheckCompat();
        }

        private void AddSlot(string name, double size, bool isDep)
        {
            if (_added.Contains(name)) return;
            _added.Add(name);
            _mods++; _size += size;
            EmptyHint.Visibility = Visibility.Collapsed;

            var row = new Border
            {
                Background = Rgb(0x0A, 0xFF, 0xFF, 0xFF),
                BorderBrush = isDep ? Rgb(0x4D, 0xF2, 0xC1, 0x4E) : (Brush)FindResource("G.Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 8, 8, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Tag = name
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = name, Foreground = (Brush)FindResource("G.Text"), FontSize = 12 });
            if (isDep)
                label.Children.Add(new TextBlock { Text = " · зависимость", Foreground = (Brush)FindResource("G.Gold"), FontFamily = (FontFamily)FindResource("G.Mono"), FontSize = 9, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(label, 0);

            var sz = new TextBlock { Text = size.ToString("0.#") + " МБ", Foreground = (Brush)FindResource("G.Muted"), FontFamily = (FontFamily)FindResource("G.Mono"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            Grid.SetColumn(sz, 1);

            var rm = new Button { Content = "✕", Foreground = (Brush)FindResource("G.Danger"), Cursor = System.Windows.Input.Cursors.Hand, FontSize = 12, Tag = name, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            rm.Template = MakeBareButton();
            rm.Click += RemoveSlot;
            Grid.SetColumn(rm, 2);

            g.Children.Add(label); g.Children.Add(sz); g.Children.Add(rm);
            row.Child = g;
            SlotList.Children.Add(row);
        }

        private void RemoveSlot(object sender, RoutedEventArgs e)
        {
            string name = (string)((Button)sender).Tag;
            for (int i = 0; i < SlotList.Children.Count; i++)
                if (SlotList.Children[i] is Border b && (string)(b.Tag ?? "") == name)
                {
                    var cat = Catalog.FirstOrDefault(m => m.Name == name);
                    _size -= cat?.Size ?? 0.6;
                    _mods--;
                    _added.Remove(name);
                    SlotList.Children.RemoveAt(i);
                    break;
                }
            if (_mods <= 0) { _mods = 0; _size = 0; EmptyHint.Visibility = Visibility.Visible; }
            UpdateStats();
            CheckCompat();
        }

        private void UpdateStats()
        {
            StatMods.Text = "модов: " + _mods;
            StatSize.Text = "размер: " + _size.ToString("0.#") + " МБ";
        }

        private void RefreshTarget(object sender, SelectionChangedEventArgs e)
        {
            if (StatTarget == null) return;
            StatTarget.Text = $"цель: {Sel(BVer)} · {Sel(BLoader)}";
            CheckCompat();
        }

        private static string Sel(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        private void CheckCompat()
        {
            // demo rule: Create needs Forge/NeoForge
            bool bad = Sel(BLoader) == "Fabric" && _added.Contains("Create");
            CompatText.Text = bad ? "совместимость: Create требует Forge/NeoForge" : "совместимость: ok";
            var c = bad ? (Brush)FindResource("G.Gold") : (Brush)FindResource("G.Primary");
            CompatText.Foreground = c; CompatDot.Fill = c;
        }

        private void ExportClick(object sender, RoutedEventArgs e)
        {
            if (_mods == 0) { Flash(ExportBtn, "Сначала добавь моды", "Экспорт .mrpack"); return; }
            Flash(ExportBtn, "✓ " + BName.Text + ".mrpack", "Экспорт .mrpack");
        }

        private void BuildClick(object sender, RoutedEventArgs e)
        {
            if (_mods == 0) { Flash(BuildBtn, "Сборка пуста", "Собрать и играть"); return; }
            Flash(BuildBtn, "✓ Готово", "Собрать и играть");
        }

        private static async void Flash(ContentControl b, string tmp, string back)
        {
            b.Content = tmp;
            await System.Threading.Tasks.Task.Delay(1600);
            b.Content = back;
        }

        private static SolidColorBrush Rgb(byte r, byte g, byte bl, byte a = 0xFF) =>
            new SolidColorBrush(Color.FromArgb(a, r, g, bl));

        private static ControlTemplate MakeBareButton()
        {
            var t = new ControlTemplate(typeof(Button));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            t.VisualTree = cp;
            return t;
        }
    }
}
