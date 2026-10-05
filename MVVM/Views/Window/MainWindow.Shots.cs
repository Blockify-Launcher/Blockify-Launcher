using BlockifyLauncher.Core.Modrinth;
using Newtonsoft.Json.Linq;
using System.IO;
using Imaging = System.Windows.Media.Imaging;

namespace BlockifyLauncher
{
    // Feature module: screenshot center (albums, watermark, pack cover). Owned by the shots feature.
    public partial class MainWindow
    {
        private const int ShotsPerAlbum = 150;
        private const int CoverMaxWidth = 1280;
        private const string MainShotsTitle = "Основная игра";

        // Id: "" for the main game, otherwise the pack slug.
        // Rel: folder relative to McBase with '/' separators (also the mc.assets URL path).
        private sealed record ShotAlbum(string Id, string Title, string Dir, string Rel);

        private void InitShotsFeature() { }

        private bool TryHandleShotsMessage(string type, JObject m)
        {
            switch (type)
            {
                case "shotsOpenFolder":
                    OpenShotAlbumFolder(m.Value<string>("album") ?? "");
                    return true;
                case "shotSetCover":
                    _ = SetShotCoverAsync(m.Value<string>("file") ?? "", m.Value<string>("dataUrl"));
                    return true;
                default:
                    return false;
            }
        }

        // ── albums ──
        private ShotAlbum MainShotAlbum()
            => new("", MainShotsTitle, Path.Combine(McBase(), "screenshots"), "screenshots");

        private ShotAlbum PackShotAlbumOf(InstalledPack p)
            => new(p.Slug, string.IsNullOrWhiteSpace(p.Title) ? p.Slug : p.Title,
                   Path.Combine(PacksRoot(), p.Slug, "screenshots"),
                   "blockify-packs/" + p.Slug + "/screenshots");

