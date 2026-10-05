using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BlockifyLauncher.MVVM.Views.Components
{
    /// <summary>
    /// Toast notification with the "block drop" entrance: falls from above,
    /// bounces once, holds, then fades away upward.
    /// </summary>
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

        private readonly TranslateTransform _drop = new();
        private bool _isRunning;

        public NotificationElement()
        {
            InitializeComponent();
            DataContext = this;
            RenderTransform = _drop;
            this.Opacity = 0;
        }

        public async Task GetNotification(string title, string description, int delay = 1000)
        {
            Title = title;
            Description = description;

            if (_isRunning) return; // texts updated, current toast keeps showing
            _isRunning = true;
            try
            {
                await PlayDropInAsync();
                await Task.Delay(delay + 600);
                await PlayFadeOutAsync();
            }
            finally
            {
                this.Opacity = 0;
                _isRunning = false;
            }
        }

        public Task GetNotification(string title, string description, string image, int delay = 1000) =>
            GetNotification(title, description, delay);

        // A1 "block drop": falls from above and springs like a dropped item
        private Task PlayDropInAsync()
        {
            var y = new DoubleAnimationUsingKeyFrames();
            y.KeyFrames.Add(new EasingDoubleKeyFrame(-70, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.28)),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(-3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.4)),
                new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5)),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.22));

            var tcs = new TaskCompletionSource();
            y.Completed += (_, _) => tcs.TrySetResult();

            _drop.BeginAnimation(TranslateTransform.YProperty, y);
            BeginAnimation(OpacityProperty, fade);
            return tcs.Task;
        }

        private Task PlayFadeOutAsync()
        {
            var y = new DoubleAnimation(0, -16, TimeSpan.FromSeconds(0.28))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.28));

            var tcs = new TaskCompletionSource();
            fade.Completed += (_, _) => tcs.TrySetResult();

            _drop.BeginAnimation(TranslateTransform.YProperty, y);
            BeginAnimation(OpacityProperty, fade);
            return tcs.Task;
        }
    }
}
