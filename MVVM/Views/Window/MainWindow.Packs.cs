using BlockifyLauncher.Core.Modrinth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

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
            var list = LoadPacks();
            var p = list.FirstOrDefault(x => x.Slug == slug);
            if (p == null) return;
            string jobId = "fps:" + slug;
            _activeJob = (jobId, p.Title);
            try
            {
                await AutoSnapshotAsync(p, enable ? "перед бустом FPS" : "перед отключением буста FPS");
                if (enable)
                {
                    PostJob(jobId, p.Title, "Включаю буст FPS…", 0, 0);
                    var log = new Progress<InstallProgress>(x => PostJob(jobId, p.Title, x.Phase, x.Cur, x.Total));
                    var added = await Task.Run(() => ModpackInstaller.InstallFpsBoostAsync(p, log));
                    p.FpsFiles = added;
                    p.FpsBoost = true;
                }
                else
                {
                    PostJob(jobId, p.Title, "Выключаю буст FPS…", 0, 0);
                    string modsDir = Path.Combine(p.InstanceDir, "mods");
                    foreach (var f in p.FpsFiles)
                    {
                        try { File.Delete(Path.Combine(modsDir, SafeName(f))); } catch { }
                        try { File.Delete(Path.Combine(modsDir, SafeName(f) + ".disabled")); } catch { }
                    }
                    p.FpsFiles = new List<string>();
                    p.FpsBoost = false;
                }
                SavePacks(list);
                PushInstalledPacks();
                Post(new { type = "installDone", id = jobId, ok = true });
            }
            catch (Exception ex)
            {
                Post(new { type = "installDone", id = jobId, ok = false, error = ex.Message });
            }
            finally { _activeJob = null; }
        }

        // ── install a single mod / shader / resource pack from the catalog into a pack's instance ──
        private async Task InstallContentAsync(string slug, string title, string ptype, string packSlug)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == packSlug);
            if (pack == null) return;
            string sub = ptype switch { "shader" => "shaderpacks", "resourcepack" => "resourcepacks", _ => "mods" };
            string jobId = $"content:{ptype}:{slug}", jobTitle = (title.Length > 0 ? title : slug) + " → " + pack.Title;
            _activeJob = (jobId, jobTitle);
            try
            {
                if (ptype == "mod") await AutoSnapshotAsync(pack, "перед добавлением мода");
                PostJob(jobId, jobTitle, "Ищу подходящую версию…", 0, 0);
                // mods must match the pack's loader (quilt runs fabric mods); shaders/packs only the game version,
                // and many of those are version-agnostic — fall back to "any version" if nothing matched
                string[]? loaders = ptype == "mod"
                    ? (pack.Loader == "quilt" ? new[] { "quilt", "fabric" } : new[] { pack.Loader })
                    : null;
                var hit = await ModrinthService.GetLatestVersionFileAsync(slug, loaders, pack.McVersion)
                       ?? (ptype != "mod" ? await ModrinthService.GetLatestVersionFileAsync(slug, (string[]?)null, null) : null);
                if (hit == null)
                    throw new Exception($"У «{title}» нет версии под {pack.Loader} {pack.McVersion}.");

                string dir = Path.Combine(pack.InstanceDir, sub);
                Directory.CreateDirectory(dir);
                string dest = Path.Combine(dir, SafeName(hit.Value.FileName));
                PostJob(jobId, jobTitle, "Скачиваю " + hit.Value.FileName, 0, 0);
                await File.WriteAllBytesAsync(dest, await ModrinthService.DownloadAsync(hit.Value.Url));

                // mods: pull required dependencies (one level) so the game doesn't complain on launch
                if (ptype == "mod")
                {
                    var ver = await ModrinthService.GetLatestVersionAsync(slug, loaders, pack.McVersion);
                    var deps = ((Newtonsoft.Json.Linq.JArray?)ver?["dependencies"] ?? new Newtonsoft.Json.Linq.JArray())
                        .Where(d => d.Value<string>("dependency_type") == "required" && !string.IsNullOrEmpty(d.Value<string>("project_id")))
                        .ToList();
                    int i = 0;
                    foreach (var d in deps)
                    {
                        string pid = d.Value<string>("project_id")!;
                        PostJob(jobId, jobTitle, "Зависимость " + pid, ++i, deps.Count);
                        var dv = await ModrinthService.GetLatestVersionAsync(pid, loaders, pack.McVersion);
                        var df = ModrinthService.PrimaryFile(dv);
                        if (df == null) continue;
                        string ddest = Path.Combine(dir, SafeName(df.Value.FileName));
                        if (File.Exists(ddest) || File.Exists(ddest + ".disabled")) continue;
                        try { await File.WriteAllBytesAsync(ddest, await ModrinthService.DownloadAsync(df.Value.Url)); } catch { }
                    }
                }
                Post(new { type = "installDone", id = jobId, ok = true });
            }
            catch (Exception ex)
            {
                Post(new { type = "installDone", id = jobId, ok = false, error = ex.Message });
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
            _activeJob = (jobId, p.Title);
            try
            {
                var log = new Progress<InstallProgress>(x => PostJob(jobId, p.Title, x.Phase, x.Cur, x.Total));
                await Task.Run(() => ModpackInstaller.ExportAsync(p, dlg.FileName, log));
                Post(new { type = "installDone", id = jobId, ok = true });
                RevealInExplorer(dlg.FileName);
            }
            catch (Exception ex)
            {
                Post(new { type = "installDone", id = jobId, ok = false, error = ex.Message });
            }
            finally { _activeJob = null; }
        }

        // active background install job (id, title) — while set, game-file download
        // events are forwarded to the web download panel too.
        private (string id, string title)? _activeJob;

        private void PostJob(string id, string title, string phase, int cur, int total)
            => Post(new { type = "jobProgress", id, title, phase, cur, total });

        private static string PacksJsonPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "BlockifyLauncher", "packs.json");

        private string PacksRoot() => Path.Combine(McBase(), "blockify-packs");

        private List<InstalledPack> LoadPacks()
        {
            try
            {
                if (File.Exists(PacksJsonPath()))
                    return JsonConvert.DeserializeObject<List<InstalledPack>>(File.ReadAllText(PacksJsonPath()))
                           ?? new List<InstalledPack>();
            }
            catch { }
            return new List<InstalledPack>();
        }

        private void SavePacks(List<InstalledPack> list)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PacksJsonPath())!);
                File.WriteAllText(PacksJsonPath(), JsonConvert.SerializeObject(list, Formatting.Indented));
            }
            catch { }
        }

        private void PushInstalledPacks()
        {
            var items = LoadPacks().Select(p => new
            {
                slug = p.Slug, title = p.Title, icon = p.Icon,
                mc = p.McVersion, loader = p.Loader, version = p.PackVersion, installed = p.InstalledAt,
                settings = new { ram = p.RamMb, java = p.JavaPath, jvm = p.JvmArgs, w = p.ScreenW, h = p.ScreenH, fps = p.FpsBoost },
                // named (manual) snapshots double as switchable mod profiles
                profiles = ListManifests(p).Where(m => !m.auto).Select(m => new { m.id, label = m.label.Length > 0 ? m.label : m.at }).ToList()
            }).ToList();
            Post(new { type = "installedPacks", items });
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
                Post(new { type = "packVersions", slug, title, icon, items = Array.Empty<object>(), error = ex.Message });
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
                var vers = await ModpackInstaller.GetPackVersionsAsync(slug);
                var ver = vers.FirstOrDefault(v => v.Id == versionId) ?? vers.FirstOrDefault();
                if (ver == null) throw new Exception("Версия сборки не найдена.");

                string instanceDir = Path.Combine(PacksRoot(), SafeName(slug));
                string versionsDir = setting.launcher.MinecraftPath.Versions;

                // heavy zip/file work off the UI thread so the launcher stays responsive;
                // Forge/NeoForge loaders are installed via the headless installer callback.
                var pack = await Task.Run(() =>
                    ModpackInstaller.InstallAsync(slug, title, icon, ver, versionsDir, instanceDir, log,
                        installLoader: RunLoaderInstallerAsync));

                // pull vanilla client + loader libraries for the freshly written profile
                PostJob(slug, title, "Скачиваю файлы игры…", 0, 0);
                await setting.launcher.GetAllVersionsAsync();               // rescan so the new profile resolves
                var v = await setting.launcher.GetVersionAsync(pack.VersionName);
                await setting.launcher.CheckAndDownloadAsync(v);

                pack.InstalledAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                var list = LoadPacks();
                list.RemoveAll(p => p.Slug == pack.Slug);
                list.Insert(0, pack);
                SavePacks(list);

                await InitializeVersionsAsync();
                PushVersionsWeb();
                PushInstalledPacks();
                Post(new { type = "installDone", id = slug, ok = true });
            }
            catch (Exception ex)
            {
                Post(new { type = "installDone", id = slug, ok = false, error = ex.Message });
            }
            finally { _activeJob = null; }
        }

        private void LaunchPack(string slug)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) { LogDiag($"launchPack: no pack for slug='{slug}'"); return; }

            LogDiag($"launchPack slug='{slug}' versionName='{pack.VersionName}' instance='{pack.InstanceDir}'");
            SelectVersion(pack.VersionName);
            LogDiag($"launchPack after SelectVersion selected='{MinecraftVerisonComboBox.SelectedItem}'");

            if (MinecraftVerisonComboBox.SelectedItem?.ToString() != pack.VersionName)
            {
                HandleException(new Exception(
                    $"Профиль сборки «{pack.VersionName}» не найден в списке версий. Переустанови сборку."));
                return;
            }
            _packGameDir = pack.InstanceDir;
            _packLaunch = pack;
            LaunchSelected();
        }

        private void OpenPackFolder(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p != null) OpenPath(p.InstanceDir);
        }

        private void OpenPackMods(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p != null) OpenPath(Path.Combine(p.InstanceDir, "mods"));
        }

        // repair / update: re-run install (resume skips files already present & hash-correct)
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

        private async Task RepairAsync(InstalledPack p)
        {
            await AutoSnapshotAsync(p, "перед починкой");
            await InstallPackAsync(p.Slug, p.Title, p.Icon, p.VersionId);
        }

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
                    var bytes = await File.ReadAllBytesAsync(path);
                    string slug = ModpackInstaller.Slugify(title);
                    pack = await Task.Run(() => ModpackInstaller.InstallFromMrpackAsync(
                        bytes, slug, "", "", "", "", "", versionsDir, Path.Combine(PacksRoot(), slug), log, RunLoaderInstallerAsync));
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
                SavePacks(list);

                await InitializeVersionsAsync();
                PushVersionsWeb();
                PushInstalledPacks();
                Post(new { type = "installDone", id = jobId, ok = true });
            }
            catch (Exception ex)
            {
                Post(new { type = "installDone", id = jobId, ok = false, error = ex.Message });
            }
            finally { _activeJob = null; }
        }

        // ── per-pack mod management (list / toggle / update / delete inside the instance) ──

        private string PackModsDir(InstalledPack p) => Path.Combine(p.InstanceDir, "mods");

        private async Task PushPackModsAsync(string slug)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            var items = new List<object>();
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
                            new List<string> { pack.Loader }, new List<string> { pack.McVersion });
                    }
                    catch { updates = new Dictionary<string, ModUpdateInfo>(); }

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
                            current = r.u?.CurrentVersion ?? "",
                            latest = r.u?.LatestVersion ?? "",
                            hasUpdate = r.hasUpdate,
                            known = r.u?.CurrentVersion != null
                        });
                }
            }
            catch { }
            Post(new { type = "packMods", slug, items });
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

        private async Task UpdatePackModAsync(string slug, string file, string hash)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            await AutoSnapshotAsync(pack, "перед обновлением мода");
            try
            {
                var dir = PackModsDir(pack);
                var updates = await ModrinthService.CheckModUpdatesAsync(new List<string> { hash },
                    new List<string> { pack.Loader }, new List<string> { pack.McVersion });
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
            await PushPackModsAsync(slug);
        }

        private async Task DeletePackModAsync(string slug, string file)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            await AutoSnapshotAsync(pack, "перед удалением мода");
            try
            {
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

        private void RemovePack(string slug)
        {
            try
            {
                var list = LoadPacks();
                var p = list.FirstOrDefault(x => x.Slug == slug);
                list.RemoveAll(x => x.Slug == slug);
                SavePacks(list);
                if (p != null && Directory.Exists(p.InstanceDir))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            p.InstanceDir,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch { }
                }
            }
            catch (Exception ex) { HandleException(ex); }
            PushInstalledPacks();
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
            catch (Exception ex)
            {
                Post(new { type = "installDone", id, ok = false, error = ex.Message });
            }
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
