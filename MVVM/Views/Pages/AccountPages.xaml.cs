using BlockifyLauncher.MVVM.ViewModel.Pages.VisualFucn;
using BlockifyLauncher.Resources;
using BlockifyLib.Launcher.Microsoft;
using BlockifyLib.Launcher.Minecraft.Auth;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

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
                box.Text = ResxLocalizationProvider.Instance["none_account"];
            

            box.Items.Add(new Separator()
            {
                Style = (System.Windows.Style)
                        Application.Current.FindResource("MenuSeparatorStyle")
            });

            var navButton_account = new Button()
            {
                Style = (System.Windows.Style)
                        Application.Current.FindResource("ButtonComboBoxAccount"),
                Content = ResxLocalizationProvider.Instance["account_setting"]
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
                    Style = (System.Windows.Style)FindResource("ListBoxAccount_Item"),
                    ContextMenu = new ContextMenu()
                    {
                        Items = {
                            new MenuItem() {
                                Header = ResxLocalizationProvider.Instance["edit_button"]
                            },
                            new Separator() {
                                Style = (System.Windows.Style)FindResource("MenuSeparatorStyle")
                            },
                            new MenuItem() {
                                Header = ResxLocalizationProvider.Instance["delete_account"],
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
            try
            {
                new Properties.Settings().accountSession.EditUser(editElement.Id, UserNameTextBox.Text);
                GetAccount();

                Notification.Show(ResxLocalizationProvider.Instance["notif_edit_account"],
                    string.Format(ResxLocalizationProvider.Instance["notif_edit_account_body"], UserNameTextBox.Text));
                UpdateElement();

                UserNameTextBox.Text = string.Empty;
                SaveAccountButton.IsEnabled = false;
            }
                catch (Exception ex)
            {
                Notification.Show(ResxLocalizationProvider.Instance["error_title"], ex.Message);
                return;
            }
        }

        // Edit element contextMenu.
        private void ContextMenuEdit(ListBoxItem item)
        {
            UserNameTextBox.Text = item.Content.ToString();
            editElement = (SessionStruct)item.Tag;
            LoginNotPassword.IsChecked = true;
            SaveAccountButton.IsEnabled = true;
        }

        // Delete element contextMenu.
        private void ContextMenuDelete(ListBoxItem item)
        {
            AccountList.Items.Remove(item);
            new Properties.Settings().accountSession.RemoveUser(((SessionStruct)item.Tag).Id);
            UpdateElement();
        }

        // Add new user.
        private async void ButtonClickAddNewAccount(object sender, RoutedEventArgs e)
        {
            var lang = ResxLocalizationProvider.Instance;

            if (LoginMicrosoft.IsChecked == true)
            {
                var button = (Button)sender;
                button.IsEnabled = false;
                try
                {
                    var loginHandler = JELoginHandlerBuilder.BuildDefault();
                    var session = await loginHandler.AuthenticateInteractively();

                    if (!session.CheckIsValid())
                        throw new InvalidOperationException(lang["error_microsoft_login"]);

                    // Stable id so re-login updates the same account.
                    session.Id = "msa_" + (session.Xuid ?? session.UUID);
                    new Properties.Settings().accountSession.CreateUser(session);
                    GetAccount();

                    Notification.Show(lang["notif_create_account"],
                        string.Format(lang["notif_create_account_body"], session.Username));
                    UpdateElement();
                }
                catch (Exception ex)
                {
                    Notification.Show(lang["error_title"], ex.Message);
                }
                finally
                {
                    button.IsEnabled = true;
                }
                return;
            }

            var UserName = UserNameTextBox.Text;
            if (string.IsNullOrWhiteSpace(UserName))
            {
                Notification.Show(lang["error_title"], lang["error_empty_username"]);
                return;
            }

            new Properties.Settings().accountSession.CreateUser(UserName);
            GetAccount();

            Notification.Show(lang["notif_create_account"],
                string.Format(lang["notif_create_account_body"], UserName));
            UpdateElement();
        }

        //private void Notification(string Title, string Description = "None") =>
         //    _ = mainWindow.NotificationElement.GetNotification(Title, Description);

    }
}
