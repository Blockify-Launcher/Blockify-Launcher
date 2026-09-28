using BlockifyLauncher.MVVM.ViewModel.Pages.VisualFucn;
using BlockifyLauncher.Resources;
using BlockifyLib.Launcher.Microsoft;
using BlockifyLib.Launcher.Minecraft.Auth;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>Accounts screen (Liquid Glass): list + Microsoft / offline add cards.</summary>
    public partial class AccountPages : Page
    {
        private MainWindow mainWindow;

        public AccountPages()
        {
            InitializeComponent();
            mainWindow = (MainWindow)Application.Current.MainWindow;
            GetAccount();
        }

        private static Brush Rgb(byte r, byte g, byte b, byte a = 0xFF) =>
            new SolidColorBrush(Color.FromArgb(a, r, g, b));

        // Sync the hidden combo the launch path reads from.
        private void UpdateElement()
        {
            var box = mainWindow.MinecraftAccountComboBox;
            int index = box.SelectedIndex > 0 ? box.SelectedIndex : 0;
            box.Items.Clear();
            foreach (var us in new Properties.Settings().accountSession.GetAllUserList())
                box.Items.Add(us.Username);
            if (box.Items.Count > 0)
                box.SelectedIndex = index >= box.Items.Count ? 0 : index;
            mainWindow.RefreshAccountCard();
        }

        private void GetAccount()
        {
            AccountList.Children.Clear();
            string lastId = new Properties.Settings().GetLastUser();

            foreach (var us in new Properties.Settings().accountSession.GetAllUserArray())
                AccountList.Children.Add(BuildRow(us, us.Id == lastId));
        }

        private UIElement BuildRow(SessionStruct us, bool active)
        {
            var lang = ResxLocalizationProvider.Instance;
            bool msa = us.UserType == "msa";

            var row = new Border
            {
                Background = (Brush)FindResource("G.Glass2"),
                BorderBrush = (Brush)FindResource("G.Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(15, 13, 15, 13),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var skin = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Background = msa ? Rgb(0x2E, 0x8B, 0x57) : Rgb(0x8D, 0x6B, 0x4F) };
            Grid.SetColumn(skin, 0);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = us.Username, Foreground = (Brush)FindResource("G.Text"), FontSize = 15, FontWeight = FontWeights.Bold });
            info.Children.Add(new TextBlock { Text = msa ? "Microsoft" : lang["login_without_password"], Foreground = (Brush)FindResource("G.Muted"), FontFamily = (FontFamily)FindResource("G.Mono"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(info, 2);

            var badge = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 3, 9, 3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            if (active)
            {
                badge.Background = Rgb(0x6B, 0xBF, 0x3B, 0x28);
                badge.Child = new TextBlock { Text = "активен", Foreground = (Brush)FindResource("G.Primary"), FontFamily = (FontFamily)FindResource("G.Mono"), FontSize = 10 };
            }
            else
            {
                badge.Background = msa ? Rgb(0x5E, 0xB8, 0xE6, 0x24) : Rgb(0xF2, 0xC1, 0x4E, 0x24);
                badge.Child = new TextBlock { Text = msa ? "Microsoft" : "оффлайн", Foreground = msa ? (Brush)FindResource("G.Info") : (Brush)FindResource("G.Gold"), FontFamily = (FontFamily)FindResource("G.Mono"), FontSize = 10 };
            }
            Grid.SetColumn(badge, 3);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!active)
            {
                var makeActive = new Button { Style = (System.Windows.Style)FindResource("GlassButton"), Content = "Сделать активным", Margin = new Thickness(0, 0, 8, 0) };
                makeActive.Click += (_, _) => { new Properties.Settings().SetLastUser(us.Id); UpdateElement(); GetAccount(); };
                actions.Children.Add(makeActive);
            }
            var del = new Button { Style = (System.Windows.Style)FindResource("GlassButton"), Content = "Убрать", Foreground = (Brush)FindResource("G.Danger") };
            del.Click += (_, _) =>
            {
                new Properties.Settings().accountSession.RemoveUser(us.Id);
                UpdateElement(); GetAccount();
            };
            actions.Children.Add(del);
            Grid.SetColumn(actions, 4);

            g.Children.Add(skin); g.Children.Add(info); g.Children.Add(badge); g.Children.Add(actions);
            row.Child = g;
            return row;
        }

        private async void MicrosoftLogin(object sender, RoutedEventArgs e)
        {
            var lang = ResxLocalizationProvider.Instance;
            MsLoginBtn.IsEnabled = false;
            try
            {
                var loginHandler = JELoginHandlerBuilder.BuildDefault();
                var session = await loginHandler.AuthenticateInteractively();
                if (!session.CheckIsValid())
                    throw new InvalidOperationException(lang["error_microsoft_login"]);

                session.Id = "msa_" + (session.Xuid ?? session.UUID);
                new Properties.Settings().accountSession.CreateUser(session);
                GetAccount(); UpdateElement();
                Notification.Show(lang["notif_create_account"], string.Format(lang["notif_create_account_body"], session.Username));
            }
            catch (Exception ex)
            {
                Notification.Show(lang["error_title"], ex.Message);
            }
            finally { MsLoginBtn.IsEnabled = true; }
        }

        private void AddOffline(object sender, RoutedEventArgs e)
        {
            var lang = ResxLocalizationProvider.Instance;
            string nick = OffNick.Text.Trim();
            if (string.IsNullOrWhiteSpace(nick))
            {
                Notification.Show(lang["error_title"], lang["error_empty_username"]);
                return;
            }
            new Properties.Settings().accountSession.CreateUser(nick);
            OffNick.Text = "";
            GetAccount(); UpdateElement();
            Notification.Show(lang["notif_create_account"], string.Format(lang["notif_create_account_body"], nick));
        }
    }
}
