using System.Windows;
using System.Windows.Controls;

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
            DependencyProperty.Register("Activ", typeof(string), typeof(LoaderComponent), new PropertyMetadata(string.Empty));

        public LoaderComponent()
        {
            InitializeComponent();
            DataContext = this;
        }
    }
}
