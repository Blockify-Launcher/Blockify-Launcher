using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlockifyLauncher.Core.Vibe
{
    /// <summary>
    /// Pixel-art diorama baked onto the dock panel: sculk corruption on the
    /// left (mound, catalyst, sensor, shrieker), an amethyst geode rift in
    /// the middle, lush caves on the right (moss, azaleas, big dripleaf,
    /// glow-berry vines, spore blossom). Rendered once per dock width;
    /// particle anchor points are returned for the animated layer.
    /// </summary>
    public class DockDioramaArt
    {
        public WriteableBitmap Bitmap = null!;
        public List<Point> BerryTips = new();
        public Point SporeOrigin;
        public int Width, Height;
    }

    public static class DockDiorama
    {
        public const int ArtHeight = 106;
        private const int TopY = 26;   // dock top edge inside the bitmap
        private const int BotY = 100;  // dock bottom edge inside the bitmap

        private sealed class Rng
        {
            private uint _s;
            public Rng(uint seed) => _s = seed;
            public double Next()
            {
                _s += 0x6D2B79F5;
                uint t = _s;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public static DockDioramaArt Render(int w)
        {
            const int h = ArtHeight;
            const double s = 6;
            var r = new Rng(77);
            var buf = new uint[w * h];
            var art = new DockDioramaArt { Width = w, Height = h };

            void Px(double cx, double cy, double pw, double ph, uint color)
            {
                int x2 = Math.Min(w, (int)(cx + pw)), y2 = Math.Min(h, (int)(cy + ph));
                for (int yy = Math.Max(0, (int)cy); yy < y2; yy++)
                    for (int xx = Math.Max(0, (int)cx); xx < x2; xx++)
                        buf[yy * w + xx] = color;
            }

            /* ============ SCULK (left ~28%) ============ */
            double sculkEnd = w * .28;

            // solid corner mound
            for (double cx = 0; cx < w * .09; cx += s)
            {
                double hgt = (w * .09 - cx) / (w * .09);
                for (double cy = BotY - s * (1 + hgt * 4); cy < BotY; cy += s * .8)
                    Px(cx, cy, s, s * .8, r.Next() < .45 ? 0xFF052A2Eu : 0xFF0A3A3Eu);
            }
            // patches with dark hearts
            for (int i = 0; i < 34; i++)
            {
                double cx = r.Next() * sculkEnd * .9, cy = BotY - s * (0.4 + r.Next() * 1.9);
                Px(cx, cy, s * (1 + (int)(r.Next() * 1.5)) + s, s, r.Next() < .5 ? 0xFF052A2Eu : 0xFF0A3A3Eu);
                if (r.Next() < .6) Px(cx + s * .4, cy + s * .25, s * .5, s * .5, 0xFF03181C);
            }
            // veins along the bottom edge
            for (double cx = 0; cx < sculkEnd + w * .08; cx += s)
            {
                double fade = 1 - cx / (sculkEnd + w * .08);
                if (r.Next() < fade * 1.2) Px(cx, BotY - s * .35, s, s * .35, r.Next() < .4 ? 0xFF0D5560u : 0xFF0A3A3Eu);
                if (r.Next() < fade * .7) Px(cx, BotY - s, s * .6, s * .4, 0xFF0A3A3E);
            }
            // climbing the left edge
            for (double cy = BotY; cy > TopY - s; cy -= s * .7)
            {
                double fade = (BotY - cy) / (BotY - TopY);
                Px(1, cy, s * (1.6 - fade) + 2, s * .7, r.Next() < .5 ? 0xFF0A3A3Eu : 0xFF052A2Eu);
                if (r.Next() < .7 - fade * .5) Px(s * 1.4, cy + s * .2, s * .7, s * .5, 0xFF052A2E);
            }
            // catalyst: bone-white crown on a dark block
            Px(s * 1.2, BotY - s * 2.4, s * 2.4, s * 1.2, 0xFF052A2E);
            Px(s * 1.4, BotY - s * 2.9, s * 2, s * .6, 0xFFDFE7EC);
            Px(s * 1.8, BotY - s * 3.3, s * .5, s * .5, 0xFFC9D6DD);
            Px(s * 2.7, BotY - s * 3.2, s * .4, s * .4, 0xFFC9D6DD);
            // tendrils over the top edge
            Px(s * 3.6, TopY - s * .5, s * .3, s * 1.4, 0xFF0D5560);
            Px(s * 9.5, TopY - s * .3, s * .25, s * 1.1, 0xFF0A3A3E);
            // sensor on the top edge
            double snx = s * 6;
            Px(snx, TopY - s, s * 2.4, s, 0xFF0D5560);
            Px(snx + s * .2, TopY - s * .35, s * 2, s * .35, 0xFF083237);
            foreach (var (dx, dy) in new[] { (.3, -2.1), (1.0, -2.6), (1.7, -2.2) })
            {
                Px(snx + s * dx, TopY + s * dy, s * .22, s * (Math.Abs(dy) - 1), 0xFF2AA8BD);
                Px(snx + s * dx - s * .12, TopY + s * dy - s * .3, s * .46, s * .32, 0xFF3DE8FF);
            }
            // shrieker: dark block with bone horns
            double shx = s * 16;
            Px(shx, BotY - s * 1.2, s * 2.6, s * 1.2, 0xFF052A2E);
            Px(shx + s * .3, BotY - s * .9, s * 2, s * .6, 0xFF0D5560);
            for (int i = 0; i < 4; i++)
                Px(shx + s * (.15 + i * .65), BotY - s * 1.7, s * .3, s * .5, 0xFFDFE7EC);

            /* ============ AMETHYST (31–42%) ============ */
            double amx = w * .31, amw = w * .11;
            Px(amx - s, BotY - s * .5, amw + 2 * s, s * .5, 0xFF3A3A44);
            Px(amx - s, TopY, s * .8, s * 1.2, 0xFF3A3A44);
            Px(amx + amw, TopY, s * .8, s * 1.5, 0xFF3A3A44);

            void Crystal(double cx, double baseY, bool up, int ch, bool light)
            {
                uint[] cols = light
                    ? new uint[] { 0xFF9A6EE0, 0xFF7B4FC0, 0xFFC9A6FF }
                    : new uint[] { 0xFF7B4FC0, 0xFF6A3AB8, 0xFFB58CF2 };
                for (int i = 0; i < ch; i++)
                {
                    double cw = s * (1 - (i / (double)ch) * 0.7);
                    Px(cx + (s - cw) / 2, up ? baseY - (i + 1) * s * .6 : baseY + i * s * .6, cw, s * .6, cols[i % 3]);
                }
                Px(cx + s * .3, up ? baseY - ch * s * .6 - s * .3 : baseY + ch * s * .6, s * .4, s * .3, 0xFFE3D0FF);
            }
            Crystal(amx + s * 1, BotY - s * .5, true, 3, false);
            Crystal(amx + s * 3.4, BotY - s * .5, true, 4, true);
            Crystal(amx + s * 6, BotY - s * .5, true, 2, false);
            Crystal(amx + s * 2.2, TopY, false, 2, true);
            Crystal(amx + s * 5, TopY, false, 3, false);

            /* ============ LUSH (45–100%) ============ */
            double lushStart = w * .45;
            for (double cx = lushStart; cx < w - 3; cx += s)
            {
                Px(cx, BotY - s * .45, s, s * .45, r.Next() < .75 ? 0xFF3F7A34u : 0xFF5A9A44u);
                if (r.Next() < .12) Px(cx, BotY - s * .45, s, s * .45, 0xFF8A9AA8);
                if (r.Next() < .3) Px(cx + s * .3, BotY - s * 1.05, s * .25, s * .6, 0xFF4A8A38);
            }
            // water pocket + lily pad
            double wpx = w * .62;
            Px(wpx, BotY - s * .4, s * 4, s * .4, 0xFF3A7EA8);
            Px(wpx + s * .5, BotY - s * .4, s * 3, s * .2, 0xFF5AA3CC);
            Px(wpx + s * 1.4, BotY - s * .55, s * .9, s * .25, 0xFF4A8A38);

            void Azalea(double ax)
            {
                Px(ax + s * .7, BotY - s * 1.2, s * .5, s * .8, 0xFF5D4037);
                Px(ax, BotY - s * 2.4, s * 2.2, s * 1.4, 0xFF2E6B2A);
                Px(ax + s * .3, BotY - s * 2.9, s * 1.6, s * .6, 0xFF3F8A34);
                foreach (var (dx, dy) in new[] { (.2, -2.5), (1.4, -2.7), (.8, -2.2) })
                    Px(ax + s * dx, BotY + s * dy, s * .5, s * .45, 0xFFCF6EE4);
                Px(ax + s * 1.7, BotY - s * 2.2, s * .4, s * .4, 0xFFE39BFF);
            }
            Azalea(w * .5); Azalea(w * .83); Azalea(w * .955);

            // big dripleaf
            double dlx = w * .905;
            Px(dlx, BotY - s * 3.4, s * .4, s * 3.4, 0xFF3F8A34);
            Px(dlx - s * 1.4, BotY - s * 3.8, s * 3.2, s * .55, 0xFF4FAF44);
            Px(dlx - s * 1.1, BotY - s * 3.5, s * 2.6, s * .3, 0xFF3F8A34);
            Px(dlx + s * .15, BotY - s * 2.2, s * .7, s * .3, 0xFF3F8A34);

            // moss mound in the right corner
            for (double cx = w - s * 6; cx < w - 2; cx += s)
            {
                double hgt = (cx - (w - s * 6)) / (s * 6);
                for (double cy = BotY - s * (1 + hgt * 2.6); cy < BotY; cy += s * .8)
                    Px(cx, cy, s, s * .8, r.Next() < .6 ? 0xFF3F7A34u : 0xFF5A9A44u);
            }
            Px(w - s * 3.4, BotY - s * 3.9, s * .5, s * .45, 0xFFCF6EE4);
            Px(w - s * 2.2, BotY - s * 3.4, s * .4, s * .4, 0xFFE39BFF);

            // glow berry vines from the top edge
            double[] vineFractions = { .48, .55, .6, .68, .74, .87, .94, .975 };
            foreach (var t in vineFractions)
            {
                double vx = w * t, vl = s * (1.6 + r.Next() * 2.8);
                Px(vx, TopY, s * .32, vl, 0xFF4A7A3A);
                if (r.Next() < .8) Px(vx - s * .25, TopY + vl * .4, s * .28, s * .5, 0xFF4A7A3A);
                Px(vx - s * .1, TopY + vl, s * .55, s * .55, 0xFFFFB02E);
                if (r.Next() < .5) Px(vx + s * .2, TopY + vl * .55, s * .4, s * .4, 0xFFFF9E2E);
                art.BerryTips.Add(new Point(vx, TopY + vl + s * .3));
            }
            // hanging moss at the far right
            Px(w - s * 1.2, TopY, s * .3, s * 2.2, 0xFF4A8A38);
            Px(w - s * 2.1, TopY, s * .28, s * 1.4, 0xFF3F7A34);

            // spore blossom under the top edge
            double spx = w * .795;
            Px(spx - s * 1.2, TopY, s * 2.8, s * .5, 0xFF3F6B2A);
            Px(spx - s * 1.5, TopY + s * .4, s * 1.2, s * .7, 0xFFF2A1C0);
            Px(spx + s * .6, TopY + s * .4, s * 1.2, s * .7, 0xFFF2A1C0);
            Px(spx - s * .6, TopY + s * .8, s * 1.5, s * .8, 0xFFF7B8D4);
            Px(spx - s * .15, TopY + s * .9, s * .6, s * .5, 0xFFE05A8A);
            art.SporeOrigin = new Point(spx, TopY + s * 1.6);

            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, w, h), buf, w * 4, 0);
            bmp.Freeze();
            art.Bitmap = bmp;
            return art;
        }
    }
}
