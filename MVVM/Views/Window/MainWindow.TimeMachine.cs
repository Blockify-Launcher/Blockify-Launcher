using BlockifyLauncher.Core.Modrinth;
using Newtonsoft.Json;
using System.IO;
using System.IO.Compression;

namespace BlockifyLauncher
{
    /// <summary>
    /// Time machine for pack instances: a snapshot = manifest of mods (name, sha1, enabled)
    /// + a zip of configs. Jars live in a content-addressed store (one copy per sha1), so
    /// snapshots are cheap even for 300 MB mod folders. Restore rebuilds mods/ + config/ exactly.
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

        private string SnapRoot(InstalledPack p) => Path.Combine(PacksRoot(), ".snapshots", SafeName(p.Slug));
        private string SnapStore() => Path.Combine(PacksRoot(), ".snapshots", "store");

        private static List<SnapMod> ScanSnapMods(InstalledPack p)
        {
            var list = new List<SnapMod>();
            string dir = Path.Combine(p.InstanceDir, "mods");
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

        // ── create ──
        private SnapManifest Snapshot(InstalledPack p, string label, string reason, bool auto)
        {
            Directory.CreateDirectory(SnapRoot(p));
            Directory.CreateDirectory(SnapStore());
            var mods = ScanSnapMods(p);
            string modsDir = Path.Combine(p.InstanceDir, "mods");
            foreach (var m in mods)
            {
                string stored = Path.Combine(SnapStore(), m.sha1 + ".jar");
                if (File.Exists(stored)) continue;
                string src = Path.Combine(modsDir, m.enabled ? m.file : m.file + ".disabled");
                try { File.Copy(src, stored); } catch { }
            }

            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            while (File.Exists(Path.Combine(SnapRoot(p), id + ".json"))) id += "x";
            var man = new SnapManifest
            {
                id = id, label = label, reason = reason, auto = auto,
                at = DateTime.Now.ToString("dd.MM.yyyy HH:mm"), mods = mods
            };

            string zipPath = Path.Combine(SnapRoot(p), id + ".zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var d in SnapConfigDirs)
                {
                    string full = Path.Combine(p.InstanceDir, d);
                    if (!Directory.Exists(full)) continue;
                    foreach (var f in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
                    {
                        zip.CreateEntryFromFile(f, Path.GetRelativePath(p.InstanceDir, f).Replace('\\', '/'));
                        man.hasConfig = true;
                    }
                }
                foreach (var f in SnapConfigFiles)
                {
                    string full = Path.Combine(p.InstanceDir, f);
                    if (File.Exists(full)) { zip.CreateEntryFromFile(full, f); man.hasConfig = true; }
                }
            }
            File.WriteAllText(Path.Combine(SnapRoot(p), id + ".json"), JsonConvert.SerializeObject(man));
            TrimAutoSnapshots(p, keep: 25);
            return man;
        }

        private void TrimAutoSnapshots(InstalledPack p, int keep)
        {
            try
            {
                var autos = ListManifests(p).Where(m => m.auto).Skip(keep).ToList();
                foreach (var m in autos) DeleteSnapshotFiles(p, m.id);
            }
            catch { }
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
                    if (m != null) list.Add(m);
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

        // silent snapshot before a risky change; throttled so bursts don't spam the timeline
        private DateTime _lastAutoSnap = DateTime.MinValue;
        private string _lastAutoReason = "";
        private async Task AutoSnapshotAsync(InstalledPack p, string reason)
        {
            try
            {
                if (reason == _lastAutoReason && (DateTime.Now - _lastAutoSnap).TotalSeconds < 60) return;
                await Task.Run(() => Snapshot(p, "", reason, auto: true));
                _lastAutoSnap = DateTime.Now; _lastAutoReason = reason;
            }
            catch (Exception ex) { LogDiag("auto-snapshot failed: " + ex.Message); }
        }

        // ── list (with diff against the current state) ──
        private async Task PushSnapshotsAsync(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            var payload = await Task.Run(() =>
            {
                var current = ScanSnapMods(p);
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
        private async Task RestoreSnapshotAsync(string slug, string id)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            string jobId = "restore:" + slug;
            _activeJob = (jobId, p.Title);
            try
            {
                PostJob(jobId, p.Title, "Снимок текущего состояния…", 0, 0);
                await Task.Run(() => Snapshot(p, "", "перед откатом", auto: true));

                int missing = await Task.Run(() =>
                {
                    var manPath = Path.Combine(SnapRoot(p), SafeName(id) + ".json");
                    var man = JsonConvert.DeserializeObject<SnapManifest>(File.ReadAllText(manPath))
                              ?? throw new Exception("Снимок повреждён.");
                    string modsDir = Path.Combine(p.InstanceDir, "mods");
                    Directory.CreateDirectory(modsDir);

                    // 1. mods: rebuild the exact set from current files + the store
                    var current = ScanSnapMods(p);
                    var want = man.mods.GroupBy(m => m.sha1).ToDictionary(g => g.Key, g => g.First());
                    int miss = 0;

                    // remove (after parking in the store) anything not in the snapshot
                    foreach (var c in current)
                        if (!want.ContainsKey(c.sha1))
                        {
                            string path = Path.Combine(modsDir, c.enabled ? c.file : c.file + ".disabled");
                            string stored = Path.Combine(SnapStore(), c.sha1 + ".jar");
                            try { if (!File.Exists(stored)) File.Copy(path, stored); File.Delete(path); } catch { }
                        }
                    // place every wanted jar with the right name + enabled state
                    var curBySha = current.GroupBy(m => m.sha1).ToDictionary(g => g.Key, g => g.First());
                    foreach (var w in man.mods)
                    {
                        string target = Path.Combine(modsDir, w.enabled ? w.file : w.file + ".disabled");
                        if (curBySha.TryGetValue(w.sha1, out var c))
                        {
                            string have = Path.Combine(modsDir, c.enabled ? c.file : c.file + ".disabled");
                            if (!string.Equals(have, target, StringComparison.OrdinalIgnoreCase) && File.Exists(have))
                            {
                                try { if (File.Exists(target)) File.Delete(target); File.Move(have, target); } catch { }
                            }
                            continue;
                        }
                        string stored = Path.Combine(SnapStore(), w.sha1 + ".jar");
                        if (File.Exists(stored)) { try { File.Copy(stored, target, overwrite: true); } catch { miss++; } }
                        else miss++;
                    }

                    // 2. configs: wipe the tracked config dirs/files, then extract the snapshot's copy
                    if (man.hasConfig)
                    {
                        foreach (var d in SnapConfigDirs)
                        { try { Directory.Delete(Path.Combine(p.InstanceDir, d), true); } catch { } }
                        foreach (var f in SnapConfigFiles)
                        { try { File.Delete(Path.Combine(p.InstanceDir, f)); } catch { } }
                        string zipPath = Path.Combine(SnapRoot(p), SafeName(id) + ".zip");
                        if (File.Exists(zipPath)) ZipFile.ExtractToDirectory(zipPath, p.InstanceDir, overwriteFiles: true);
                    }
                    return miss;
                });

                Post(new { type = "installDone", id = jobId, ok = missing == 0,
                           error = missing > 0 ? $"Откат сделан, но {missing} мод(ов) не найдено в хранилище" : null });
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
            DeleteSnapshotFiles(p, id);
            await PushSnapshotsAsync(slug);
            PushInstalledPacks();
        }
    }
}
