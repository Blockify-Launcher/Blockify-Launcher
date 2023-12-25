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
            this.MessageContent.Content = Message;
        }

        private void MessageBobLoading(object sender, RoutedEventArgs e)
        {
            switch (__typeMessage)
            {
                case TypeMessage.Info:
                    this.Title = "Info";
                    this.TitleContent.Content = Title;
                    this.ImageMessage.Source = (ImageSource)FindResource("InfoDrawingImage");
                    break;
                case TypeMessage.Warning:
                    this.Title = "Warning";
                    this.TitleContent.Content = Title;
                    this.ImageMessage.Source = (ImageSource)FindResource("DangerDrawingImage");
                    break;
                case TypeMessage.Error:
                    this.Title = "Error";
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
