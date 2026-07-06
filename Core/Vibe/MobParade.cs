using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BlockifyLauncher.Core.Vibe
{
    /// <summary>
    /// The mob parade: pixel creeper, pig and bee occasionally cross the screen
    /// above the bottom dock. One translate animation + a two-frame hop each —
    /// effectively free for the GPU.
    /// </summary>
    public static class MobParade
    {
        private static readonly string[] Creeper =
        {
            "GGGGGGGG",
            "GGGGGGGG",
            "GKKGGKKG",
            "GKKGGKKG",
            "GGGKKGGG",
            "GGKKKKGG",
            "GGKKKKGG",
            "GGKGGKGG",
            "GGGGGGGG",
            "GGGGGGGG",
            "GGGGGGGG",
            "GGG..GGG",
            "GGG..GGG"
        };

        private static readonly string[] Pig =
        {
            "PPPPPPPP",
            "PPPPPPPP",
            "PKPPPPKP",
            "PPPPPPPP",
            "PPSSSSPP",
            "PPSNNSPP",
            "PPPPPPPP",
            "PPPPPPPP",
            "PPP..PPP",
            "PPP..PPP"
        };

        private static readonly string[] Bee =
        {
            "...WW.....",
            "..WWWW....",
            ".YKYKYYK..",
            "YYKYKYYYK.",
            "YYKYKYYYK.",
            ".YYYYYYY..",
            "..Y..Y...."
        };

        private static readonly Dictionary<char, uint> Palette = new()
        {
            ['G'] = 0xFF4FAE4F, ['K'] = 0xFF17301A,
            ['P'] = 0xFFEDA3A3, ['S'] = 0xFFD98484, ['N'] = 0xFF7A3A3A,
            ['Y'] = 0xFFF4C542, ['W'] = 0xCCDCEBFF
        };

        public static void Start(Canvas canvas)
        {
            AddMob(canvas, Creeper, scale: 3, durationSec: 46, delaySec: 0, bottom: 0, hop: true);
            AddMob(canvas, Pig, scale: 3, durationSec: 70, delaySec: -32, bottom: 0, hop: true);
            AddMob(canvas, Bee, scale: 3, durationSec: 34, delaySec: -12, bottom: 18, hop: false, flap: true);
        }

        private static void AddMob(Canvas canvas, string[] map, int scale,
            double durationSec, double delaySec, double bottom, bool hop, bool flap = false)
        {
            var image = new Image
            {
                Source = BuildSprite(map),
                Width = map[0].Length * scale,
                Height = map.Length * scale,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

            var bob = new TranslateTransform();
            image.RenderTransform = bob;

            Canvas.SetBottom(image, bottom);
            Canvas.SetLeft(image, -80);
            canvas.Children.Add(image);

            var walk = new DoubleAnimation(-80, 2400, TimeSpan.FromSeconds(durationSec))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(delaySec)
            };
            image.BeginAnimation(Canvas.LeftProperty, walk);

            if (hop)
            {
                var hopAnim = new DoubleAnimation(0, -3, TimeSpan.FromSeconds(0.28))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true
                };
                bob.BeginAnimation(TranslateTransform.YProperty, hopAnim);
            }
            else if (flap)
            {
                var flapAnim = new DoubleAnimation(0, -8, TimeSpan.FromSeconds(0.8))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true,
                    EasingFunction = new System.Windows.Media.Animation.SineEase()
                };
                bob.BeginAnimation(TranslateTransform.YProperty, flapAnim);
            }
        }

        private static WriteableBitmap BuildSprite(string[] map)
        {
            int cols = map[0].Length, rows = map.Length;
            var px = new uint[cols * rows];
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    px[y * cols + x] = Palette.TryGetValue(map[y][x], out uint c) ? c : 0x00000000;

            var bmp = new WriteableBitmap(cols, rows, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, cols, rows), px, cols * 4, 0);
            bmp.Freeze();
            return bmp;
        }
    }
}
