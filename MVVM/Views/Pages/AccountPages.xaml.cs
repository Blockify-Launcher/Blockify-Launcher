using BlockifyLib.Launcher.Minecraft.Auth;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Interaction logic for AccountPages.xaml
    /// </summary>
    public partial class AccountPages : Page
    {
        private MainWindow mainWindow;

        public AccountPages()
        {
            InitializeComponent();

            mainWindow = (MainWindow)Application.Current.MainWindow;

            SetBlurContainer();
            GetAccount();
        }

        // Update elements after each action.
        private void UpdateElement()
        {
            var box = mainWindow.MinecraftAccountComboBox;
            int index = box.SelectedIndex;
            box.Items.Clear();
            foreach (var us in new Properties.Settings().accountSession.GetAllUserList())
            {
                box.Items.Add(us.Username);
            }
            box.SelectedIndex = index >= box.Items.Count ? 0 : index;
        }

        private void SetBlurContainer()
        {
            Blur_WindowBlur.BlurContainer = mainWindow.MainBorder;
        }

        private void GetAccount()
        {
            AccountList.Items.Clear();
            foreach (var us in new Properties.Settings().accountSession.GetAllUserList())
            {
                var listBoxItem = new ListBoxItem()
                {
                    Content = us.Username,
                    Tag = us,
                    ContextMenu = new ContextMenu()
                    {
                        Items = {
                            new MenuItem() {
                                Header = "Edit"
                            },
                            new Separator() {
                                Style = (System.Windows.Style)FindResource("MenuSeparatorStyle")
                            },
                            new MenuItem() {
                                Header = "Delete account",
                                Foreground = new SolidColorBrush (Color.FromRgb(200,50,50))
                            }
                        }
                    }
                };
                ((MenuItem)listBoxItem.ContextMenu.Items[2]).Click += (sender, args) =>
                    ContextMenuDelete(listBoxItem);
                AccountList.Items.Add(listBoxItem);
            }
        }

        // Delete element contextMenu.
        private void ContextMenuDelete(ListBoxItem item)
        {
            AccountList.Items.Remove(item);
            new Properties.Settings().accountSession.RemoveUser(((SessionStruct)item.Tag).Id);
            UpdateElement();
        }

        // Add new user.
        private void ButtonClickAddNewAccount(object sender, RoutedEventArgs e)
        {
            var UserName = UserNameTextBox.Text;
            if (LoginNotPassword.IsChecked == true)
            {
                new Properties.Settings().accountSession.CreateUser(UserName);
                GetAccount();
            }
            else if (LoginMicrosoft.IsChecked == true)
            {
                var login = new Login();

                // new CustomWindow(new BlockifyLib.Launcher.Microsoft.Auth.Login(), "Auntification Microsoft").ShowDialog();
            }
            else if (LoginMojang.IsChecked == true)
            {
                //new CustomWindow("Auntification Mojang").ShowDialog();
            }
            else
            {
                return; // Close method.
            }
            UpdateElement();
            Notification("Create new account", $"a new account has been created with the name \"{UserName}\"");
        }

        private void Notification(string Title, string Description = "None") =>
             _ = mainWindow.NotificationElement.GetNotification(Title, Description);

    }
}
