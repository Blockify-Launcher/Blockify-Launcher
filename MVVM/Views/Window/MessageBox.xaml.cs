using BlockifyLauncher.MVVM.Views.Style.Effect;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BlockifyLauncher
{
    public partial class MessageBox : Window
    {
        public enum TypeMessage
        {
            Info,
            Warning,
            Error
        }
        
        private TypeMessage __typeMessage;

        public MessageBox(string Message, TypeMessage typeMessage)
        {
            InitializeComponent();

            WindowFormBlur.SetIsEnabled(this, true);

            __typeMessage = typeMessage;
            // long messages wrap instead of running off the 400 px window
            this.MessageContent.Content = new TextBlock { Text = Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 290 };

            // centred over the launcher window when it is on screen (only a shown window can be an owner)
            try
            {
                var main = Application.Current?.MainWindow;
                if (main != null && !ReferenceEquals(main, this) && main.IsLoaded && main.IsVisible)
                {
                    Owner = main;
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch { }
        }

        private void MessageBobLoading(object sender, RoutedEventArgs e)
        {
            switch (__typeMessage)
            {
                case TypeMessage.Info:
                    this.Title = "Информация";
                    this.TitleContent.Content = Title;
                    this.ImageMessage.Source = (ImageSource)FindResource("InfoDrawingImage");
                    break;
                case TypeMessage.Warning:
                    this.Title = "Внимание";
                    this.TitleContent.Content = Title;
                    this.ImageMessage.Source = (ImageSource)FindResource("DangerDrawingImage");
                    break;
                case TypeMessage.Error:
                    this.Title = "Ошибка";
                    this.TitleContent.Content = Title;
                    this.ImageMessage.Source = (ImageSource)FindResource("ErrorDrawingImage");
                    break;
                default:
                    this.Close();
                    break;
            }
        }

        private void HeaderMouse(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void FormClose(object sender, RoutedEventArgs e) =>
            this.Close();
    }
}