        private ShotAlbum? PackShotAlbum(string slug)
        {
            if (!IsSafeShotSegment(slug)) return null;
            var p = LoadPacks().FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.Ordinal));
            return p == null ? null : PackShotAlbumOf(p);
        }

        private List<ShotAlbum> ShotAlbums()
        {
            var list = new List<ShotAlbum> { MainShotAlbum() };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in LoadPacks()
                     .Where(x => IsSafeShotSegment(x.Slug))
                     .OrderBy(x => string.IsNullOrWhiteSpace(x.Title) ? x.Slug : x.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                if (seen.Add(p.Slug)) list.Add(PackShotAlbumOf(p));
            }
            return list;
        }

        private static string ShotUrl(ShotAlbum a, FileInfo f)
            => "https://mc.assets/" + string.Join("/", a.Rel.Split('/').Select(s => Uri.EscapeDataString(s)))
               + "/" + Uri.EscapeDataString(f.Name) + "?t=" + f.LastWriteTimeUtc.Ticks;

        // ── validation ──
        // a single path segment that cannot climb, alias (trailing dot/space) or hit an ADS
        private static bool IsSafeShotSegment(string? s)
            => !string.IsNullOrWhiteSpace(s)
               && s.Length <= 200
               && s == SafeName(s)
               && s != "." && s != ".."
               && s.Trim() == s
               && !s.EndsWith('.')
               && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        private static bool IsShotExt(string name)
            => Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";

        // Accepts only "screenshots/<name>" or "blockify-packs/<slug>/screenshots/<name>"
        // where <slug> is an installed pack; anything else is rejected.
        private bool TryResolveShot(string? rel, out ShotAlbum album, out string path)
        {
            album = null!;
            path = "";
            if (string.IsNullOrEmpty(rel) || rel.Length > 600) return false;

            var parts = rel.Split('/');
            string name;
            if (parts.Length == 2 && parts[0] == "screenshots")
            {
                album = MainShotAlbum();
                name = parts[1];
            }
            else if (parts.Length == 4 && parts[0] == "blockify-packs" && parts[2] == "screenshots")
            {
                var a = PackShotAlbum(parts[1]);
                if (a == null) return false;
                album = a;
                name = parts[3];
            }
            else return false;

            if (!IsSafeShotSegment(name) || !IsShotExt(name)) return false;

            try
            {
                string dir = Path.GetFullPath(album.Dir).TrimEnd(Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(dir, name));
                if (!string.Equals(Path.GetDirectoryName(full), dir, StringComparison.OrdinalIgnoreCase)) return false;
                path = full;
                return true;
            }
            catch { return false; }
        }

        // data URL → bytes, only if the payload really is a PNG/JPEG (and matches the
        // target extension when one is given, so a .png never ends up holding a JPEG).
        private static byte[]? DecodeShotDataUrl(string? dataUrl, string? targetPath)
        {
            int comma = dataUrl?.IndexOf(',') ?? -1;
            if (comma < 0) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(dataUrl!.Substring(comma + 1)); }
            catch { return null; }

            bool png = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            bool jpg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            if (!png && !jpg) return null;
            if (targetPath != null)
            {
                bool wantJpg = Path.GetExtension(targetPath).ToLowerInvariant() is ".jpg" or ".jpeg";
                if (wantJpg != jpg) return null;
            }
            return bytes;
        }

        // ── open album folder ──
        private void OpenShotAlbumFolder(string albumId)
        {
            try
            {
                var album = albumId.Length == 0 ? MainShotAlbum() : PackShotAlbum(albumId);
                if (album == null) return;
                Directory.CreateDirectory(album.Dir);
                OpenPath(album.Dir);
            }
            catch (Exception ex) { HandleException(ex); }
        }

        // ── pack cover ──
        // Writes <instance>/cover.png (downscaled PNG) from the screenshot — or from the
        // viewer's unsaved edit when dataUrl is given — and points the pack icon at it.
        private async Task SetShotCoverAsync(string file, string? dataUrl)
        {
            try
            {
                if (!TryResolveShot(file, out var album, out var path) || album.Id.Length == 0)
                {
                    Post(new { type = "shotCover", ok = false, error = "Скриншот не из сборки" });
                    return;
                }
                byte[]? edited = string.IsNullOrEmpty(dataUrl) ? null : DecodeShotDataUrl(dataUrl, null);
                if (edited == null && !File.Exists(path))
                {
                    Post(new { type = "shotCover", ok = false, error = "Файл не найден" });
                    return;
                }

                string slug = album.Id;
                string cover = Path.Combine(PacksRoot(), slug, "cover.png");
                await Task.Run(() => WriteShotCover(edited ?? File.ReadAllBytes(path), cover));

                // back on the UI thread: load → modify → save without interleaving
                var packs = LoadPacks();
                var p = packs.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.Ordinal));
                if (p == null)
                {
                    Post(new { type = "shotCover", ok = false, error = "Сборка не найдена" });
                    return;
                }
                p.Icon = "https://mc.assets/blockify-packs/" + Uri.EscapeDataString(slug)
                         + "/cover.png?t=" + File.GetLastWriteTimeUtc(cover).Ticks;
                SavePacks(packs);
                PushInstalledPacks();
                Post(new { type = "shotCover", ok = true, album = slug, title = album.Title });
            }
            catch (Exception ex)
            {
                LogDiag("shotCover " + ex);
                Post(new { type = "shotCover", ok = false, error = ex.Message });
            }
        }

        private static void WriteShotCover(byte[] src, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            string tmp = dest + ".tmp";
            try
            {
                using var ms = new MemoryStream(src);
                var frame = Imaging.BitmapDecoder.Create(ms, Imaging.BitmapCreateOptions.None,
                                                         Imaging.BitmapCacheOption.OnLoad).Frames[0];
                Imaging.BitmapSource img = frame;
                if (frame.PixelWidth > CoverMaxWidth)
                {
                    double s = (double)CoverMaxWidth / frame.PixelWidth;
                    img = new Imaging.TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(s, s));
                }
                var enc = new Imaging.PngBitmapEncoder();
                enc.Frames.Add(Imaging.BitmapFrame.Create(img));
                using (var fs = File.Create(tmp)) enc.Save(fs);
            }
            catch
            {
                // transcoding failed: the raw bytes still render (browsers sniff the format)
                File.WriteAllBytes(tmp, src);
            }
            File.Move(tmp, dest, overwrite: true);
        }
    }
}
