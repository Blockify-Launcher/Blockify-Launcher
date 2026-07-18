using BlockifyLauncher.Core.Modrinth;
using BlockifyLauncher.Core.News;
using BlockifyLauncher.MVVM.Views.Pages.Func.Setting;
using BlockifyLauncher.Resources;
using BlockifyLib.Launcher.Minecraft.Auth;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BlockifyLauncher
{
    /// <summary>
    /// WebView2 bridge: the whole UI is HTML (WebUI/index.html); this connects
    /// it to the existing launcher logic (launch, versions, accounts, settings)
    /// and feeds it live data (news, Modrinth packs).
    /// </summary>
    public partial class MainWindow
    {
        private bool _webReady, _dataReady;
        private string _packLoader = "";
        private string _packQuery = "";
        private string _packMc = "";
        private string _packSort = "";

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();

        // Start a native window-move loop (the click came from inside the WebView,
        // so WPF's DragMove can't be used — ask Windows to move the window).
        private void StartWindowDrag()
        {
            if (WindowState == WindowState.Maximized) return;
            ReleaseCapture();
            SendMessage(new WindowInteropHelper(this).Handle, 0xA1u /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);
        }

        // Native window resize from an HTML edge grip (WebView covers WPF's own grips).
        private void StartWindowResize(string dir)
        {
            if (WindowState == WindowState.Maximized) return;
            int ht = dir switch
            {
                "left" => 10, "right" => 11, "top" => 12, "top-left" => 13,
                "top-right" => 14, "bottom" => 15, "bottom-left" => 16, "bottom-right" => 17,
                _ => 0
            };
            if (ht == 0) return;
            ReleaseCapture();
            SendMessage(new WindowInteropHelper(this).Handle, 0xA1u /*WM_NCLBUTTONDOWN*/, (IntPtr)ht, IntPtr.Zero);
        }

        // ── init ──
        private async Task InitWebAsync()
        {
            string dataFolder = Path.Combine(Path.GetTempPath(), "BlockifyWebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await Web.EnsureCoreWebView2Async(env);

            string webRoot = Path.Combine(AppContext.BaseDirectory, "WebUI");
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.blockify", webRoot, CoreWebView2HostResourceAccessKind.Allow);

            // expose the .minecraft folder (screenshots, world icons) and the local
            // skin library to the page as read-only image hosts.
            try
            {
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "mc.assets", McBase(), CoreWebView2HostResourceAccessKind.Allow);
            }
            catch { }
            try
            {
                Directory.CreateDirectory(SkinsDir());
                Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "skins.assets", SkinsDir(), CoreWebView2HostResourceAccessKind.Allow);
            }
            catch { }

            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xFF, 0x0B, 0x0E, 0x10);

            Web.CoreWebView2.WebMessageReceived += OnWebMessage;

            LaunchStateChanged -= OnLaunchStateWeb;
            LaunchStateChanged += OnLaunchStateWeb;

            // cache-bust so edited UI assets always load (disk cache survives restarts).
            // version = newest mtime across WebUI so any edit invalidates html + js.
            long ver = 0;
            try
            {
                foreach (var f in Directory.GetFiles(webRoot, "*.*", SearchOption.AllDirectories))
                    ver = Math.Max(ver, File.GetLastWriteTimeUtc(f).Ticks);
            }
            catch { }
            Web.CoreWebView2.Navigate($"https://app.blockify/index.html?v={ver}");
        }

        private void Post(object payload)
        {
            if (Web?.CoreWebView2 == null) return;
            Web.CoreWebView2.PostWebMessageAsJson(JsonConvert.SerializeObject(payload));
        }

        private void OnLaunchStateWeb(bool busy) => Post(new { type = "launchState", busy });

        // ── inbound messages from JS ──
        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            JObject m;
            try { m = JObject.Parse(e.WebMessageAsJson); } catch { return; }
            switch (m.Value<string>("type") ?? "")
            {
                case "ready": _webReady = true; TryPushInit(); break;

                case "min": WindowState = WindowState.Minimized; break;
                case "max": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break;
                case "close":
                    new Properties.Settings().SettingsSavingSizeForms((int)Width, (int)Height);
                    Application.Current.Shutdown();
                    break;
                case "drag": StartWindowDrag(); break;
                case "resize": StartWindowResize(m.Value<string>("dir") ?? ""); break;

                case "launch": LaunchSelected(); break;
                case "selectVersion": SelectVersion(m.Value<string>("name") ?? ""); break;

                case "filterPacks": _packLoader = m.Value<string>("loader") ?? ""; _ = PushPacksAsync(); break;
                case "searchPacks":
                    _packQuery = m.Value<string>("query") ?? "";
                    _packLoader = m.Value<string>("loader") ?? "";
                    _packMc = m.Value<string>("gameVersion") ?? "";
                    _packSort = m.Value<string>("sort") ?? "";
                    _ = PushPacksAsync();
                    break;
                case "openPack": OpenUrl("https://modrinth.com/modpack/" + (m.Value<string>("slug") ?? "")); break;
                case "openLink": OpenUrl(m.Value<string>("url") ?? ""); break;

                case "addOffline": AddOfflineAccount(m.Value<string>("nick") ?? ""); break;
                case "msLogin": _ = MicrosoftLoginAsync(); break;
                case "setActive": SetActiveAccount(m.Value<string>("id") ?? ""); break;
                case "removeAccount": RemoveAccount(m.Value<string>("id") ?? ""); break;

                case "setting": ApplySetting(m.Value<string>("key") ?? "", m["value"]); break;

                // ── feature screens ──
                case "loadScreens": PushScreenshots(); break;
                case "openScreenshot": OpenScreenshot(m.Value<string>("file") ?? ""); break;
                case "openScreensFolder": OpenPath(Path.Combine(McBase(), "screenshots")); break;
                case "openShot": OpenShotData(m.Value<string>("file") ?? ""); break;
                case "saveShot": SaveShot(m.Value<string>("file") ?? "", m.Value<string>("dataUrl") ?? "", m.Value<string>("mode") ?? "overwrite"); break;
                case "deleteShot": DeleteShot(m.Value<string>("file") ?? ""); break;

                case "loadWorlds": PushWorlds(); break;
                case "backupWorld": BackupWorld(m.Value<string>("name") ?? ""); break;
                case "restoreWorld": RestoreWorld(m.Value<string>("name") ?? "", m.Value<string>("backup") ?? ""); break;
                case "openWorldFolder": OpenWorldFolder(m.Value<string>("name") ?? ""); break;
                case "openSavesFolder": OpenPath(Path.Combine(McBase(), "saves")); break;

                case "loadMods": _ = PushModsAsync(); break;
                case "updateMod": _ = UpdateModAsync(m.Value<string>("file") ?? "", m.Value<string>("hash") ?? ""); break;
                case "openModsFolder": OpenPath(Path.Combine(McBase(), "mods")); break;

                case "loadSkins": PushSkins(); break;
                case "uploadSkin": UploadSkin(); break;
                case "assignSkin": AssignSkin(m.Value<string>("id") ?? "", m.Value<string>("file") ?? ""); break;

                // ── modpacks / version install ──
                case "loadInstalledPacks": PushInstalledPacks(); break;
                case "packVersions": _ = PushPackVersionsAsync(m.Value<string>("slug") ?? "", m.Value<string>("title") ?? "", m.Value<string>("icon") ?? ""); break;
                case "installPack": _ = InstallPackAsync(m.Value<string>("slug") ?? "", m.Value<string>("title") ?? "", m.Value<string>("icon") ?? "", m.Value<string>("versionId") ?? ""); break;
                case "launchPack": LaunchPack(m.Value<string>("slug") ?? ""); break;
                case "removePack": RemovePack(m.Value<string>("slug") ?? ""); break;
                case "openPackFolder": OpenPackFolder(m.Value<string>("slug") ?? ""); break;
                case "openPackMods": OpenPackMods(m.Value<string>("slug") ?? ""); break;
                case "reinstallPack": _ = ReinstallPack(m.Value<string>("slug") ?? ""); break;
                case "installVersion": _ = InstallVersionAsync(m.Value<string>("name") ?? ""); break;
            }
        }

        // ── initial data push (once page + data are both ready) ──
        private void MarkDataReady() { _dataReady = true; TryPushInit(); }
        private void TryPushInit() { if (_webReady && _dataReady) PushInit(); }

        private void PushInit()
        {
            string ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var s = new Properties.Settings();
            int ramGb = s.GetMemoryRAM() >= 64 ? s.GetMemoryRAM() / 1024 : s.GetMemoryRAM();

            Post(new
            {
                type = "init",
                verTag = $"v{ver} · glass",
                footVer = $"// Blockify {ver}",
                versionNames = GetVersionNames(),
                selectedVersion = MinecraftVerisonComboBox.SelectedItem?.ToString() ?? "",
                versions = BuildVersionRows(),
                account = CurrentAccountObj(),
                accounts = AccountObjs(),
                settings = new
                {
                    ram = Math.Clamp(ramGb, 2, 16),
                    favServer = s.GetFavoriteServer() ?? "",
                    mcDir = s.GetMinecraftDir() ?? "",
                    java = s.GetJavaPath() ?? "",
                    swJvm = s.GetJvmAikar(),
                    swDiscord = s.GetDiscordRpc(),
                    swSnap = s.GetShowSnapshots(),
                    swClose = s.GetHideLauncher() == 0   // true = close launcher after game starts
                },
                javaList = new[] { s.GetJavaPath() ?? "javaw.exe" }
            });

            _ = PushNewsAsync();
            _ = PushPacksAsync();
            _ = PushMojangVersionsAsync();
            PushInstalledPacks();
        }

        // ── versions ──
        private List<object> BuildVersionRows(IEnumerable<BlockifyLib.Launcher.Version.Metadata.VersionMetadata> src)
        {
            var rows = new List<object>();
            foreach (var v in src)
            {
                string kind = v.IsLocalVersion ? "release" : (v.Type ?? "release");
                rows.Add(new
                {
                    name = v.Name,
                    info = v.IsLocalVersion ? "Установлена локально" : (v.Type ?? "release"),
                    badge = v.IsLocalVersion ? "установлена" : "доступна",
                    kind
                });
            }
            return rows;
        }

        private List<object> BuildVersionRows()
        {
            try
            {
                var local = new BlockifyLib.Launcher.Version.Load.LocalVersionLoader(setting.launcher.MinecraftPath)
                    .GetVersionMetadatas();
                return BuildVersionRows(local);
            }
            catch { return new List<object>(); }
        }

        private async Task PushMojangVersionsAsync()
        {
            try
            {
                var all = await setting.launcher.GetAllVersionsAsync();
                Post(new
                {
                    type = "versions",
                    items = BuildVersionRows(all),
                    versionNames = GetVersionNames(),
                    selectedVersion = MinecraftVerisonComboBox.SelectedItem?.ToString() ?? ""
                });
            }
            catch { /* offline — local rows already shown */ }
        }

        // ── accounts ──
        // Reuse the window's own Account instance (the launch path reads it too).
        private SessionStruct[] AllAccounts()
        {
            try { return (account ??= new Account()).GetAllUserArray() ?? Array.Empty<SessionStruct>(); }
            catch { return Array.Empty<SessionStruct>(); }
        }

        private object CurrentAccountObj()
        {
            var arr = AllAccounts();
            string lastId = setting.GetLastUser() ?? "";
            var cur = arr.FirstOrDefault(a => a != null && a.Id == lastId) ?? arr.FirstOrDefault(a => a != null);
            if (cur?.Username == null)
                return new { name = ResxLocalizationProvider.Instance["none_account"], type = "off" };
            return new { name = cur.Username, type = cur.UserType == "msa" ? "lic" : "off" };
        }

        private List<object> AccountObjs()
        {
            var arr = AllAccounts();
            string lastId = setting.GetLastUser() ?? "";
            var list = new List<object>();
            foreach (var a in arr)
            {
                if (a == null) continue;
                list.Add(new
                {
                    id = a.Id,
                    name = a.Username,
                    sub = a.UserType == "msa" ? "Microsoft" : "оффлайн-профиль · скин локальный",
                    type = a.UserType == "msa" ? "lic" : "off",
                    active = a.Id == lastId
                });
            }
            return list;
        }

        private void PushAccounts()
        {
            if (Web?.CoreWebView2 == null) return;
            Post(new { type = "accounts", items = AccountObjs(), account = CurrentAccountObj() });
        }

        private void AddOfflineAccount(string nick)
        {
            nick = (nick ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nick)) return;
            (account ??= new Account()).CreateUser(nick);
            SyncAccountsToCombo();
        }

        private async Task MicrosoftLoginAsync()
        {
            try
            {
                var lh = BlockifyLib.Launcher.Microsoft.JELoginHandlerBuilder.BuildDefault();
                var session = await lh.AuthenticateInteractively();
                if (!session.CheckIsValid()) return;
                session.Id = "msa_" + (session.Xuid ?? session.UUID);
                (account ??= new Account()).CreateUser(session);
                SyncAccountsToCombo();
            }
            catch (Exception ex) { HandleException(ex); }
        }

        private void SetActiveAccount(string id)
        {
            new Properties.Settings().SetLastUser(id);
            SyncAccountsToCombo();
        }

        private void RemoveAccount(string id)
        {
            (account ??= new Account()).RemoveUser(id);
            SyncAccountsToCombo();
        }

        // Rebuild the hidden combo the launch path reads, then refresh the web UI.
        private void SyncAccountsToCombo()
        {
            var arr = AllAccounts();
            string lastId = setting.GetLastUser() ?? "";
            MinecraftAccountComboBox.Items.Clear();
            int sel = 0;
            foreach (var a in arr)
            {
                if (a == null) continue;
                MinecraftAccountComboBox.Items.Add(a.Username);
                if (a.Id == lastId) sel = MinecraftAccountComboBox.Items.Count - 1;
            }
            if (MinecraftAccountComboBox.Items.Count > 0)
                MinecraftAccountComboBox.SelectedIndex = sel;
            PushAccounts();
        }

        // ── settings ──
        private void ApplySetting(string key, JToken? val)
        {
            if (val == null) return;
            var s = new Properties.Settings();
            switch (key)
            {
                case "ram": s.SetMemoryRAM(Math.Max(1, val.Value<int>()) * 1024); break;
                case "favServer": s.SetFavoriteServer(val.Value<string>() ?? ""); break;
                case "mcDir": s.SetMinecraftDir(val.Value<string>() ?? ""); break;
                case "swJvm": s.SetJvmAikar(val.Value<bool>()); break;
                case "swDiscord": s.SetDiscordRpc(val.Value<bool>()); break;
                case "swSnap": s.SetShowSnapshots(val.Value<bool>()); break;
                case "swClose": s.SetHideLauncher(val.Value<bool>() ? 0 : 1); break;
            }
        }

        // ── content: news + packs ──
        private async Task PushNewsAsync()
        {
            List<NewsItem> news;
            try { news = await NewsService.GetAsync(5); }
            catch { news = NewsService.GetCached(5); }
            if (news == null || news.Count == 0) news = NewsService.GetCached(5);

            var items = news.Select(n => new
            {
                title = n.Title,
                date = n.Date == default ? n.Subtitle : n.Date.ToString("d MMM yyyy"),
                link = n.Link ?? "",
                image = n.ImageUrl ?? ""
            }).ToList();
            Post(new { type = "news", items });
        }

        private async Task PushPacksAsync()
        {
            List<ModpackInfo> packs;
            try
            {
                packs = await ModrinthService.SearchAsync(
                    loader: string.IsNullOrEmpty(_packLoader) ? null : _packLoader,
                    query: string.IsNullOrWhiteSpace(_packQuery) ? null : _packQuery,
                    gameVersion: string.IsNullOrWhiteSpace(_packMc) ? null : _packMc.Trim(),
                    sortIndex: string.IsNullOrEmpty(_packSort) ? null : _packSort,
                    limit: 30);
            }
            catch { packs = new List<ModpackInfo>(); }

            var items = packs.Select(p => new
            {
                slug = p.Slug,
                title = p.Title,
                description = p.Description,
                loader = p.Loader,
                gameVersion = p.GameVersion,
                downloads = p.DownloadsText,
                banner = p.BannerUrl ?? "",
                icon = p.IconUrl ?? ""
            }).ToList();
            Post(new { type = "packs", items });
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
