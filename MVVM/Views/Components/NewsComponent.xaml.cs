using BlockifyLauncher.MVVM.Views.Pages.Func.Main;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlockifyLauncher.MVVM.Views.Components
{
    public partial class NewsComponent : UserControl
    {
        public ImageSource ImageSour
        {
            get { return (ImageSource)GetValue(ImageProperty); }
            set { SetValue(ImageProperty, value); }
        }

        public static readonly DependencyProperty ImageProperty =
            DependencyProperty.Register("Image", typeof(ImageSource), typeof(NewsComponent), new PropertyMetadata(null));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(NewsComponent), new PropertyMetadata(string.Empty));

        public string Description
        {
            get { return (string)GetValue(DescriptionProperty); }
            set { SetValue(DescriptionProperty, value); }
        }

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(NewsComponent), new PropertyMetadata(string.Empty));

        public NewsComponent(NewsStr news)
        {
            InitializeComponent();

            DataContext = this;

            ImageSour = ConvertToImageSource(news.Image);
            Title = news.Name;
            Description = news.Description;
        }

        public NewsComponent(Core.News.NewsItem item)
        {
            InitializeComponent();

            DataContext = this;

            Title = item.Title;
            Description = item.Subtitle;

            if (!string.IsNullOrEmpty(item.LocalImagePath) && File.Exists(item.LocalImagePath))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(item.LocalImagePath);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 520;
                bmp.EndInit();
                bmp.Freeze();
                ImageSour = bmp;
            }

            if (!string.IsNullOrEmpty(item.Link))
            {
                Cursor = System.Windows.Input.Cursors.Hand;
                ToolTip = item.Link;
                MouseLeftButtonUp += (_, _) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Link)
                        {
                            UseShellExecute = true
                        });
                    }
                    catch { /* no browser handler — ignore */ }
                };
            }
        }

        public static ImageSource ConvertToImageSource(System.Drawing.Image image)
        {
            if (image == null) return null;

            using (MemoryStream memoryStream = new MemoryStream())
            {
                image.Save(memoryStream, image.RawFormat);
                memoryStream.Seek(0, SeekOrigin.Begin);

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        
    }
}
