using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BlockifyLauncher.MVVM.Views.Components
{
    public partial class NotificationElement : UserControl
    {
        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(NotificationElement), new PropertyMetadata(string.Empty));

        public string Description
        {
            get { return (string)GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(NotificationElement), new PropertyMetadata(string.Empty));

        public NotificationElement()
        {
            InitializeComponent();
            DataContext = this;
            this.Opacity = 0;
        }

        private bool isOpacityNotificRunning = false;
        private async Task OpacityNotific(double numOpaciti, bool increasing)
        {
            if (isOpacityNotificRunning)
                return;
            else
                isOpacityNotificRunning = true;

            try
            {
                while (true)
                {
                    if (increasing)
                    {
                        this.Opacity += numOpaciti;

                        if (this.Opacity >= 1.0)
                        {
                            this.Opacity = 1.0;
                            return;
                        }
                    }
                    else
                    {
                        this.Opacity -= numOpaciti;

                        if (this.Opacity <= 0.0)
                        {
                            this.Opacity = 0.0;
                            return;
                        }
                    }
                    await Task.Delay(30);
                }
            }
            finally
            {
                isOpacityNotificRunning = false;
            }
        }

        public async Task GetNotification(string title, string description)
        {
            Title = title;
            Description = description;

            await OpacityNotific(0.1, true);
            await Task.Delay(1000);
            await OpacityNotific(0.1, false);
            await Task.Delay(10);
            this.Opacity = 0;
        }
    }
}
