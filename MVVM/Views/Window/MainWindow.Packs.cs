using BlockifyLauncher.Core.Modrinth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Net.Http;
using System.Text;

namespace BlockifyLauncher
{
    /// <summary>
    /// Modpack management: install a Modrinth .mrpack into an isolated instance,
    /// keep a registry of installed packs, and launch a pack with its own game dir.
    /// Also a download-only "install version" path for the Versions screen.
    /// </summary>
    public partial class MainWindow
    {
        // set just before launching a modpack so StartGame targets its instance folder
        private string? _packGameDir;
        // the pack being launched — StartGame applies its RAM/Java/JVM/screen overrides
        private InstalledPack? _packLaunch;

        // newer catalog versions of installed packs (slug → version), filled once per session; UI thread only
        private readonly Dictionary<string, PackVersionOption> _packUpdates = new();
        private bool _packUpdateCheckStarted;
        private int _pushPacksSeq;

        // network trouble vs a real error (the web UI shows 'offline' as "no connection")
        private static bool IsNetworkError(Exception ex)
            => ex is HttpRequestException or TimeoutException or TaskCanceledException
               || ex.InnerException is HttpRequestException or TimeoutException;

        private void JobFailed(string jobId, Exception ex)
        {
            LogDiag($"job '{jobId}' failed: {ex}");
            Post(new { type = "installDone", id = jobId, ok = false, error = ex.Message });
        }

        private void SavePackSettings(string slug, JToken? v)
        {
            if (v == null) return;
            var list = LoadPacks();
            var p = list.FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            p.RamMb = Math.Max(0, v.Value<int?>("ram") ?? 0);
            p.JavaPath = (v.Value<string>("java") ?? "").Trim();
            p.JvmArgs = (v.Value<string>("jvm") ?? "").Trim();
            p.ScreenW = Math.Max(0, v.Value<int?>("w") ?? 0);
            p.ScreenH = Math.Max(0, v.Value<int?>("h") ?? 0);
            SavePacks(list);
            PushInstalledPacks();

            bool wantFps = v.Value<bool?>("fps") ?? p.FpsBoost;
            if (wantFps != p.FpsBoost) _ = SetFpsBoostAsync(slug, wantFps);
        }

        // ── one-click FPS boost (optimisation mods + Aikar flags), reversible ──
        private async Task SetFpsBoostAsync(string slug, bool enable)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            string jobId = "fps:" + slug;
            string home = PackHome(p);
            _activeJob = (jobId, p.Title);
            try
            {
                if (await Task.Run(() => IsInstanceRunning(home))) throw new InvalidOperationException(GameRunningMsg);
                await AutoSnapshotAsync(p, enable ? "перед бустом FPS" : "перед отключением буста FPS");
                List<string> files;
                if (enable)
                {
                    PostJob(jobId, p.Title, "Включаю буст FPS…", 0, 0);
                    var log = new Progress<InstallProgress>(x => PostJob(jobId, p.Title, x.Phase, x.Cur, x.Total));
                    files = await Task.Run(() => ModpackInstaller.InstallFpsBoostAsync(p, log, home));
                }
                else
                {
                    PostJob(jobId, p.Title, "Выключаю буст FPS…", 0, 0);
                    string modsDir = Path.Combine(home, "mods");
                    foreach (var f in p.FpsFiles ?? new List<string>())
                    {
                        try { File.Delete(Path.Combine(modsDir, SafeName(f))); } catch { }
                        try { File.Delete(Path.Combine(modsDir, SafeName(f) + ".disabled")); } catch { }
                    }
                    files = new List<string>();
                }
                // re-read right before saving: settings may have changed while mods were downloading
                if (!UpdatePack(slug, x => { x.FpsFiles = files; x.FpsBoost = enable; }))
                    throw new InvalidOperationException("Не удалось сохранить список сборок — подробности в журнале.");
                PushInstalledPacks();
                Post(new { type = "installDone", id = jobId, ok = true });
            }
            catch (Exception ex) { JobFailed(jobId, ex); }
            finally { _activeJob = null; }
        }

