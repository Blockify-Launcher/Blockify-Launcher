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

        // one path segment that can't climb out of its folder: no separators, no "." / "..",
        // no trailing dots/spaces (Windows aliases them) and no ":" (alternate data streams)
        private static string SafeName(string name)
        {
            string n = Path.GetFileName(name ?? "").TrimEnd('.', ' ');
            if (n.Length == 0 || n.Contains(':') || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "_invalid_";
            return n;
        }

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

        // HandleException touches the WebView (or opens a WPF dialog) — from background work, hop to the UI thread first
        private void ReportErrorOnUi(Exception ex)
        {
            try { Dispatcher.BeginInvoke(() => HandleException(ex)); }
            catch { LogDiag("EXCEPTION " + ex); }
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
        // Albums: the main game plus every installed pack instance (MainWindow.Shots.cs).
        // `file` is always a path relative to McBase — see TryResolveShot for the accepted shapes.
        // All file work runs off the UI thread; Post always happens back on it.
        private int _shotsScanSeq;   // UI thread only: drops a slower, older scan that finishes last

        private void PushScreenshots()
        {
            var albumList = ShotAlbums();   // settings + packs.json: resolve on the UI thread
            int seq = ++_shotsScanSeq;
            Task.Run(() =>
            {
                var shots = new List<(long ticks, object item)>();
                var albums = new List<object>();
                foreach (var a in albumList)
                {
                    int total = 0;
                    try
                    {
                        if (Directory.Exists(a.Dir))
                        {
                            var files = new DirectoryInfo(a.Dir).EnumerateFiles().Where(f => IsShotExt(f.Name)).ToList();
                            total = files.Count;
                            foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(ShotsPerAlbum))
                            {
                                shots.Add((f.LastWriteTimeUtc.Ticks, new
                                {
                                    file = a.Rel + "/" + f.Name,
                                    name = f.Name,
                                    album = a.Id,
                                    albumTitle = a.Title,
                                    // ?t= busts the WebView cache when a screenshot is edited in place
                                    url = ShotUrl(a, f),
                                    date = f.LastWriteTime.ToString("dd.MM.yyyy HH:mm"),
                                    size = HumanSize(f.Length)
                                }));
                            }
                        }
                    }
                    catch { }
                    albums.Add(new { id = a.Id, title = a.Title, total });
                }
                var items = shots.OrderByDescending(s => s.ticks).Select(s => s.item).ToList();
                var payload = new { type = "screens", items, albums, perAlbum = ShotsPerAlbum };
                try { Dispatcher.BeginInvoke(() => { if (seq == _shotsScanSeq) Post(payload); }); } catch { }
            });
        }

        private void OpenScreenshot(string file)
        {
            if (TryResolveShot(file, out _, out var path) && File.Exists(path)) OpenPath(path);
        }

        // full-resolution bytes for the in-app viewer/editor, as a data URL
        // (same-origin so the canvas stays exportable — no cross-origin taint).
        private void OpenShotData(string file)
        {
            if (!TryResolveShot(file, out _, out var path))
            {
                Post(new { type = "shotData", file, dataUrl = (string?)null });
                return;
            }
            Task.Run(() =>
            {
                string? dataUrl = null;
                try
                {
                    if (File.Exists(path))
                    {
                        var bytes = File.ReadAllBytes(path);
                        string ext = Path.GetExtension(path).ToLowerInvariant();
                        string mime = ext is ".jpg" or ".jpeg" ? "image/jpeg" : ext == ".webp" ? "image/webp" : "image/png";
                        dataUrl = "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
                    }
                }
                catch (Exception ex) { ReportErrorOnUi(ex); }
                // always answer, so the viewer never waits forever
                try { Dispatcher.BeginInvoke(() => Post(new { type = "shotData", file, dataUrl })); } catch { }
            });
        }

        // write an edited screenshot back next to the original: overwrite it,
        // or save a copy ("copy" → -edit, "signed" → -blockify for the captioned version).
        private void SaveShot(string file, string dataUrl, string mode)
        {
            if (!TryResolveShot(file, out var album, out var path))
            {
                Post(new { type = "shotSaved", ok = false, file, mode });
                PushScreenshots();
                return;
            }
            string rel = album.Rel;
            Task.Run(() =>
            {
                object? result = null;
                try
                {
                    var bytes = File.Exists(path) ? DecodeShotDataUrl(dataUrl, path) : null;
                    if (bytes == null) result = new { type = "shotSaved", ok = false, file, mode };
                    else
                    {
                        string target = path;
                        if (mode is "copy" or "signed")
                        {
                            string dir = Path.GetDirectoryName(path)!;
                            string bn = Path.GetFileNameWithoutExtension(path);
                            string ext = Path.GetExtension(path);
                            string suffix = mode == "signed" ? "-blockify" : "-edit";
                            target = Path.Combine(dir, bn + suffix + ext);
                            int n = 2;
                            while (File.Exists(target)) target = Path.Combine(dir, bn + suffix + n++ + ext);
                        }

                        // write next to the target and swap in: a failed write never truncates the original
                        string tmp = target + ".tmp";
                        try
                        {
                            File.WriteAllBytes(tmp, bytes);
                            File.Move(tmp, target, overwrite: true);
                        }
                        catch
                        {
                            try { File.Delete(tmp); } catch { }
                            throw;
                        }
                        result = new { type = "shotSaved", ok = true, file, saved = rel + "/" + Path.GetFileName(target), mode };
                    }
                }
                catch (Exception ex) { ReportErrorOnUi(ex); }
                try
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (result != null) Post(result);
                        PushScreenshots();
                    });
                }
                catch { }
            });
        }

        // delete a screenshot to the Recycle Bin (reversible), falling back to a hard delete.
        private void DeleteShot(string file)
        {
            try
            {
                if (TryResolveShot(file, out _, out var path) && File.Exists(path))
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
        // A world key ("name" in messages) is "<world>" for the main game's saves and
        // "<pack-slug>/<world>" for a pack instance's saves — folder names can't contain '/',
        // so the two never collide and old UI code that just echoes `name` back keeps working.
        // Pack backups live in backups/blockify-packs/<slug>/<world>, outside the instance,
        // so they survive removing the pack.
        private sealed record WorldSource(string Slug, string Title, string Saves, string Backups);

        private const string GameRunningMsg = "Сборка сейчас запущена. Закрой игру и попробуй снова.";
        private const string WorldOpenMsg = "Мир сейчас открыт в игре. Выйди из мира и попробуй снова.";

        private int _worldsScanSeq;                                                              // UI thread only
        private readonly HashSet<string> _worldOps = new(StringComparer.OrdinalIgnoreCase);     // UI thread only: worlds being zipped/restored
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long ticks, long size)> WorldSizeCache
            = new(StringComparer.OrdinalIgnoreCase);

        private string PackBackupRoot(string slug) => Path.Combine(BackupRoot(), "blockify-packs", SafeName(slug));

        // where worlds live: the main game first, then every installed pack (resolved on the UI thread)
        private List<WorldSource> WorldSources()
        {
            var list = new List<WorldSource> { new("", MainShotsTitle, Path.Combine(McBase(), "saves"), BackupRoot()) };
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in LoadPacks())
                    if (IsSafeShotSegment(p.Slug) && seen.Add(p.Slug))
                        list.Add(new(p.Slug, string.IsNullOrWhiteSpace(p.Title) ? p.Slug : p.Title,
                                     Path.Combine(PackHome(p), "saves"), PackBackupRoot(p.Slug)));
            }
            catch { }
            return list;
        }

        // world key → world folder + its backup folder; false for anything malformed / unknown pack
        private bool TryResolveWorld(string? key, out string worldDir, out string backupDir)
        {
            worldDir = backupDir = "";
            if (string.IsNullOrEmpty(key) || key.Length > 600) return false;
            int slash = key.IndexOf('/');
            if (slash < 0)
            {
                string w = SafeName(key);
                worldDir = Path.Combine(McBase(), "saves", w);
                backupDir = Path.Combine(BackupRoot(), w);
                return true;
            }
            string slug = key[..slash], world = key[(slash + 1)..];
            if (!IsSafeShotSegment(slug) || world.Length == 0 || world.Contains('/')) return false;
            var p = LoadPacks().FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.Ordinal));
            if (p == null) return false;
            string wn = SafeName(world);
            worldDir = Path.Combine(PackHome(p), "saves", wn);
            backupDir = Path.Combine(PackBackupRoot(slug), wn);
            return true;
        }

        private void PushWorlds()
        {
            var sources = WorldSources();
            int seq = ++_worldsScanSeq;
            Task.Run(() =>
            {
                // folder sizes walk thousands of region files — never on the UI thread
                var rows = new List<(DateTime at, object item)>();
                foreach (var s in sources)
                {
                    try
                    {
                        if (!Directory.Exists(s.Saves)) continue;
                        foreach (var d in new DirectoryInfo(s.Saves).GetDirectories())
                        {
                            rows.Add((d.LastWriteTime, new
                            {
                                name = s.Slug.Length == 0 ? d.Name : s.Slug + "/" + d.Name,   // key echoed back by the UI
                                world = d.Name,                                                // display name
                                pack = s.Slug,                                                 // "" = main game
                                packTitle = s.Title,
                                size = HumanSize(CachedDirSize(d)),
                                modified = d.LastWriteTime.ToString("dd.MM.yyyy HH:mm"),
                                backups = ListBackupsIn(Path.Combine(s.Backups, SafeName(d.Name)))
                            }));
                        }
                    }
                    catch { }
                }
                var items = rows.OrderByDescending(r => r.at).Select(r => r.item).ToList();
                try { Dispatcher.BeginInvoke(() => { if (seq == _worldsScanSeq) Post(new { type = "worlds", items }); }); } catch { }
            });
        }

        private static long DirSize(DirectoryInfo d)
        {
            try { return d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch { return 0; }
        }

        // the game rewrites level.dat (new file + rename) on every save, which bumps the folder's mtime
        private static long CachedDirSize(DirectoryInfo d)
        {
            long ticks = d.LastWriteTimeUtc.Ticks;
            if (WorldSizeCache.TryGetValue(d.FullName, out var c) && c.ticks == ticks) return c.size;
            long size = DirSize(d);
            if (size > 0) WorldSizeCache[d.FullName] = (ticks, size);
            return size;
        }

        private static List<object> ListBackupsIn(string wdir)
        {
            var list = new List<object>();
            try
            {
                if (Directory.Exists(wdir))
                    foreach (var f in new DirectoryInfo(wdir).GetFiles("*.zip").OrderByDescending(f => f.LastWriteTime))
                        list.Add(new { file = f.Name, date = f.LastWriteTime.ToString("dd.MM.yyyy HH:mm"), size = HumanSize(f.Length) });
            }
            catch { }
            return list;
        }

        // runs off the UI thread: only plain paths in, no settings access
        private static string ZipWorld(string src, string wdir, string suffix)
        {
            Directory.CreateDirectory(wdir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string zip = Path.Combine(wdir, stamp + suffix + ".zip");
            // write to .tmp and verify before it shows up as a backup — a half-written zip is worse than none
            string tmp = zip + ".tmp";
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                ZipFile.CreateFromDirectory(src, tmp, CompressionLevel.Fastest, includeBaseDirectory: false);
                using (var check = ZipFile.OpenRead(tmp)) _ = check.Entries.Count;
                File.Move(tmp, zip, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
            return zip;
        }

        // The game (1.16+) keeps session.lock open and byte-locked while the world is loaded.
        // Probe with a shared open + a 1-byte lock so the check itself never stops the game from opening the world.
        private static bool IsWorldInUse(string worldDir)
        {
            var lockFile = Path.Combine(worldDir, "session.lock");
            if (!File.Exists(lockFile)) return false;
            try
            {
                using var fs = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                fs.Lock(0, 1);
                fs.Unlock(0, 1);
                return false;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (IOException) { return true; }
            catch { return false; }
        }

        // true if some process has the file open (log4j keeps logs/latest.log open for the whole session)
        private static bool IsFileHeldOpen(string path)
        {
            if (!File.Exists(path)) return false;
            try { using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (IOException) { return true; }
            catch { return false; }   // read-only / access denied: can't tell, don't block
        }

        // Is the game running from this game directory (main .minecraft or a pack instance)?
        // Checks the open latest.log first, then a loaded world. Do not call on the UI thread.
        private static bool IsInstanceRunning(string gameDir)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return false;
            if (IsFileHeldOpen(Path.Combine(gameDir, "logs", "latest.log"))) return true;
            try
            {
                string saves = Path.Combine(gameDir, "saves");
                if (Directory.Exists(saves))
                    foreach (var w in Directory.EnumerateDirectories(saves))
                        if (IsWorldInUse(w)) return true;
            }
            catch { }
            return false;
        }

        private void BackupWorld(string world)
        {
            if (!TryResolveWorld(world, out var src, out var wdir) || !Directory.Exists(src)) { PushWorlds(); return; }
            // an open world can't be zipped consistently (and the game keeps its files open)
            if (IsWorldInUse(src)) { HandleException(new InvalidOperationException(WorldOpenMsg)); PushWorlds(); return; }
            if (!_worldOps.Add(src))
            {
                HandleException(new InvalidOperationException("С этим миром уже идёт операция — дождись её окончания."));
                return;
            }
            Task.Run(() =>
            {
                try { ZipWorld(src, wdir, ""); }
                catch (Exception ex) { ReportErrorOnUi(ex); }
                try { Dispatcher.BeginInvoke(() => { _worldOps.Remove(src); PushWorlds(); }); } catch { }
            });
        }

        private void RestoreWorld(string world, string backup)
        {
            if (!TryResolveWorld(world, out var dest, out var wdir)
                || !backup.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { PushWorlds(); return; }
            var zip = Path.Combine(wdir, SafeName(backup));
            if (!File.Exists(zip)) { PushWorlds(); return; }
            if (Directory.Exists(dest) && IsWorldInUse(dest))
            {
                HandleException(new InvalidOperationException(WorldOpenMsg));
                PushWorlds();
                return;
            }
            if (!_worldOps.Add(dest))
            {
                HandleException(new InvalidOperationException("С этим миром уже идёт операция — дождись её окончания."));
                return;
            }

            Task.Run(() =>
            {
                string staging = dest + ".restore-tmp", old = dest + ".restore-old";
                try
                {
                    // 1) unpack next to the world first — if the archive is broken, nothing is touched
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    ZipFile.ExtractToDirectory(zip, staging);

                    if (Directory.Exists(dest))
                    {
                        // 2) safety net is mandatory: no verified backup of the current world → abort
                        try { ZipWorld(dest, wdir, "_before-restore"); }
                        catch (Exception ex) { throw new IOException("Не удалось сохранить текущий мир перед откатом, откат отменён: " + ex.Message, ex); }

                        // the game may have opened the world while we were unpacking
                        if (IsWorldInUse(dest)) throw new InvalidOperationException(WorldOpenMsg);

                        // 3) swap folders, rolling back if the second move fails
                        if (Directory.Exists(old)) Directory.Delete(old, true);
                        Directory.Move(dest, old);
                        try { Directory.Move(staging, dest); }
                        catch { Directory.Move(old, dest); throw; }
                        try { Directory.Delete(old, true); } catch { }
                    }
                    else Directory.Move(staging, dest);
                }
                catch (Exception ex)
                {
                    try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
                    ReportErrorOnUi(ex);
                }
                try { Dispatcher.BeginInvoke(() => { _worldOps.Remove(dest); PushWorlds(); }); } catch { }
            });
        }

        private void OpenWorldFolder(string world)
        {
            if (TryResolveWorld(world, out var dir, out _)) OpenPath(dir);
        }

        // ═══════════════════════ PACK INSTANCE DIR ═══════════════════════
        // Stored InstanceDir is used while it exists (that's where the game runs); a relative value
        // is read against the packs root, and a moved/renamed .minecraft falls back to <packs root>/<slug>.
        private string PackHome(InstalledPack p)
        {
            string dir = p.InstanceDir ?? "";
            try
            {
                if (dir.Length > 0 && !Path.IsPathRooted(dir)) dir = Path.Combine(PacksRoot(), dir);
                if (dir.Length > 0 && Directory.Exists(dir)) return dir;
                string byRoot = Path.Combine(PacksRoot(), SafeName(p.Slug));
                if (dir.Length == 0 || Directory.Exists(byRoot)) return byRoot;
            }
            catch { }
            return dir;
        }

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
                var gvs = new List<string>();
                string sel = BaseMcVersion(MinecraftVerisonComboBox.SelectedItem?.ToString());
                if (sel.Length > 0) gvs.Add(sel);

                // hashing a few hundred MB of jars must not freeze the window
                var (hashes, byHash) = await Task.Run(() =>
                {
                    var hs = new List<string>();
                    var map = new Dictionary<string, FileInfo>();
                    if (Directory.Exists(dir))
                        foreach (var f in new DirectoryInfo(dir).GetFiles("*.jar"))
                        {
                            string h;
                            try { h = Sha1File(f.FullName); } catch { continue; }
                            if (!map.ContainsKey(h)) { map[h] = f; hs.Add(h); }
                        }
                    return (hs, map);
                });

                if (hashes.Count > 0)
                {
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
                string mcBase = McBase();
                // the running game keeps its jars open: replacing one now leaves a duplicate or a missing mod
                if (await Task.Run(() => IsInstanceRunning(mcBase)))
                    throw new InvalidOperationException("Игра сейчас запущена. Закрой её и обнови мод снова.");

                var dir = Path.Combine(mcBase, "mods");
                var gvs = new List<string>();
                string sel = BaseMcVersion(MinecraftVerisonComboBox.SelectedItem?.ToString());
                if (sel.Length > 0) gvs.Add(sel);

                var updates = await ModrinthService.CheckModUpdatesAsync(new List<string> { hash }, new List<string>(), gvs);
                if (updates.TryGetValue(hash, out var u) && u.HasUpdate && !string.IsNullOrEmpty(u.DownloadUrl))
                {
                    var bytes = await ModrinthService.DownloadAsync(u.DownloadUrl);
                    string newName = SafeName(u.DownloadFileName ?? file);
                    string oldName = SafeName(file);
                    await Task.Run(() => ReplaceModJar(dir, oldName, newName, bytes));
                }
            }
            catch (Exception ex) { HandleException(ex); }
            await PushModsAsync();
        }

        // New jar goes to .tmp first and the old one is parked as .bak until the new one is in place,
        // so any failure leaves exactly one working copy (no duplicate, no missing mod).
        // Loaders only pick up *.jar, so the .tmp/.bak files are never loaded.
        private static void ReplaceModJar(string dir, string oldName, string newName, byte[] bytes)
        {
            string oldPath = Path.Combine(dir, oldName), newPath = Path.Combine(dir, newName);
            string tmp = newPath + ".tmp", bak = oldPath + ".bak";
            File.WriteAllBytes(tmp, bytes);
            bool parked = false;
            try
            {
                if (File.Exists(oldPath)) { File.Move(oldPath, bak, overwrite: true); parked = true; }
                File.Move(tmp, newPath, overwrite: true);
            }
            catch
            {
                try { if (parked && !File.Exists(oldPath)) File.Move(bak, oldPath); } catch { }
                try { File.Delete(tmp); } catch { }
                throw;
            }
            try { if (parked) File.Delete(bak); } catch { }
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
