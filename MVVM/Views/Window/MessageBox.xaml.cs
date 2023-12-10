using BlockifyLauncher.MVVM.Views.Style.Effect;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BlockifyLauncher
{
    public partial class MessageBox : Window
    {
        public MessageBox(string Message, string TitleMessage = "Problem")
        {
            InitializeComponent();

            WindowFormBlur.SetIsEnabled(this, true);

            this.Title = TitleMessage;
            this.TitleContent.Content = TitleMessage;
            //this.MessageContent.Content = Message;
        }

        private void HeaderMouse(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void FormClose(object sender, RoutedEventArgs e) =>
            this.Close();
    }
}
