using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace BlockifyLauncher.MVVM.Views.Style.Effect
{
    public class WindowBlur : UserControl
    {
        #region Fields.

        private Rectangle _blur;

        private Size _containerSize;
        private Point _difference;
        private double _scaleX;
        private double _scaleY;

        private const bool ShowUglyCurser = false;

        #endregion

        #region Dependecy Properties

        public static readonly DependencyProperty BlurContainerProperty = DependencyProperty.Register(
            "BlurContainer", typeof(UIElement), typeof(WindowBlur),
            new PropertyMetadata(default(UIElement), OnBlurContainerChanged));

        private static void OnBlurContainerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var blurryUserControl = (WindowBlur)d;
            blurryUserControl.UpdateVisual(e.OldValue as UIElement);
        }

        public UIElement BlurContainer
        {
            get => (UIElement)GetValue(BlurContainerProperty);
            set => SetValue(BlurContainerProperty, value);
        }

        public int BlurRadius
        {
            get => (int)GetValue(BlurRadiusProperty);
            set => SetValue(BlurRadiusProperty, value);
        }

        public static readonly DependencyProperty BlurRadiusProperty = DependencyProperty.Register(
            "BlurRadius", typeof(int), typeof(WindowBlur), new PropertyMetadata(10, OnBlurRadiusChanged));

        private static void OnBlurRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var blurryUserControl = (WindowBlur)d;
            blurryUserControl.UpdateVisual();
        }

        public static readonly DependencyProperty RenderingBiasProperty = DependencyProperty.Register(
            "RenderingBias", typeof(RenderingBias), typeof(WindowBlur),
            new PropertyMetadata(RenderingBias.Quality, OnRenderingBiasChanged));

        private static void OnRenderingBiasChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var blurryUserControl = (WindowBlur)d;
            blurryUserControl.UpdateVisual();
        }

        public RenderingBias RenderingBias
        {
            get => (RenderingBias)GetValue(RenderingBiasProperty);
            set => SetValue(RenderingBiasProperty, value);
        }

        public static readonly DependencyProperty MagnificationProperty = DependencyProperty.Register(
            "Magnification", typeof(double), typeof(WindowBlur), new PropertyMetadata(1.0d, OnMagnificationChanged));

        private static void OnMagnificationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var blurryUserControl = (WindowBlur)d;
            blurryUserControl.UpdateVisual();
        }

        public double Magnification
        {
            get => (double)GetValue(MagnificationProperty);
            set => SetValue(MagnificationProperty, value);
        }

        private static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
            "Brush", typeof(VisualBrush), typeof(WindowBlur), new PropertyMetadata());

        private VisualBrush Brush
        {
            get => (VisualBrush)GetValue(BrushProperty);
            set => SetValue(BrushProperty, value);
        }

        #endregion

        #region Constructor

        static WindowBlur()
        {
            //ensure loading template of BlurryUserControl defined in Themes/Generic.xaml
            DefaultStyleKeyProperty.OverrideMetadata(typeof(WindowBlur),
                new FrameworkPropertyMetadata(typeof(WindowBlur)));
        }

        public WindowBlur()
        {
            Loaded += OnLoaded;

            Background = new SolidColorBrush(Colors.WhiteSmoke) { Opacity = 0.2 };
        }

        private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            UpdateVisual();
        }

        public override void OnApplyTemplate()
        {
            //initialize visual parts
            _blur = (Rectangle)GetTemplateChild("Blur");

            _blur?.SetBinding(Shape.FillProperty,
                new Binding { Source = this, Path = new PropertyPath(BrushProperty) });

            base.OnApplyTemplate();
        }

        #endregion

        #region Blur

        private void RefreshBounds()
        {
            if (_blur == null || BlurContainer == null || Brush == null) return;
            _difference = _blur.TranslatePoint(new Point(), BlurContainer);
            _scaleX = 1 + 2 * BlurRadius * Magnification / RenderSize.Width;
            _scaleY = 1 + 2 * BlurRadius * Magnification / RenderSize.Height;

            var renderSize = new Size(RenderSize.Width * _scaleX, RenderSize.Height * _scaleY);
            Brush.Viewbox = new Rect(_difference, renderSize);

            _containerSize = BlurContainer.RenderSize;
        }

        private void RefreshEffect()
        {
            if (_blur == null) return;
            _blur.Effect = new BlurEffect
            {
                Radius = BlurRadius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias
            };

            _blur.RenderTransform = new MatrixTransform(_scaleX, 0, 0, _scaleY, -BlurRadius * Magnification, -BlurRadius * Magnification);
        }

        private void UpdateVisual(UIElement oldBlurContainer = null)
        {
            if (oldBlurContainer != null)
                oldBlurContainer.LayoutUpdated -= OnContainerLayoutUpdated;

            if (BlurContainer != null && _blur != null)
            {
                Brush = new VisualBrush(BlurContainer)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.None
                };

                BlurContainer.LayoutUpdated += OnContainerLayoutUpdated;
                RefreshBounds();
                RefreshEffect();
            }
            else
            {
                Brush = null;
            }
        }

        private void OnContainerLayoutUpdated(object sender, EventArgs eventArgs)
        {
            RefreshBounds();
        }

        #endregion
    }
}