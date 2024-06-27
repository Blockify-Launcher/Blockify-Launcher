using BlockifyLib.Launcher.Microsoft;
using BlockifyLib.Launcher.Minecraft.Auth;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace BlockifyLauncher.MVVM.Views.Pages
{
    /// <summary>
    /// Interaction logic for AccountPages.xaml
    /// </summary>
    public partial class AccountPages : Page
    {
        private MainWindow mainWindow;
        private SessionStruct editElement;

        public AccountPages()
        {
            InitializeComponent();

            mainWindow = (MainWindow)Application.Current.MainWindow;

            LastPageButton.SetBinding(Button.CommandProperty, new Binding("LastPageCommand"));

            SetBlurContainer();
            GetAccount();
        }

        // Update elements after each action.
        private void UpdateElement()
        {
            var box = mainWindow.MinecraftAccountComboBox;
            int index = box.SelectedIndex > 0 ? box.SelectedIndex : 0;
            
            box.Items.Clear();


            foreach (var us in new Properties.Settings().accountSession.GetAllUserList())
            {
                box.Items.Add(us.Username);
            }

            if (box.Items.Count > 0)
                box.SelectedIndex = index >= box.Items.Count ? 0 : index;
            else
                box.Text = "None...";
            

            box.Items.Add(new Separator()
            {
                Style = (System.Windows.Style)
                        Application.Current.FindResource("MenuSeparatorStyle")
            });

            var navButton_account = new Button()
            {
                Style = (System.Windows.Style)
                        Application.Current.FindResource("ButtonComboBoxAccount"),
                Content = "Account Setting"
            };
            navButton_account.SetBinding(Button.CommandProperty, new Binding("AccountCommand"));
            box.Items.Add(navButton_account);
        }

        private void SetBlurContainer()
        {
            Blur_WindowBlur.BlurContainer = mainWindow.MainBorder;
        }

        private void GetAccount()
        {
            AccountList.Items.Clear();
            foreach (var us in new Properties.Settings().accountSession.GetAllUserArray())
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
                ((MenuItem)listBoxItem.ContextMenu.Items[0]).Click += (sender, args) =>
                    ContextMenuEdit(listBoxItem);
                ((MenuItem)listBoxItem.ContextMenu.Items[2]).Click += (sender, args) =>
                    ContextMenuDelete(listBoxItem);
                AccountList.Items.Add(listBoxItem);
            }
        }

        private void SaveUserAccount(object sender, RoutedEventArgs e)
        {
            new Properties.Settings().accountSession.EditUser(editElement.Id, UserNameTextBox.Text);
            GetAccount();

            Notification("Edit account", $"a new username has been set up \"{UserNameTextBox.Text}\"");
            UpdateElement();
        }

        // Edit element contextMenu.
        private void ContextMenuEdit(ListBoxItem item)
        {
            UserNameTextBox.Text = item.Content.ToString();
            editElement = (SessionStruct)item.Tag;
            LoginNotPassword.IsChecked = true;
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
            new Properties.Settings().accountSession.CreateUser(UserName);
            GetAccount();

            Notification("Create new account", $"a new account has been created with the name \"{UserName}\"");
            UpdateElement();
        }

        private void Notification(string Title, string Description = "None") =>
             _ = mainWindow.NotificationElement.GetNotification(Title, Description);

    }
}
