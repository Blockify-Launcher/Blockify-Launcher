using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BlockifyLauncher.MVVM.Views.Components
{
    public partial class LoaderComponent : UserControl
    {
        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(LoaderComponent), new PropertyMetadata(string.Empty));

        public string Description
        {
            get { return (string)GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(LoaderComponent), new PropertyMetadata(string.Empty));

        public double Value
        {
            get { return (double)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(double), typeof(LoaderComponent), new PropertyMetadata(null));

        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register("Maximum", typeof(double), typeof(LoaderComponent), new PropertyMetadata(null));

        public string Activ 
        {
            get { return (string)GetValue(ActivProperty); }
            set { SetValue(ActivProperty, value); }
        }

        public static readonly DependencyProperty ActivProperty =
            DependencyProperty.Register("Activ", typeof(string), typeof(LoaderComponent),
                new PropertyMetadata(string.Empty, OnActivChanged));

        private static void OnActivChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if ((string)e.NewValue == "Use")
                ((LoaderComponent)d).PlayDropIn();
        }

        private readonly TranslateTransform _drop = new();

        public LoaderComponent()
        {
            InitializeComponent();
            DataContext = this;
            RenderTransform = _drop;
        }

        // A1 "block drop" entrance, matching the notification toast
        private void PlayDropIn()
        {
            var y = new DoubleAnimationUsingKeyFrames();
            y.KeyFrames.Add(new EasingDoubleKeyFrame(70, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.28)),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.4)),
                new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            y.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5)),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            _drop.BeginAnimation(TranslateTransform.YProperty, y);
        }
    }
}
