using BlockifyLauncher.Core.Modrinth;
using Newtonsoft.Json;
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
                mc = p.McVersion, loader = p.Loader, version = p.PackVersion, installed = p.InstalledAt
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
            return InstallPackAsync(p.Slug, p.Title, p.Icon, p.VersionId);
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