        // ── install a single mod / shader / resource pack from the catalog into a pack's instance ──
        // returns false when the install failed (the error is already shown in the downloads panel)
        private async Task<bool> InstallContentAsync(string slug, string title, string ptype, string packSlug)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == packSlug);
            if (pack == null) return false;
            string sub = ptype switch { "shader" => "shaderpacks", "resourcepack" => "resourcepacks", _ => "mods" };
            string jobId = $"content:{ptype}:{slug}", jobTitle = (title.Length > 0 ? title : slug) + " → " + pack.Title;
            string home = PackHome(pack);
            _activeJob = (jobId, jobTitle);
            try
            {
                string dir = Path.Combine(home, sub);
                Directory.CreateDirectory(dir);
                PostJob(jobId, jobTitle, "Ищу подходящую версию…", 0, 0);
                if (ptype == "mod")
                {
                    // replacing a jar the running game holds would leave a duplicate or a gap
                    if (await Task.Run(() => IsInstanceRunning(home))) throw new InvalidOperationException(GameRunningMsg);
                    await AutoSnapshotAsync(pack, "перед добавлением мода");
                    // the pack's loader (quilt runs fabric mods) + game version, release first
                    string[] loaders = ModResolver.LoadersFor(pack.Loader);
                    var root = await ModrinthService.GetBestVersionAsync(slug, loaders, pack.McVersion)
                        ?? throw new Exception($"У «{title}» нет версии под {pack.Loader} {pack.McVersion}.");
                    var log = new Progress<InstallProgress>(x => PostJob(jobId, jobTitle, x.Phase, x.Cur, x.Total));
                    // required dependencies recursively; an older copy of the same mod is replaced, not duplicated
                    var res = await Task.Run(() => ModResolver.InstallAsync(dir, root, loaders, pack.McVersion, log));
                    LogDiag($"installContent '{slug}' → '{packSlug}': {res.Summary()}");
                }
                else
                {
                    // shaders/packs only care about the game version, and many are version-agnostic
                    var ver = await ModrinthService.GetBestVersionAsync(slug, null, pack.McVersion)
                           ?? await ModrinthService.GetBestVersionAsync(slug, null, null);
                    var hit = ModrinthService.PrimaryFileWithHash(ver)
                        ?? throw new Exception($"У «{title}» нет версии под {pack.McVersion}.");
                    PostJob(jobId, jobTitle, "Скачиваю " + hit.FileName, 0, 0);
                    await ModrinthService.DownloadToFileAsync(hit.Url, Path.Combine(dir, SafeName(hit.FileName)), hit.Sha1);
                }
                Post(new { type = "installDone", id = jobId, ok = true });
                return true;
            }
            catch (Exception ex)
            {
                JobFailed(jobId, ex);
                return false;
            }
            finally { _activeJob = null; }
        }

        // ── export instance → .mrpack (share with friends / other launchers) ──
        private async Task ExportPackAsync(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Экспорт сборки",
                Filter = "Modrinth modpack|*.mrpack",
                FileName = SafeName(p.Slug) + ".mrpack"
            };
            if (dlg.ShowDialog(this) != true) return;

            string jobId = "export:" + slug;
            p.InstanceDir = PackHome(p);   // local copy only — never saved
            _activeJob = (jobId, p.Title);
            try
            {
                var log = new Progress<InstallProgress>(x => PostJob(jobId, p.Title, x.Phase, x.Cur, x.Total));
                await Task.Run(() => ModpackInstaller.ExportAsync(p, dlg.FileName, log));
                Post(new { type = "installDone", id = jobId, ok = true });
                RevealInExplorer(dlg.FileName);
            }
            catch (Exception ex) { JobFailed(jobId, ex); }
            finally { _activeJob = null; }
        }

        // active background install job (id, title) — while set, game-file download
        // events are forwarded to the web download panel too.
        private (string id, string title)? _activeJob;

        private void PostJob(string id, string title, string phase, int cur, int total)
            => Post(new { type = "jobProgress", id, title, phase, cur, total });

        // ── pack registry (packs.json): atomic writes, a .bak, and never overwritten after a failed read ──

        private static string PacksJsonPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "BlockifyLauncher", "packs.json");

        private static string PacksBakPath() => PacksJsonPath() + ".bak";

        private static readonly object _packsLock = new();
        // packs.json exists but neither it nor its backup could be read: saving now would wipe every pack
        private static bool _packsUnreadable;

        private string PacksRoot() => Path.Combine(McBase(), "blockify-packs");

        private static bool TryReadPacks(string path, out List<InstalledPack> list)
        {
            list = new List<InstalledPack>();
            try
            {
                if (!File.Exists(path)) return false;
                var parsed = JsonConvert.DeserializeObject<List<InstalledPack>>(File.ReadAllText(path));
                if (parsed == null) return false;
                list = parsed;
                return true;
            }
            catch { return false; }
        }

        private List<InstalledPack> LoadPacks()
        {
            string? problem = null;
            List<InstalledPack> result;
            lock (_packsLock)
            {
                string path = PacksJsonPath(), bak = PacksBakPath();
                if (TryReadPacks(path, out var list)) { _packsUnreadable = false; return list; }
                bool mainExists = File.Exists(path);
                if (TryReadPacks(bak, out var fromBak))
                {
                    if (mainExists) LogDiag("packs.json is unreadable — using packs.json.bak");
                    _packsUnreadable = false;
                    return fromBak;
                }
                if (!mainExists) { _packsUnreadable = false; return new List<InstalledPack>(); }

                if (!_packsUnreadable)
                {
                    try { File.Copy(path, path + ".corrupt", overwrite: true); } catch { }
                    problem = "Не удалось прочитать список сборок (packs.json повреждён). Сборки на диске не тронуты, " +
                              "список не будет перезаписан; копия файла — packs.json.corrupt рядом с ним.";
                    LogDiag("packs.json and its backup are unreadable — saving is blocked until it is fixed");
                }
                _packsUnreadable = true;
                result = new List<InstalledPack>();
            }
            if (problem != null) ReportPacksProblem(problem);
            return result;
        }

        /// <summary>Writes packs.json via a temp file (+ .bak of the previous version). Returns false when
        /// nothing was written (the error is logged and shown).</summary>
        private bool SavePacks(List<InstalledPack> list)
        {
            string? problem = null;
            lock (_packsLock)
            {
                string path = PacksJsonPath();
                bool mainReadable = TryReadPacks(path, out _);
                if (_packsUnreadable && File.Exists(path) && !mainReadable)
                {
                    LogDiag("packs.json save refused: the registry could not be read, a save would drop every pack");
                    problem = "Список сборок не сохранён: packs.json повреждён, и запись стёрла бы все сборки. " +
                              "Почини или удали файл packs.json и перезапусти лаунчер.";
                }
                else
                {
                    string tmp = path + ".tmp";
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        string json = JsonConvert.SerializeObject(list, Formatting.Indented);
                        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
                        {
                            sw.Write(json);
                            sw.Flush();
                            fs.Flush(true);
                        }
                        if (mainReadable)
                            File.Replace(tmp, path, PacksBakPath(), ignoreMetadataErrors: true);
                        else
                        {
                            // a broken main file is kept aside, never used as the backup
                            if (File.Exists(path)) { try { File.Copy(path, path + ".corrupt", overwrite: true); } catch { } }
                            File.Move(tmp, path, overwrite: true);
                        }
                        _packsUnreadable = false;
                    }
                    catch (Exception ex)
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                        LogDiag("packs.json save failed: " + ex);
                        problem = "Не удалось сохранить список сборок: " + ex.Message;
                    }
                }
            }
            if (problem == null) return true;
            ReportPacksProblem(problem);
            return false;
        }

        /// <summary>Load → change one pack → save, in one go (fresh copy, so parallel jobs don't undo each other).</summary>
        private bool UpdatePack(string slug, Action<InstalledPack> change)
        {
            var list = LoadPacks();
            var p = list.FirstOrDefault(x => x.Slug == slug);
            if (p == null) return false;
            change(p);
            return SavePacks(list);
        }

        // shown asynchronously on the UI thread: callers may hold locks or run off the dispatcher
        private void ReportPacksProblem(string message)
        {
            try { Dispatcher.BeginInvoke(new Action(() => HandleException(new Exception(message)))); } catch { }
        }

        private void PushInstalledPacks()
        {
            // Post must run on the UI thread; snapshot listing (file I/O) runs in the background
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(PushInstalledPacks)); return; }
            _ = PushInstalledPacksAsync();
            if (!_packUpdateCheckStarted)
            {
                _packUpdateCheckStarted = true;
                _ = CheckPackUpdatesAsync();
            }
        }

        private async Task PushInstalledPacksAsync()
        {
            int seq = ++_pushPacksSeq;
            var packs = LoadPacks();
            List<List<object>> profiles;
            try
            {
                // named (manual) snapshots double as switchable mod profiles
                profiles = await Task.Run(() => packs.Select(p =>
                    ListManifests(p).Where(m => !m.auto)
                        .Select(m => (object)new { m.id, label = m.label.Length > 0 ? m.label : m.at }).ToList()).ToList());
            }
            catch (Exception ex)
            {
                LogDiag("installedPacks: snapshot listing failed: " + ex.Message);
                profiles = packs.Select(_ => new List<object>()).ToList();
            }
            if (seq != _pushPacksSeq) return;   // a newer push is on its way

            var items = packs.Select((p, i) => new
            {
                slug = p.Slug, title = p.Title, icon = p.Icon,
                mc = p.McVersion, loader = p.Loader, version = p.PackVersion, installed = p.InstalledAt,
                settings = new { ram = p.RamMb, java = p.JavaPath, jvm = p.JvmArgs, w = p.ScreenW, h = p.ScreenH, fps = p.FpsBoost },
                profiles = profiles[i],
                // newer version of the pack on Modrinth (same loader + MC), null when up to date / unknown
                update = _packUpdates.TryGetValue(p.Slug, out var up)
                    ? new { id = up.Id, version = up.VersionNumber, name = up.Name }
                    : null
            }).ToList();
            Post(new { type = "installedPacks", items });
        }

        // once per session: is there a newer version of each installed catalog pack?
        private async Task CheckPackUpdatesAsync()
        {
            bool any = false;
            foreach (var p in LoadPacks().Where(x => !string.IsNullOrEmpty(x.VersionId)).ToList())
            {
                try
                {
                    var upd = await ModpackInstaller.FindPackUpdateAsync(p);
                    if (upd != null) { _packUpdates[p.Slug] = upd; any = true; }
                    else _packUpdates.Remove(p.Slug);
                }
                catch (Exception ex)
                {
                    LogDiag($"pack update check '{p.Slug}': {ex.Message}");
                    if (IsNetworkError(ex)) break;   // offline — don't hammer every pack
                }
            }
            if (any) PushInstalledPacks();
        }

        private async Task PushPackVersionsAsync(string slug, string title, string icon)
        {
            try
            {
                var vers = await ModpackInstaller.GetPackVersionsAsync(slug);
                var items = vers.Select(v => new
                {
                    id = v.Id, name = v.Name, version = v.VersionNumber,
                    gameVersion = v.GameVersion, loader = v.Loader, supported = v.Supported
                }).ToList();
                Post(new { type = "packVersions", slug, title, icon, items });
            }
            catch (Exception ex)
            {
                LogDiag($"packVersions '{slug}': {ex}");
                Post(new { type = "packVersions", slug, title, icon, items = Array.Empty<object>(),
                           error = IsNetworkError(ex) ? "offline" : ex.Message });
            }
        }

        private async Task InstallPackAsync(string slug, string title, string icon, string versionId)
        {
            // progress marshals to the UI thread (Progress<T> captures this context)
            var log = new Progress<InstallProgress>(p => PostJob(slug, title, p.Phase, p.Cur, p.Total));
            _activeJob = (slug, title);
            try
            {
                PostJob(slug, title, "Готовлю установку…", 0, 0);
                var existing = LoadPacks().FirstOrDefault(p => p.Slug == slug);
                string instanceDir = existing != null ? PackHome(existing) : Path.Combine(PacksRoot(), SafeName(slug));
                if (existing != null && Directory.Exists(instanceDir))
                {
                    // the running game holds its jars: a reinstall now would half-apply
                    if (await Task.Run(() => IsInstanceRunning(instanceDir))) throw new InvalidOperationException(GameRunningMsg);
                    // reinstalling over an existing instance (repair, share code, newer version) rewrites its mods
                    await AutoSnapshotAsync(existing, "перед переустановкой");
                }

                // the chosen version directly (the version dialog already pulled the full list once)
                var ver = await ModpackInstaller.GetPackVersionAsync(versionId);
                if (ver == null)
                {
                    var vers = await ModpackInstaller.GetPackVersionsAsync(slug);
                    ver = vers.FirstOrDefault(v => v.Id == versionId) ?? vers.FirstOrDefault();
                }
                if (ver == null) throw new Exception("Версия сборки не найдена.");

                string versionsDir = setting.launcher.MinecraftPath.Versions;
                string? previousVersionId = existing?.VersionId;

                // heavy zip/file work off the UI thread so the launcher stays responsive;
                // Forge/NeoForge loaders are installed via the headless installer callback.
                var pack = await Task.Run(() =>
                    ModpackInstaller.InstallAsync(slug, title, icon, ver, versionsDir, instanceDir, log,
                        installLoader: RunLoaderInstallerAsync, previousVersionId: previousVersionId));
                LogDiag($"installPack '{slug}' {previousVersionId ?? "-"} → {ver.Id}: {pack.LastInstallNote}");

                // pull vanilla client + loader libraries for the freshly written profile
                PostJob(slug, title, "Скачиваю файлы игры…", 0, 0);
                await setting.launcher.GetAllVersionsAsync();               // rescan so the new profile resolves
                var v = await setting.launcher.GetVersionAsync(pack.VersionName);
                await setting.launcher.CheckAndDownloadAsync(v);

                pack.InstalledAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                var list = LoadPacks();
                // keep the player's per-pack settings, FPS boost state and cover across repair/update
                pack = ModpackInstaller.MergeInstalled(list.FirstOrDefault(p => p.Slug == pack.Slug), pack);
                // a mod the new version ships itself must not stay twice (boost copies / the player's own copies)
                var dupes = await Task.Run(() => ModpackInstaller.DisableDuplicateMods(pack));
                if (dupes.Count > 0) LogDiag($"installPack '{slug}': duplicates switched off/removed: {string.Join(", ", dupes)}");
                list.RemoveAll(p => p.Slug == pack.Slug);
                list.Insert(0, pack);
                if (!SavePacks(list)) throw new Exception("Сборка установлена, но список сборок не сохранился — подробности в журнале.");
                _packUpdates.Remove(slug);

                await InitializeVersionsAsync();
                PushVersionsWeb();
                PushInstalledPacks();
                Post(new { type = "installDone", id = slug, ok = true });
            }
            catch (Exception ex) { JobFailed(slug, ex); }
            finally { _activeJob = null; }
        }

        private void LaunchPack(string slug) => _ = LaunchPackAsync(slug);

        private async Task LaunchPackAsync(string slug)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) { LogDiag($"launchPack: no pack for slug='{slug}'"); return; }

            string home = PackHome(pack);
            // FPS-boost copies of a mod the pack already has (old name-based boost) crash Forge/Fabric
            // with "duplicate mod" — drop them before starting
            if (pack.FpsFiles != null && pack.FpsFiles.Count > 0)
            {
                try
                {
                    var dupes = await Task.Run(() => ModpackInstaller.DisableDuplicateMods(pack, home));
                    if (dupes.Count > 0)
                    {
                        LogDiag($"launchPack '{slug}': removed duplicate boost mods: {string.Join(", ", dupes)}");
                        var files = pack.FpsFiles.ToList();
                        UpdatePack(slug, x => x.FpsFiles = files);
                    }
                }
                catch (Exception ex) { LogDiag($"launchPack '{slug}': duplicate check failed: {ex.Message}"); }
            }
            LogDiag($"launchPack slug='{slug}' versionName='{pack.VersionName}' instance='{home}'");
            SelectVersion(pack.VersionName);
            LogDiag($"launchPack after SelectVersion selected='{MinecraftVerisonComboBox.SelectedItem}'");

            if (MinecraftVerisonComboBox.SelectedItem?.ToString() != pack.VersionName)
            {
                HandleException(new Exception(
                    $"Профиль сборки «{pack.VersionName}» не найден в списке версий. Переустанови сборку."));
                return;
            }
            pack.InstanceDir = home;   // launch copy only
            _packGameDir = home;
            _packLaunch = pack;
            LaunchSelected();
        }

        private void OpenPackFolder(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p != null) OpenPath(PackHome(p));
        }

        private void OpenPackMods(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p != null) OpenPath(PackModsDir(p));
        }

        // repair / update: re-run install (resume skips files already present & hash-correct;
        // files of the old version nobody changed are removed, the player's files and edits stay)
        private Task ReinstallPack(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null) return Task.CompletedTask;
            if (string.IsNullOrEmpty(p.VersionId))
            {
                HandleException(new Exception("Эта сборка импортирована локально — у неё нет версии на Modrinth для докачки. Переустанови из файла/папки."));
                return Task.CompletedTask;
            }
            return RepairAsync(p);
        }

        // InstallPackAsync snapshots an existing instance before rewriting it
        private Task RepairAsync(InstalledPack p) => InstallPackAsync(p.Slug, p.Title, p.Icon, p.VersionId);

        // ── import: local .mrpack, Prism/MultiMC instance, CurseForge App instance ──
        private async Task ImportPackAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Импорт сборки — выбери .mrpack или файл-описание инстанса",
                Filter = "Сборка (.mrpack, Prism/MultiMC, CurseForge App)|*.mrpack;mmc-pack.json;instance.cfg;minecraftinstance.json|Все файлы|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true) return;

            string path = dlg.FileName;
            string title = path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(path)
                : Path.GetFileName(Path.GetDirectoryName(path) ?? path);
            string jobId = "import:" + title, jobTitle = "Импорт: " + title;
            _activeJob = (jobId, jobTitle);
            try
            {
                var log = new Progress<InstallProgress>(x => PostJob(jobId, jobTitle, x.Phase, x.Cur, x.Total));
                string versionsDir = setting.launcher.MinecraftPath.Versions;
                InstalledPack pack;
                if (path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                {
                    PostJob(jobId, jobTitle, "Читаю .mrpack…", 0, 0);
                    // a local file always becomes a new instance — never silently overwrite an existing pack;
                    // the archive is read straight from disk (no full copy in memory)
                    string slug = ModpackInstaller.UniqueSlug(ModpackInstaller.Slugify(title), PacksRoot());
                    string instanceDir = Path.Combine(PacksRoot(), slug);
                    pack = await Task.Run(() => ModpackInstaller.InstallFromMrpackAsync(
                        path, slug, "", "", "", "", "", versionsDir, instanceDir, log, RunLoaderInstallerAsync));
                }
                else
                {
                    pack = await Task.Run(() => ModpackInstaller.ImportInstanceAsync(
                        path, versionsDir, PacksRoot(), log, RunLoaderInstallerAsync));
                }

                PostJob(jobId, jobTitle, "Скачиваю файлы игры…", 0, 0);
                await setting.launcher.GetAllVersionsAsync();
                var v = await setting.launcher.GetVersionAsync(pack.VersionName);
                await setting.launcher.CheckAndDownloadAsync(v);

                pack.InstalledAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                var list = LoadPacks();
                list.RemoveAll(p => p.Slug == pack.Slug);
                list.Insert(0, pack);
                if (!SavePacks(list)) throw new Exception("Сборка импортирована, но список сборок не сохранился — подробности в журнале.");

                await InitializeVersionsAsync();
                PushVersionsWeb();
                PushInstalledPacks();
                Post(new { type = "installDone", id = jobId, ok = true });
            }
            catch (Exception ex) { JobFailed(jobId, ex); }
            finally { _activeJob = null; }
        }

        // ── per-pack mod management (list / toggle / update / delete inside the instance) ──

        private string PackModsDir(InstalledPack p) => Path.Combine(PackHome(p), "mods");

        private async Task PushPackModsAsync(string slug)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            var items = new List<object>();
            string? checkError = null;
            try
            {
                var dir = PackModsDir(pack);
                if (Directory.Exists(dir))
                {
                    // enabled .jar + disabled .jar.disabled, hashed off the UI thread
                    var files = await Task.Run(() =>
                        new DirectoryInfo(dir).GetFiles("*.jar")
                            .Concat(new DirectoryInfo(dir).GetFiles("*.jar.disabled"))
                            .Select(f => (f, hash: TrySha1(f.FullName)))
                            .ToList());

                    var hashes = files.Where(x => x.hash != null).Select(x => x.hash!).Distinct().ToList();
                    Dictionary<string, ModUpdateInfo> updates;
                    try
                    {
                        updates = await ModrinthService.CheckModUpdatesAsync(hashes,
                            ModResolver.LoadersFor(pack.Loader).ToList(), new List<string> { pack.McVersion });
                    }
                    catch (Exception ex)
                    {
                        // couldn't ask Modrinth — that's not the same as "not on Modrinth"
                        LogDiag($"packMods '{slug}': update check failed: {ex.Message}");
                        checkError = IsNetworkError(ex) ? "offline" : ex.Message;
                        updates = new Dictionary<string, ModUpdateInfo>();
                    }

                    // updatable first, then enabled, then by name
                    var rows = files.Select(x =>
                    {
                        var (f, hash) = x;
                        bool enabled = !f.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                        var u = hash != null ? updates.GetValueOrDefault(hash) : null;
                        string name = u?.Title ?? Path.GetFileNameWithoutExtension(
                            enabled ? f.Name : f.Name[..^".disabled".Length]);
                        bool hasUpdate = (u?.HasUpdate ?? false) && enabled;
                        return (f, hash, enabled, u, name, hasUpdate);
                    })
                    .OrderByDescending(r => r.hasUpdate)
                    .ThenByDescending(r => r.enabled)
                    .ThenBy(r => r.name, StringComparer.OrdinalIgnoreCase);

                    foreach (var r in rows)
                        items.Add(new
                        {
                            file = r.f.Name,
                            hash = r.hash ?? "",
                            enabled = r.enabled,
                            name = r.name,
                            size = HumanSize(r.f.Length),
                            // when the check itself failed, say so instead of "нет на Modrinth"
                            current = checkError != null ? "обновления не проверены" : r.u?.CurrentVersion ?? "",
                            latest = r.u?.LatestVersion ?? "",
                            latestType = r.u?.LatestVersionType ?? "",
                            hasUpdate = r.hasUpdate,
                            known = checkError != null || r.u?.CurrentVersion != null,
                            checkFailed = checkError != null
                        });
                }
            }
            catch (Exception ex)
            {
                LogDiag($"packMods '{slug}': {ex}");
                Post(new { type = "packMods", slug, items = Array.Empty<object>(), error = IsNetworkError(ex) ? "offline" : ex.Message });
                return;
            }
            Post(new { type = "packMods", slug, items, checkError });
        }

        private string? TrySha1(string path)
        {
            try { return Sha1File(path); } catch { return null; }
        }

        private async Task TogglePackModAsync(string slug, string file)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            try
            {
                string home = PackHome(pack);
                if (await Task.Run(() => IsInstanceRunning(home))) throw new InvalidOperationException(GameRunningMsg);
                var path = Path.Combine(PackModsDir(pack), SafeName(file));
                if (File.Exists(path))
                {
                    string target = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? path[..^".disabled".Length]
                        : path + ".disabled";
                    if (!File.Exists(target)) File.Move(path, target);
                }
            }
            catch (Exception ex) { HandleException(ex); }
            await PushPackModsAsync(slug);
        }

        // «Обновить»: newest fitting version (pack loader + MC, same release channel) replaces the old jar
        // in one step, and its new required dependencies come along
        private async Task UpdatePackModAsync(string slug, string file, string hash)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            string jobId = "modupdate:" + slug + ":" + file;
            string jobTitle = Path.GetFileNameWithoutExtension(file) + " → " + pack.Title;
            try
            {
                string home = PackHome(pack);
                if (await Task.Run(() => IsInstanceRunning(home))) throw new InvalidOperationException(GameRunningMsg);
                PostJob(jobId, jobTitle, "Ищу обновление…", 0, 0);
                await AutoSnapshotAsync(pack, "перед обновлением мода");
                var dir = PackModsDir(pack);
                string[] loaders = ModResolver.LoadersFor(pack.Loader);
                var updates = await ModrinthService.CheckModUpdatesAsync(new List<string> { hash },
                    loaders.ToList(), new List<string> { pack.McVersion });
                if (!(updates.TryGetValue(hash, out var u) && u.HasUpdate && u.Latest is JToken latest))
                    throw new Exception($"Для этого мода нет подходящего обновления под {pack.Loader} {pack.McVersion}.");

                var log = new Progress<InstallProgress>(x => PostJob(jobId, jobTitle, x.Phase, x.Cur, x.Total));
                var res = await Task.Run(() => ModResolver.InstallAsync(dir, latest, loaders, pack.McVersion, log,
                    replaceFile: SafeName(file)));
                LogDiag($"updatePackMod '{slug}' {file}: {res.Summary()}");
                Post(new { type = "installDone", id = jobId, ok = true });
            }
            catch (Exception ex) { JobFailed(jobId, ex); }
            await PushPackModsAsync(slug);
        }

        private async Task DeletePackModAsync(string slug, string file)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            try
            {
                string home = PackHome(pack);
                if (await Task.Run(() => IsInstanceRunning(home))) throw new InvalidOperationException(GameRunningMsg);
                await AutoSnapshotAsync(pack, "перед удалением мода");
                var path = Path.Combine(PackModsDir(pack), SafeName(file));
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
            await PushPackModsAsync(slug);
        }

        private void RemovePack(string slug) => _ = RemovePackAsync(slug);

        // folder first (to the recycle bin), registry entry only once the folder is really gone
        private async Task RemovePackAsync(string slug)
        {
            try
            {
                var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
                if (p != null)
                {
                    string home = PackHome(p);
                    if (await Task.Run(() => IsInstanceRunning(home))) throw new InvalidOperationException(GameRunningMsg);
                    if (Directory.Exists(home))
                    {
                        try
                        {
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                                home,
                                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        }
                        catch (OperationCanceledException) { throw new Exception("Удаление сборки отменено."); }
                        catch (Exception ex) { LogDiag($"removePack '{slug}': {ex.Message}"); }
                        if (Directory.Exists(home))
                            throw new IOException("Не удалось удалить папку сборки (файлы заняты?) — сборка осталась в списке.");
                    }

                    // its snapshots go too; the shared jar store is swept afterwards
                    string snaps = SnapRoot(p);
                    try { if (Directory.Exists(snaps)) Directory.Delete(snaps, true); }
                    catch (Exception ex) { LogDiag($"removePack '{slug}': snapshots: {ex.Message}"); }

                    if (!UpdatePackList(list => list.RemoveAll(x => x.Slug == slug)))
                        throw new Exception("Папка сборки удалена, но список сборок не сохранился — подробности в журнале.");
                    _packUpdates.Remove(slug);
                    _ = Task.Run(GcSnapshotStore);
                }
            }
            catch (Exception ex) { HandleException(ex); }
            PushInstalledPacks();
        }

        private bool UpdatePackList(Action<List<InstalledPack>> change)
        {
            var list = LoadPacks();
            change(list);
            return SavePacks(list);
        }

        // ── download-only version install (Versions screen) ──
        private async Task InstallVersionAsync(string name)
        {
            string id = "ver:" + name;
            _activeJob = (id, name);
            try
            {
                PostJob(id, name, "Скачиваю " + name + "…", 0, 0);
                var v = await setting.launcher.GetVersionAsync(name);
                await setting.launcher.CheckAndDownloadAsync(v);
                await InitializeVersionsAsync();
                PushVersionsWeb();
                Post(new { type = "installDone", id, ok = true });
            }
            catch (Exception ex) { JobFailed(id, ex); }
            finally { _activeJob = null; }
        }

        private void PushVersionsWeb()
            => Post(new
            {
                type = "versions",
                items = BuildVersionRows(),
                versionNames = GetVersionNames(),
                selectedVersion = MinecraftVerisonComboBox.SelectedItem?.ToString() ?? ""
            });
    }
}
