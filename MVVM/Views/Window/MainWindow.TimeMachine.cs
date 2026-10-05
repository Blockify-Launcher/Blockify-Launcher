using BlockifyLauncher.Core.Modrinth;
using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BlockifyLauncher
{
    /// <summary>
    /// Time machine for pack instances: a snapshot = manifest of mods (name, sha1, enabled)
    /// + a zip of configs. Jars live in a content-addressed store (one copy per sha1), so
    /// snapshots are cheap even for 300 MB mod folders. Restore rebuilds mods/ + config/ exactly.
    /// The store is garbage-collected (mark-and-sweep over all manifests) whenever snapshots are removed.
    /// </summary>
    public partial class MainWindow
    {
        private sealed class SnapMod { public string file = ""; public string sha1 = ""; public bool enabled = true; }
        private sealed class SnapManifest
        {
            public string id = ""; public string label = ""; public string reason = ""; public string at = "";
            public bool auto = true; public bool hasConfig; public List<SnapMod> mods = new();
        }

        private static readonly string[] SnapConfigDirs = { "config", "kubejs", "defaultconfigs" };
        private static readonly string[] SnapConfigFiles = { "options.txt", "servers.dat" };
        private static readonly Regex Sha1Rx = new("^[0-9a-fA-F]{40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Store writes, restores and garbage collection never interleave: a GC between "jar copied
        // into the store" and "manifest written" would delete a jar that is about to be referenced.
        private static readonly object SnapStoreLock = new();

        private string SnapsDir() => Path.Combine(PacksRoot(), ".snapshots");
        private string SnapRoot(InstalledPack p) => Path.Combine(SnapsDir(), SafeName(p.Slug));
        private string SnapStore() => Path.Combine(SnapsDir(), "store");

        private static bool IsSha1(string? s) => s != null && Sha1Rx.IsMatch(s);

        private static List<SnapMod> ScanSnapMods(string instanceDir)
        {
            var list = new List<SnapMod>();
            string dir = Path.Combine(instanceDir, "mods");
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir))
            {
                bool enabled = f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                bool disabled = f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
                if (!enabled && !disabled) continue;
                string name = Path.GetFileName(f);
                if (disabled) name = name[..^".disabled".Length];
                try { list.Add(new SnapMod { file = name, sha1 = Sha1File(f), enabled = enabled }); } catch { }
            }
            return list;
        }

        // Copy a jar into the store via .tmp so a half-written file never gets the final name.
        // An existing entry is trusted only if its size matches (restore re-checks the sha1).
        private static void StoreJar(string src, string store, string sha1)
        {
            string stored = Path.Combine(store, sha1.ToLowerInvariant() + ".jar");
            var have = new FileInfo(stored);
            if (have.Exists && have.Length == new FileInfo(src).Length) return;
            string tmp = stored + ".tmp";
            try
            {
                File.Copy(src, tmp, overwrite: true);
                File.Move(tmp, stored, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
        }

        // ── create ──
        // keepId: a snapshot that auto-trim must not delete (the one being restored right now)
        private SnapManifest Snapshot(InstalledPack p, string label, string reason, bool auto, string? keepId = null)
        {
            string inst = PackHome(p), root = SnapRoot(p), store = SnapStore();
            lock (SnapStoreLock)
            {
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(store);
                var mods = ScanSnapMods(inst);
                string modsDir = Path.Combine(inst, "mods");
                foreach (var m in mods)
                {
                    string src = Path.Combine(modsDir, m.enabled ? m.file : m.file + ".disabled");
                    try { StoreJar(src, store, m.sha1); }
                    catch (Exception ex) { LogDiag($"snapshot: can't store {m.file}: {ex.Message}"); }
                }

                string id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                while (File.Exists(Path.Combine(root, id + ".json"))) id += "x";
                var man = new SnapManifest
                {
                    id = id, label = label, reason = reason, auto = auto,
                    at = DateTime.Now.ToString("dd.MM.yyyy HH:mm"), mods = mods
                };

                string zipPath = Path.Combine(root, id + ".zip");
                try
                {
                    using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                    {
                        foreach (var d in SnapConfigDirs)
                        {
                            string full = Path.Combine(inst, d);
                            if (!Directory.Exists(full)) continue;
                            foreach (var f in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
                            {
                                zip.CreateEntryFromFile(f, Path.GetRelativePath(inst, f).Replace('\\', '/'));
                                man.hasConfig = true;
                            }
                        }
                        foreach (var f in SnapConfigFiles)
                        {
                            string full = Path.Combine(inst, f);
                            if (File.Exists(full)) { zip.CreateEntryFromFile(full, f); man.hasConfig = true; }
                        }
                    }
                    // manifest last, via .tmp: a snapshot exists only once both files are complete
                    string json = Path.Combine(root, id + ".json");
                    File.WriteAllText(json + ".tmp", JsonConvert.SerializeObject(man));
                    File.Move(json + ".tmp", json, overwrite: true);
                }
                catch
                {
                    try { File.Delete(zipPath); } catch { }
                    try { File.Delete(Path.Combine(root, id + ".json.tmp")); } catch { }
                    throw;
                }
                if (TrimAutoSnapshots(p, keep: 25, keepId: keepId) > 0) GcSnapshotStore();
                return man;
            }
        }

        // returns how many snapshots were removed
        private int TrimAutoSnapshots(InstalledPack p, int keep, string? keepId = null)
        {
            try
            {
                var autos = ListManifests(p).Where(m => m.auto && m.id != keepId).Skip(keep).ToList();
                foreach (var m in autos) DeleteSnapshotFiles(p, m.id);
                return autos.Count;
            }
            catch { return 0; }
        }

        private List<SnapManifest> ListManifests(InstalledPack p)
        {
            var list = new List<SnapManifest>();
            if (!Directory.Exists(SnapRoot(p))) return list;
            foreach (var f in Directory.GetFiles(SnapRoot(p), "*.json"))
            {
                try
                {
                    var m = JsonConvert.DeserializeObject<SnapManifest>(File.ReadAllText(f));
                    if (m != null) { m.mods ??= new List<SnapMod>(); list.Add(m); }
                }
                catch { }
            }
            return list.OrderByDescending(m => m.id, StringComparer.Ordinal).ToList();
        }

        private void DeleteSnapshotFiles(InstalledPack p, string id)
        {
            id = SafeName(id);
            try { File.Delete(Path.Combine(SnapRoot(p), id + ".json")); } catch { }
            try { File.Delete(Path.Combine(SnapRoot(p), id + ".zip")); } catch { }
        }

        // Mark-and-sweep: drop store jars that no manifest of any pack references any more.
        // If any manifest can't be read the sweep is skipped — keeping garbage beats losing a jar.
        private void GcSnapshotStore()
        {
            string snaps = SnapsDir(), store = SnapStore();
            lock (SnapStoreLock)
            {
                try
                {
                    if (!Directory.Exists(store)) return;
                    var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var dir in Directory.GetDirectories(snaps))
                    {
                        if (string.Equals(Path.GetFileName(dir), "store", StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var f in Directory.GetFiles(dir, "*.json"))
                        {
                            var m = JsonConvert.DeserializeObject<SnapManifest>(File.ReadAllText(f))
                                    ?? throw new InvalidDataException("unreadable manifest " + f);
                            foreach (var mod in m.mods ?? new List<SnapMod>()) live.Add(mod.sha1);
                        }
                    }

                    int removed = 0; long freed = 0;
                    foreach (var f in new DirectoryInfo(store).GetFiles())
                    {
                        bool jar = f.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                        bool tmp = f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);   // leftovers of an interrupted copy
                        if (!jar && !tmp) continue;                                                 // not ours
                        if (jar && live.Contains(Path.GetFileNameWithoutExtension(f.Name))) continue;
                        try { long len = f.Length; f.Delete(); removed++; freed += len; } catch { }
                    }
                    if (removed > 0) LogDiag($"snapshot store gc: removed {removed} file(s), freed {HumanSize(freed)}");
                }
                catch (Exception ex) { LogDiag("snapshot store gc skipped: " + ex.Message); }
            }
        }

        // silent snapshot before a risky change; throttled per pack+reason so bursts don't spam the timeline
        private DateTime _lastAutoSnap = DateTime.MinValue;
        private string _lastAutoKey = "";
        private async Task AutoSnapshotAsync(InstalledPack p, string reason)
        {
            try
            {
                string key = p.Slug + "|" + reason;
                if (key == _lastAutoKey && (DateTime.Now - _lastAutoSnap).TotalSeconds < 60) return;
                await Task.Run(() => Snapshot(p, "", reason, auto: true));
                _lastAutoSnap = DateTime.Now; _lastAutoKey = key;
            }
            catch (Exception ex) { LogDiag("auto-snapshot failed: " + ex.Message); }
        }

        // ── list (with diff against the current state) ──
        private async Task PushSnapshotsAsync(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            string inst = PackHome(p);
            var payload = await Task.Run(() =>
            {
                var current = ScanSnapMods(inst);
                var curBySha = current.GroupBy(m => m.sha1).ToDictionary(g => g.Key, g => g.First());
                return ListManifests(p).Select(m =>
                {
                    var snapBySha = m.mods.GroupBy(x => x.sha1).ToDictionary(g => g.Key, g => g.First());
                    int added = current.Count(c => !snapBySha.ContainsKey(c.sha1));       // in current, not in snapshot
                    int removed = m.mods.Count(s => !curBySha.ContainsKey(s.sha1));       // in snapshot, gone now
                    int toggled = m.mods.Count(s => curBySha.TryGetValue(s.sha1, out var c) && c.enabled != s.enabled);
                    return new
                    {
                        m.id, m.label, m.reason, m.at, m.auto, m.hasConfig,
                        mods = m.mods.Count, enabled = m.mods.Count(x => x.enabled),
                        added, removed, toggled, same = added == 0 && removed == 0 && toggled == 0
                    };
                }).ToList();
            });
            Post(new { type = "snapshots", slug, items = payload });
        }

        // ── restore ──
        private static SnapManifest ReadSnapManifest(string root, string id)
        {
            id = SafeName(id);
            string manPath = Path.Combine(root, id + ".json");
            if (!File.Exists(manPath)) throw new FileNotFoundException("Снимок не найден — возможно, он уже удалён.");
            var man = JsonConvert.DeserializeObject<SnapManifest>(File.ReadAllText(manPath))
                      ?? throw new InvalidDataException("Снимок повреждён.");
            man.mods ??= new List<SnapMod>();
            // names and hashes become paths below: a tampered manifest must not reach outside mods/ or the store
            if (man.mods.Any(m => m == null || !IsSha1(m.sha1) || string.IsNullOrEmpty(m.file)
                                  || SafeName(m.file) != m.file || !m.file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Снимок повреждён.");
            man.id = id;   // paths use the requested file id, never the id stored inside the json
            return man;
        }

        // Phase 1: every jar the snapshot needs must be in mods/ already or intact in the store,
        // and the config zip must be readable. Returns what's missing; nothing is changed except
        // that store entries with a wrong sha1 are deleted (they would otherwise be trusted forever).
        private static List<string> CheckRestorable(SnapManifest man, string inst, string root, string store)
        {
            var problems = new List<string>();
            var have = new HashSet<string>(ScanSnapMods(inst).Select(c => c.sha1), StringComparer.OrdinalIgnoreCase);
            foreach (var w in man.mods.GroupBy(m => m.sha1, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                if (have.Contains(w.sha1)) continue;
                string stored = Path.Combine(store, w.sha1.ToLowerInvariant() + ".jar");
                if (!File.Exists(stored)) { problems.Add(w.file); continue; }
                string actual;
                try { actual = Sha1File(stored); }
                catch { problems.Add(w.file); continue; }
                if (!string.Equals(actual, w.sha1, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(w.file + " (файл повреждён)");
                    try { File.Delete(stored); } catch { }
                }
            }
            if (man.hasConfig)
            {
                string zipPath = Path.Combine(root, man.id + ".zip");
                try { using var z = ZipFile.OpenRead(zipPath); _ = z.Entries.Count; }
                catch { problems.Add("архив настроек"); }
            }
            return problems;
        }

        // Phase 2: rebuild mods/ + configs. Every failed step is collected instead of swallowed.
        private static List<string> ApplyRestore(SnapManifest man, string inst, string root, string store)
        {
            var errors = new List<string>();
            string modsDir = Path.Combine(inst, "mods");
            Directory.CreateDirectory(modsDir);

            // 1. mods: rebuild the exact set from current files + the store
            var current = ScanSnapMods(inst);
            var want = man.mods.GroupBy(m => m.sha1, StringComparer.OrdinalIgnoreCase)
                               .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // remove (after parking in the store) anything not in the snapshot
            foreach (var c in current)
                if (!want.ContainsKey(c.sha1))
                {
                    string path = Path.Combine(modsDir, c.enabled ? c.file : c.file + ".disabled");
                    try { StoreJar(path, store, c.sha1); File.Delete(path); }
                    catch { errors.Add(c.file); }
                }
            // place every wanted jar with the right name + enabled state
            var curBySha = current.GroupBy(m => m.sha1, StringComparer.OrdinalIgnoreCase)
                                  .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            // files of wanted mods that are still waiting for their rename must never be overwritten
            var pending = new HashSet<string>(
                curBySha.Values.Where(c => want.ContainsKey(c.sha1))
                               .Select(c => Path.Combine(modsDir, c.enabled ? c.file : c.file + ".disabled")),
                StringComparer.OrdinalIgnoreCase);
            void FreeTarget(string target)
            {
                if (!File.Exists(target)) return;
                if (pending.Contains(target)) throw new IOException("name clash: " + target);
                File.Delete(target);
            }
            foreach (var w in man.mods)
            {
                string target = Path.Combine(modsDir, w.enabled ? w.file : w.file + ".disabled");
                if (curBySha.TryGetValue(w.sha1, out var c))
                {
                    string have = Path.Combine(modsDir, c.enabled ? c.file : c.file + ".disabled");
                    if (!string.Equals(have, target, StringComparison.OrdinalIgnoreCase) && File.Exists(have))
                    {
                        try
                        {
                            pending.Remove(have);
                            FreeTarget(target);
                            File.Move(have, target);
                            pending.Add(target);
                        }
                        catch { pending.Add(have); errors.Add(w.file); }
                    }
                    continue;
                }
                string stored = Path.Combine(store, w.sha1.ToLowerInvariant() + ".jar");
                string tmp = target + ".tmp";
                try
                {
                    FreeTarget(target);
                    File.Copy(stored, tmp, overwrite: true);
                    File.Move(tmp, target, overwrite: true);
                    pending.Add(target);
                }
                catch
                {
                    try { File.Delete(tmp); } catch { }
                    errors.Add(w.file);
                }
            }

            // 2. configs: unpack to a staging folder first; only a good unpack replaces the tracked dirs/files
            if (man.hasConfig)
            {
                string zipPath = Path.Combine(root, man.id + ".zip");
                string staging = Path.Combine(inst, ".blockify-restore-tmp");
                bool unpacked = false;
                try
                {
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    ZipFile.ExtractToDirectory(zipPath, staging);
                    unpacked = true;
                }
                catch { errors.Add("настройки (архив не распаковался)"); }

                if (unpacked)
                {
                    foreach (var d in SnapConfigDirs)
                    {
                        string live = Path.Combine(inst, d), from = Path.Combine(staging, d);
                        try
                        {
                            if (Directory.Exists(live)) Directory.Delete(live, true);
                            if (Directory.Exists(from)) Directory.Move(from, live);
                        }
                        catch { errors.Add(d + "/"); }
                    }
                    foreach (var f in SnapConfigFiles)
                    {
                        string live = Path.Combine(inst, f), from = Path.Combine(staging, f);
                        try
                        {
                            if (File.Exists(from)) File.Move(from, live, overwrite: true);
                            else if (File.Exists(live)) File.Delete(live);
                        }
                        catch { errors.Add(f); }
                    }
                }
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
            return errors;
        }

        private static string ListShort(List<string> items)
            => string.Join(", ", items.Take(5)) + (items.Count > 5 ? $" и ещё {items.Count - 5}" : "");

        private async Task RestoreSnapshotAsync(string slug, string id)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            string jobId = "restore:" + slug, title = p.Title;
            string inst = PackHome(p), root = SnapRoot(p), store = SnapStore();
            _activeJob = (jobId, title);
            void Phase(string text) { try { Dispatcher.BeginInvoke(() => PostJob(jobId, title, text, 0, 0)); } catch { } }
            try
            {
                PostJob(jobId, title, "Проверяю снимок…", 0, 0);
                string? error = await Task.Run<string?>(() =>
                {
                    // the game keeps jars and configs open: changing them under it leaves a half-restored pack
                    if (IsInstanceRunning(inst)) return GameRunningMsg;

                    lock (SnapStoreLock)
                    {
                        var man = ReadSnapManifest(root, id);
                        var problems = CheckRestorable(man, inst, root, store);
                        if (problems.Count > 0)
                            return $"Откат отменён, сборка не изменена: нет или повреждены {ListShort(problems)}.";

                        Phase("Снимок текущего состояния…");
                        try { Snapshot(p, "", "перед откатом", auto: true, keepId: man.id); }
                        catch (Exception ex) { return "Не удалось сохранить текущее состояние, откат отменён: " + ex.Message; }

                        Phase("Возвращаю моды и настройки…");
                        var errors = ApplyRestore(man, inst, root, store);
                        return errors.Count == 0 ? null
                            : $"Откат выполнен не полностью — не удалось изменить: {ListShort(errors)}. " +
                              "Состояние до отката сохранено снимком «перед откатом».";
                    }
                });

                Post(new { type = "installDone", id = jobId, ok = error == null, error });
                await PushSnapshotsAsync(slug);
            }
            catch (Exception ex)
            {
                Post(new { type = "installDone", id = jobId, ok = false, error = ex.Message });
            }
            finally { _activeJob = null; }
        }

        private async Task MakeSnapshotAsync(string slug, string label)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            try { await Task.Run(() => Snapshot(p, label.Trim(), "вручную", auto: false)); }
            catch (Exception ex) { HandleException(ex); }
            await PushSnapshotsAsync(slug);
            PushInstalledPacks();   // profile count on the pack row
        }

        private async Task DeleteSnapshotAsync(string slug, string id)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            await Task.Run(() =>
            {
                lock (SnapStoreLock)
                {
                    DeleteSnapshotFiles(p, id);
                    GcSnapshotStore();   // jars only this snapshot used are freed now
                }
            });
            await PushSnapshotsAsync(slug);
            PushInstalledPacks();
        }
    }
}
