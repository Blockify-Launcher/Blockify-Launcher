using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlockifyLauncher.Core.Vibe
{
    public enum VibeKind { Ocean, Sunset, Nether, Cherry, End }

    /// <summary>
    /// Generates pixel-art "vibe" background scenes (ocean, sunset, nether,
    /// cherry grove, the End) into a WriteableBitmap. Rendered once at startup —
    /// zero runtime cost afterwards.
    /// </summary>
    public static class VibeScene
    {
        /// <summary>Auto-vibe by time of day: morning cherry, day ocean, evening sunset, night End.</summary>
        public static VibeKind ForNow()
        {
            int h = DateTime.Now.Hour;
            if (h >= 6 && h < 11) return VibeKind.Cherry;
            if (h >= 11 && h < 17) return VibeKind.Ocean;
            if (h >= 17 && h < 22) return VibeKind.Sunset;
            return VibeKind.End;
        }

        private sealed class Scene
        {
            public uint Seed;
            public uint[] Sky = Array.Empty<uint>();
            public uint Far, Top;
            public uint[] Ground = Array.Empty<uint>();
            public uint? Celestial;
            public uint? Cloud;
            public bool Stars, Monument, Ceiling, Islands;
            public string Feature = "";
            public uint Spark;
        }

        private static Scene GetScene(VibeKind kind) => kind switch
        {
            VibeKind.Ocean => new Scene
            {
                Seed = 11,
                Sky = new uint[] { 0xFF083048, 0xFF0D3F61, 0xFF123A5C, 0xFF2A1A4E },
                Far = 0xFF0A2C44, Top = 0xFF0E6B4E,
                Ground = new uint[] { 0xFF0B4536, 0xFF0E5B46, 0xFF123A5C, 0xFF0F2F4A },
                Monument = true, Feature = "kelp", Spark = 0xFF9FE8FF
            },
            VibeKind.Sunset => new Scene
            {
                Seed = 22,
                Sky = new uint[] { 0xFFFFD9A0, 0xFFFFAB5E, 0xFFE8734A, 0xFF8E4A6B },
                Far = 0xFF7A4A63, Top = 0xFF5CBF4E,
                Ground = new uint[] { 0xFF3F9E3A, 0xFF2E7D32, 0xFF6B4A2B },
                Celestial = 0xD9FFECB4, Cloud = 0x8CFFF0DC,
                Feature = "tree", Spark = 0xFFFFF3C9
            },
            VibeKind.Nether => new Scene
            {
                Seed = 33,
                Sky = new uint[] { 0xFF1C0808, 0xFF3D0F0C, 0xFF571712, 0xFF31100D },
                Far = 0xFF2A0D0A, Top = 0xFF7A3327,
                Ground = new uint[] { 0xFF5E241B, 0xFF4A1C15, 0xFF38130E },
                Cloud = 0x14FF783C, Ceiling = true,
                Feature = "lava", Spark = 0xFFFF7A3C
            },
            VibeKind.Cherry => new Scene
            {
                Seed = 44,
                Sky = new uint[] { 0xFFFFD7E8, 0xFFF7B8D4, 0xFFC98BC9, 0xFF8A6BB8 },
                Far = 0xFFA06A9E, Top = 0xFF79C05A,
                Ground = new uint[] { 0xFF5DA242, 0xFF4C8A38, 0xFF6B4A2B },
                Celestial = 0xE6FFFFFF, Cloud = 0x99FFFFFF,
                Feature = "cherry", Spark = 0xFFFFE3F0
            },
            _ => new Scene
            {
                Seed = 55,
                Sky = new uint[] { 0xFF0B0714, 0xFF171030, 0xFF241A3E, 0xFF171030 },
                Far = 0xFF1D1535, Top = 0xFFD8DBB4,
                Ground = new uint[] { 0xFFC2C68E, 0xFFA8AB77, 0xFF8A8D60 },
                Stars = true, Islands = true,
                Feature = "pillar", Spark = 0xFFC9A6FF
            }
        };

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

        public static WriteableBitmap Render(VibeKind kind, int w = 880, int h = 495)
        {
            var sc = GetScene(kind);
            var r = new Rng(sc.Seed);
            var px = new uint[w * h];
            const int s = 14, sf = 10;

            void Fill(int x, int y, int fw, int fh, uint color)
            {
                int x2 = Math.Min(w, x + fw), y2 = Math.Min(h, y + fh);
                for (int yy = Math.Max(0, y); yy < y2; yy++)
                    for (int xx = Math.Max(0, x); xx < x2; xx++)
                        px[yy * w + xx] = 0xFF000000 | color;
            }

            void Blend(int x, int y, int fw, int fh, uint color)
            {
                double a = ((color >> 24) & 0xFF) / 255.0;
                int x2 = Math.Min(w, x + fw), y2 = Math.Min(h, y + fh);
                for (int yy = Math.Max(0, y); yy < y2; yy++)
                    for (int xx = Math.Max(0, x); xx < x2; xx++)
                    {
                        uint b = px[yy * w + xx];
                        byte br = (byte)((b >> 16) & 0xFF), bg = (byte)((b >> 8) & 0xFF), bb = (byte)(b & 0xFF);
                        byte cr = (byte)((color >> 16) & 0xFF), cg = (byte)((color >> 8) & 0xFF), cb = (byte)(color & 0xFF);
                        px[yy * w + xx] = 0xFF000000
                            | ((uint)(br + (cr - br) * a) << 16)
                            | ((uint)(bg + (cg - bg) * a) << 8)
                            | (uint)(bb + (cb - bb) * a);
                    }
            }

            // sky gradient (multi-stop, per row)
            for (int y = 0; y < h; y++)
            {
                double t = (double)y / h * (sc.Sky.Length - 1);
                int i = Math.Min(sc.Sky.Length - 2, (int)t);
                double f = t - i;
                uint c1 = sc.Sky[i], c2 = sc.Sky[i + 1];
                uint cr = (uint)(((c1 >> 16) & 0xFF) + (((c2 >> 16) & 0xFF) - ((c1 >> 16) & 0xFF)) * f);
                uint cg = (uint)(((c1 >> 8) & 0xFF) + (((c2 >> 8) & 0xFF) - ((c1 >> 8) & 0xFF)) * f);
                uint cb = (uint)((c1 & 0xFF) + ((c2 & 0xFF) - (c1 & 0xFF)) * f);
                uint row = 0xFF000000 | (cr << 16) | (cg << 8) | cb;
                for (int x = 0; x < w; x++) px[y * w + x] = row;
            }

            if (sc.Stars)
                for (int i = 0; i < 90; i++)
                    Blend((int)(r.Next() * w), (int)(r.Next() * h * .75), 2, 2,
                        ((uint)(64 + r.Next() * 191) << 24) | 0x00FFFFFF);

            if (sc.Celestial is uint cel)
            {
                int cx = (int)(w * .78), cy = (int)(h * .2), R = 26;
                Blend(cx - R, cy - R, R * 2, R * 2, cel);
                Blend(cx - R - 8, cy - R + 8, R * 2 + 16, R * 2 - 16, (cel & 0x00FFFFFF) | 0x59000000);
                Blend(cx - R + 8, cy - R - 8, R * 2 - 16, R * 2 + 16, (cel & 0x00FFFFFF) | 0x59000000);
            }

            if (sc.Cloud is uint cloud)
                for (int i = 0; i < 5; i++)
                {
                    int cx = (int)(r.Next() * w), cy = (int)(r.Next() * h * .28 + 6);
                    int cw = (int)((3 + r.Next() * 5) * s);
                    Blend(cx, cy, cw, (int)(s * .7), cloud);
                    Blend(cx + s, cy - s / 2, (int)(cw * .6), (int)(s * .6), cloud);
                }

            // far silhouette / floating islands
            if (sc.Islands)
            {
                for (int i = 0; i < 3; i++)
                {
                    int ix = (int)(w * (.15 + i * .3) + r.Next() * 40);
                    int iy = (int)(h * (.3 + r.Next() * .2));
                    int iw = (int)(6 + r.Next() * 6);
                    for (int cx = 0; cx < iw; cx++)
                    {
                        int depth = 1 + (int)(iw / 2.0 - Math.Abs(cx - iw / 2.0));
                        for (int cy = 0; cy < depth; cy++)
                            Fill(ix + cx * sf, iy + cy * sf, sf, sf, cy == 0 ? 0xFF2A2447 : sc.Far);
                    }
                }
            }
            else
            {
                double fh = h * (.5 + r.Next() * .1);
                for (int cx = 0; cx < w; cx += sf)
                {
                    fh = Math.Min(h * .75, Math.Max(h * .38, fh + (r.Next() - .5) * sf * 2));
                    int top = (int)(fh / sf) * sf;
                    Fill(cx, top, sf, h - top, sc.Far);
                }
            }

            // ocean monument
            if (sc.Monument)
            {
                int mx = (int)(w * .62), mh = 7, mw = 13;
                for (int lvl = 0; lvl < mh; lvl++)
                {
                    int lw = (mw - lvl * 2) * s;
                    Fill(mx - lw / 2, h - (lvl + 3) * s, lw, s, 0xFF0C3A55);
                }
                Blend(mx - s / 2, h - (mh + 3) * s, s, s, 0xB37CE7DD);
            }

            // nether ceiling + stalactites
            if (sc.Ceiling)
            {
                double ch = h * .12;
                for (int cx = 0; cx < w; cx += s)
                {
                    ch = Math.Min(h * .24, Math.Max(h * .05, ch + (r.Next() - .5) * s * 1.4));
                    Fill(cx, 0, s, (int)(ch / s) * s, sc.Ground[2]);
                }
                for (int cx = 0; cx < w; cx += s)
                    if (r.Next() < .12)
                        Fill(cx, (int)(h * .1), s, (int)(2 + r.Next() * 3) * s, sc.Ground[0]);
            }

            // near terrain
            double hgt = h * (.72 + r.Next() * .08);
            var tops = new List<(int x, int top)>();
            for (int cx = 0; cx < w; cx += s)
            {
                hgt = Math.Min(h * .92, Math.Max(h * .6, hgt + (r.Next() - .5) * s * 1.5));
                int top = (int)(hgt / s) * s;
                tops.Add((cx, top));
                Fill(cx, top, s, s, sc.Top);
                for (int cy = top + s; cy < h; cy += s)
                    Fill(cx, cy, s, s, sc.Ground[(int)(r.Next() * sc.Ground.Length)]);
            }

            // features on top of terrain
            foreach (var (cx, top) in tops)
            {
                double f = r.Next();
                switch (sc.Feature)
                {
                    case "kelp" when f < .14:
                        int kh = (int)(2 + r.Next() * 4);
                        for (int i = 1; i <= kh; i++)
                            Fill(cx + (int)(s * .3), top - (int)(i * s * .7), (int)(s * .4), (int)(s * .7), 0xFF1E7D46);
                        Fill(cx + (int)(s * .3), top - (int)((kh + 1) * s * .7), (int)(s * .4), (int)(s * .7), 0xFF37B56B);
                        break;
                    case "tree" or "cherry" when f < .08:
                        int th = 2 + (int)(r.Next() * 2);
                        uint leaf = sc.Feature == "cherry" ? 0xFFFFB7D5u : 0xFF2E8B3Au;
                        uint leaf2 = sc.Feature == "cherry" ? 0xFFF492BDu : 0xFF237030u;
                        for (int i = 1; i <= th; i++) Fill(cx, top - i * s, s, s, 0xFF5D4037);
                        Fill(cx - s, top - (th + 2) * s, s * 3, s * 2, leaf);
                        Fill(cx - s / 2, top - (th + 3) * s, s * 2, s, leaf2);
                        break;
                    case "lava" when f < .12:
                        Fill(cx, top, s, s, 0xFFFF7A26);
                        Fill(cx + s / 4, top + s / 4, s / 2, s / 2, 0xFFFFC03A);
                        break;
                    case "pillar" when f < .05:
                        int ph = (int)(3 + r.Next() * 4);
                        Fill(cx - s / 2, top - ph * s, s * 2, ph * s, 0xFF14101F);
                        Fill(cx, top - ph * s - (int)(s * .6), s, (int)(s * .6), 0xFFE39BFF);
                        break;
                }
            }

            // sparkle particles
            for (int i = 0; i < 20; i++)
                Blend((int)(r.Next() * w), (int)(r.Next() * h * .6), 3, 3,
                    ((uint)(64 + r.Next() * 128) << 24) | (sc.Spark & 0x00FFFFFF));

            var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new Int32Rect(0, 0, w, h), px, w * 4, 0);
            bmp.Freeze();
            return bmp;
        }
    }
}
