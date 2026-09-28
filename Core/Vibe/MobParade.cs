using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BlockifyLauncher.Core.Vibe
{
    /// <summary>
    /// The mob parade: pixel creeper, pig and bee occasionally cross the screen
    /// above the bottom dock. Each mob is a two-frame sprite animation
    /// (walk cycle / wing flap) plus one translate — effectively free for the GPU.
    /// </summary>
    public static class MobParade
    {
        // frame A: feet apart, frame B: feet shuffled — a simple walk cycle
        private static readonly string[] CreeperA =
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
        private static readonly string[] CreeperB =
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
            ".GG..GG.",
            ".GG..GG."
        };

        private static readonly string[] PigA =
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
        private static readonly string[] PigB =
        {
            "PPPPPPPP",
            "PPPPPPPP",
            "PKPPPPKP",
            "PPPPPPPP",
            "PPSSSSPP",
            "PPSNNSPP",
            "PPPPPPPP",
            "PPPPPPPP",
            ".PP..PP.",
            ".PP..PP."
        };

        // frame A: wings up, frame B: wings folded — flap
        private static readonly string[] BeeA =
        {
            "...WW.....",
            "..WWWW....",
            ".YKYKYYK..",
            "YYKYKYYYK.",
            "YYKYKYYYK.",
            ".YYYYYYY..",
            "..Y..Y...."
        };
        private static readonly string[] BeeB =
        {
            "..........",
            "...WW.....",
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
            AddMob(canvas, new[] { CreeperA, CreeperB }, scale: 3,
                durationSec: 46, delaySec: 0, bottom: 0, stepSec: 0.32, hop: true);
            AddMob(canvas, new[] { PigA, PigB }, scale: 3,
                durationSec: 70, delaySec: -32, bottom: 0, stepSec: 0.28, hop: true);
            AddMob(canvas, new[] { BeeA, BeeB }, scale: 3,
                durationSec: 34, delaySec: -12, bottom: 18, stepSec: 0.14, hop: false, bob: true);
        }

        private static void AddMob(Canvas canvas, string[][] frames, int scale,
            double durationSec, double delaySec, double bottom, double stepSec,
            bool hop, bool bob = false)
        {
            var bitmaps = frames.Select(BuildSprite).ToArray();

            var image = new Image
            {
                Source = bitmaps[0],
                Width = frames[0][0].Length * scale,
                Height = frames[0].Length * scale,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

            var move = new TranslateTransform();
            image.RenderTransform = move;

            Canvas.SetBottom(image, bottom);
            Canvas.SetLeft(image, -80);
            canvas.Children.Add(image);

            // stroll across the window
            var walk = new DoubleAnimation(-80, 2400, TimeSpan.FromSeconds(durationSec))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(delaySec)
            };
            image.BeginAnimation(Canvas.LeftProperty, walk);

            // two-frame sprite cycle (walk / flap)
            var sprite = new ObjectAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(stepSec * bitmaps.Length),
                RepeatBehavior = RepeatBehavior.Forever
            };
            for (int i = 0; i < bitmaps.Length; i++)
                sprite.KeyFrames.Add(new DiscreteObjectKeyFrame(bitmaps[i],
                    KeyTime.FromTimeSpan(TimeSpan.FromSeconds(stepSec * i))));
            image.BeginAnimation(Image.SourceProperty, sprite);

            if (hop)
            {
                var hopAnim = new DoubleAnimation(0, -3, TimeSpan.FromSeconds(0.28))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true
                };
                move.BeginAnimation(TranslateTransform.YProperty, hopAnim);
            }
            else if (bob)
            {
                var bobAnim = new DoubleAnimation(0, -8, TimeSpan.FromSeconds(0.8))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                    AutoReverse = true,
                    EasingFunction = new SineEase()
                };
                move.BeginAnimation(TranslateTransform.YProperty, bobAnim);
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
