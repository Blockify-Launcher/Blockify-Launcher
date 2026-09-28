using BlockifyLauncher.Core.Modrinth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BlockifyLauncher
{
    /// <summary>
    /// Extra HTML screens wired to the real filesystem / Modrinth:
    /// screenshots, worlds+backups, mod updates, and offline-profile skins.
    /// Images reach the WebView through virtual-host maps set in InitWebAsync
    /// (mc.assets → the .minecraft folder, skins.assets → the local skin library).
    /// </summary>
    public partial class MainWindow
    {
        // ── shared paths ──
        private string McBase()
        {
            try
            {
                var d = new Properties.Settings().GetMinecraftDir();
                if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d)) return d;
            }
            catch { }
            try { return setting.launcher.MinecraftPath.BasePath; } catch { }
            return Path.Combine(Environment.GetEnvironmentVariable("appdata") ?? "", ".minecraft");
        }

        private static string SkinsDir()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "BlockifyLauncher", "skins");

        private string BackupRoot() => Path.Combine(McBase(), "backups");

        private static string SafeName(string name) => Path.GetFileName(name ?? "");

        private static string HumanSize(long b) =>
            b >= 1073741824 ? (b / 1073741824.0).ToString("0.#") + " ГБ" :
            b >= 1048576 ? (b / 1048576.0).ToString("0.#") + " МБ" :
            b >= 1024 ? (b / 1024.0).ToString("0.#") + " КБ" : b + " Б";

        private static void OpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (Directory.Exists(path) || File.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                if (File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                    OpenPath(path);
            }
            catch { }
        }

        // ═══════════════════════ SCREENSHOTS ═══════════════════════
        private void PushScreenshots()
        {
            var items = new List<object>();
            try
            {
                var dir = Path.Combine(McBase(), "screenshots");
                if (Directory.Exists(dir))
                {
                    foreach (var f in new DirectoryInfo(dir).GetFiles()
                             .Where(f => f.Extension.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                             .OrderByDescending(f => f.LastWriteTime).Take(80))
                    {
                        items.Add(new
                        {
                            file = f.Name,
                            // ?t= busts the WebView cache when a screenshot is edited in place
                            url = "https://mc.assets/screenshots/" + Uri.EscapeDataString(f.Name) + "?t=" + f.LastWriteTimeUtc.Ticks,
                            date = f.LastWriteTime.ToString("dd.MM.yyyy HH:mm"),
                            size = HumanSize(f.Length)
                        });
                    }
                }
            }
            catch { }
            Post(new { type = "screens", items });
        }

        private void OpenScreenshot(string file)
            => OpenPath(Path.Combine(McBase(), "screenshots", SafeName(file)));

        // full-resolution bytes for the in-app viewer/editor, as a data URL
        // (same-origin so the canvas stays exportable — no cross-origin taint).
        private void OpenShotData(string file)
        {
            try
            {
                var path = Path.Combine(McBase(), "screenshots", SafeName(file));
                if (!File.Exists(path)) { Post(new { type = "shotData", file, dataUrl = (string?)null }); return; }
                var bytes = File.ReadAllBytes(path);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string mime = ext is ".jpg" or ".jpeg" ? "image/jpeg" : ext == ".webp" ? "image/webp" : "image/png";
                Post(new { type = "shotData", file, dataUrl = "data:" + mime + ";base64," + Convert.ToBase64String(bytes) });
            }
            catch (Exception ex) { HandleException(ex); }
        }

        // write an edited screenshot back: overwrite the original, or save a copy.
        private void SaveShot(string file, string dataUrl, string mode)
        {
            try
            {
                int comma = dataUrl?.IndexOf(',') ?? -1;
                if (comma < 0) return;
                var bytes = Convert.FromBase64String(dataUrl!.Substring(comma + 1));
                var dir = Path.Combine(McBase(), "screenshots");
                Directory.CreateDirectory(dir);

                string target;
                if (mode == "copy")
                {
                    string bn = Path.GetFileNameWithoutExtension(SafeName(file));
                    string ext = Path.GetExtension(SafeName(file));
                    if (ext.Length == 0) ext = ".png";
                    target = Path.Combine(dir, bn + "-edit" + ext);
                    int n = 2;
                    while (File.Exists(target)) target = Path.Combine(dir, bn + "-edit" + n++ + ext);
                }
                else target = Path.Combine(dir, SafeName(file));

                File.WriteAllBytes(target, bytes);
            }
            catch (Exception ex) { HandleException(ex); }
            PushScreenshots();
        }

        // delete a screenshot to the Recycle Bin (reversible), falling back to a hard delete.
        private void DeleteShot(string file)
        {
            try
            {
                var path = Path.Combine(McBase(), "screenshots", SafeName(file));
                if (File.Exists(path))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            path,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch { File.Delete(path); }
                }
            }
            catch (Exception ex) { HandleException(ex); }
            PushScreenshots();
        }

        // ═══════════════════════ WORLDS & BACKUPS ═══════════════════════
        private void PushWorlds()
        {
            var items = new List<object>();
            try
            {
                var dir = Path.Combine(McBase(), "saves");
                if (Directory.Exists(dir))
                {
                    foreach (var d in new DirectoryInfo(dir).GetDirectories().OrderByDescending(d => d.LastWriteTime))
                    {
                        items.Add(new
                        {
                            name = d.Name,
                            size = HumanSize(DirSize(d)),
                            modified = d.LastWriteTime.ToString("dd.MM.yyyy HH:mm"),
                            backups = ListBackups(d.Name)
                        });
                    }
                }
            }
            catch { }
            Post(new { type = "worlds", items });
        }

        private static long DirSize(DirectoryInfo d)
        {
            try { return d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch { return 0; }
        }

        private List<object> ListBackups(string world)
        {
            var list = new List<object>();
            try
            {
                var wdir = Path.Combine(BackupRoot(), SafeName(world));
                if (Directory.Exists(wdir))
                    foreach (var f in new DirectoryInfo(wdir).GetFiles("*.zip").OrderByDescending(f => f.LastWriteTime))
                        list.Add(new { file = f.Name, date = f.LastWriteTime.ToString("dd.MM.yyyy HH:mm"), size = HumanSize(f.Length) });
            }
            catch { }
            return list;
        }

        private string ZipWorld(string world, string suffix)
        {
            var src = Path.Combine(McBase(), "saves", SafeName(world));
            var wdir = Path.Combine(BackupRoot(), SafeName(world));
            Directory.CreateDirectory(wdir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string zip = Path.Combine(wdir, stamp + suffix + ".zip");
            ZipFile.CreateFromDirectory(src, zip, CompressionLevel.Fastest, includeBaseDirectory: false);
            return zip;
        }

        private void BackupWorld(string world)
        {
            try
            {
                if (Directory.Exists(Path.Combine(McBase(), "saves", SafeName(world))))
                    ZipWorld(world, "");
            }
            catch (Exception ex) { HandleException(ex); }
            PushWorlds();
        }

        private void RestoreWorld(string world, string backup)
        {
            try
            {
                var zip = Path.Combine(BackupRoot(), SafeName(world), SafeName(backup));
                if (!File.Exists(zip)) return;
                var dest = Path.Combine(McBase(), "saves", SafeName(world));

                // safety net: snapshot the current world before overwriting it
                if (Directory.Exists(dest))
                {
                    try { ZipWorld(world, "_before-restore"); } catch { }
                    Directory.Delete(dest, true);
                }
                Directory.CreateDirectory(dest);
                ZipFile.ExtractToDirectory(zip, dest, overwriteFiles: true);
            }
            catch (Exception ex) { HandleException(ex); }
            PushWorlds();
        }

        private void OpenWorldFolder(string world)
            => OpenPath(Path.Combine(McBase(), "saves", SafeName(world)));

        // ═══════════════════════ MOD UPDATES ═══════════════════════
        private static string Sha1File(string path)
        {
            using var sha = SHA1.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }

        private static string BaseMcVersion(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var m = Regex.Match(raw, @"1\.\d+(\.\d+)?");
            return m.Success ? m.Value : "";
        }

        private async Task PushModsAsync()
        {
            var result = new List<object>();
            try
            {
                var dir = Path.Combine(McBase(), "mods");
                if (Directory.Exists(dir))
                {
                    var files = new DirectoryInfo(dir).GetFiles("*.jar");
                    var hashes = new List<string>();
                    var byHash = new Dictionary<string, FileInfo>();
                    foreach (var f in files)
                    {
                        string h;
                        try { h = Sha1File(f.FullName); } catch { continue; }
                        if (!byHash.ContainsKey(h)) { byHash[h] = f; hashes.Add(h); }
                    }

                    var gvs = new List<string>();
                    string sel = BaseMcVersion(MinecraftVerisonComboBox.SelectedItem?.ToString());
                    if (sel.Length > 0) gvs.Add(sel);

                    Dictionary<string, ModUpdateInfo> updates;
                    try { updates = await ModrinthService.CheckModUpdatesAsync(hashes, new List<string>(), gvs); }
                    catch { updates = new Dictionary<string, ModUpdateInfo>(); }

                    // updatable mods first, then the rest alphabetically
                    var ordered = hashes.Select(h => (h, f: byHash[h], u: updates.GetValueOrDefault(h)))
                        .OrderByDescending(x => x.u?.HasUpdate ?? false)
                        .ThenBy(x => x.u?.Title ?? x.f.Name, StringComparer.OrdinalIgnoreCase);

                    foreach (var (h, f, u) in ordered)
                    {
                        result.Add(new
                        {
                            file = f.Name,
                            hash = h,
                            name = u?.Title ?? Path.GetFileNameWithoutExtension(f.Name),
                            size = HumanSize(f.Length),
                            current = u?.CurrentVersion ?? "",
                            latest = u?.LatestVersion ?? "",
                            hasUpdate = u?.HasUpdate ?? false,
                            known = u?.CurrentVersion != null
                        });
                    }
                }
            }
            catch { }
            Post(new { type = "mods", items = result });
        }

        private async Task UpdateModAsync(string file, string hash)
        {
            try
            {
                var dir = Path.Combine(McBase(), "mods");
                var gvs = new List<string>();
                string sel = BaseMcVersion(MinecraftVerisonComboBox.SelectedItem?.ToString());
                if (sel.Length > 0) gvs.Add(sel);

                var updates = await ModrinthService.CheckModUpdatesAsync(new List<string> { hash }, new List<string>(), gvs);
                if (updates.TryGetValue(hash, out var u) && u.HasUpdate && !string.IsNullOrEmpty(u.DownloadUrl))
                {
                    var bytes = await ModrinthService.DownloadAsync(u.DownloadUrl);
                    string newName = SafeName(u.DownloadFileName ?? file);
                    await File.WriteAllBytesAsync(Path.Combine(dir, newName), bytes);

                    string oldPath = Path.Combine(dir, SafeName(file));
                    if (File.Exists(oldPath) && !string.Equals(newName, SafeName(file), StringComparison.OrdinalIgnoreCase))
                        File.Delete(oldPath);
                }
            }
            catch (Exception ex) { HandleException(ex); }
            await PushModsAsync();
        }

        // ═══════════════════════ SKINS (offline profiles) ═══════════════════════
        private string SkinMapPath() => Path.Combine(SkinsDir(), "skins.json");

        private Dictionary<string, string> LoadSkinMap()
        {
            try
            {
                if (File.Exists(SkinMapPath()))
                    return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(SkinMapPath()))
                           ?? new Dictionary<string, string>();
            }
            catch { }
            return new Dictionary<string, string>();
        }

        private void SaveSkinMap(Dictionary<string, string> map)
        {
            try
            {
                Directory.CreateDirectory(SkinsDir());
                File.WriteAllText(SkinMapPath(), JsonConvert.SerializeObject(map));
            }
            catch { }
        }

        private void PushSkins()
        {
            Directory.CreateDirectory(SkinsDir());
            var map = LoadSkinMap();

            var library = new List<object>();
            try
            {
                foreach (var f in new DirectoryInfo(SkinsDir()).GetFiles("*.png").OrderBy(f => f.Name))
                    library.Add(new { file = f.Name, url = "https://skins.assets/" + Uri.EscapeDataString(f.Name) });
            }
            catch { }

            var accounts = new List<object>();
            foreach (var a in AllAccounts())
            {
                if (a?.Username == null) continue;
                bool offline = a.UserType != "msa";
                map.TryGetValue(a.Id ?? "", out var skinFile);
                accounts.Add(new
                {
                    id = a.Id,
                    name = a.Username,
                    offline,
                    skin = !string.IsNullOrEmpty(skinFile) && File.Exists(Path.Combine(SkinsDir(), skinFile))
                        ? "https://skins.assets/" + Uri.EscapeDataString(skinFile!) : ""
                });
            }
            Post(new { type = "skins", accounts, library });
        }

        private void UploadSkin()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Выбери скин PNG (64×64)",
                    Filter = "PNG-скин|*.png",
                    CheckFileExists = true
                };
                if (dlg.ShowDialog(this) == true)
                {
                    Directory.CreateDirectory(SkinsDir());
                    string dest = Path.Combine(SkinsDir(), SafeName(dlg.FileName));
                    File.Copy(dlg.FileName, dest, overwrite: true);
                }
            }
            catch (Exception ex) { HandleException(ex); }
            PushSkins();
        }

        private void AssignSkin(string id, string file)
        {
            try
            {
                var map = LoadSkinMap();
                if (string.IsNullOrEmpty(file)) map.Remove(id);
                else map[id] = SafeName(file);
                SaveSkinMap(map);
            }
            catch (Exception ex) { HandleException(ex); }
            PushSkins();
        }
    }
}
