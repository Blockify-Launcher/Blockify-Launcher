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
        private string _packType = "modpack";   // modpack | mod | shader | resourcepack

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
            string dataFolder = BlockifyLauncher.Core.AppPaths.WebViewDataDir;   // not %TEMP%: cleaners wipe it
            // Russian UI for the built-in context menu (Вырезать / Копировать / Вставить) and form validation
            var envOptions = new CoreWebView2EnvironmentOptions { Language = "ru-RU" };
            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, envOptions);
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

            // right-click works only where it is useful: text fields and selected text get
            // cut / copy / paste / select all; everywhere else (buttons, cards, images) no menu at all
            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Web.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xFF, 0x0B, 0x0E, 0x10);

            // the window only ever shows our own UI: anything else (a dropped file/link, a stray
            // redirect) must not end up as a document that can talk to the C# bridge
            Web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (IsAppUrl(e.Uri)) return;
                e.Cancel = true;
                OpenUrl(e.Uri);
            };
            Web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; OpenUrl(e.Uri); };
            Web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            // a file dropped on the window would replace the launcher page; browser hotkeys
            // (F5, Ctrl+P, Ctrl+F, Alt+←) make no sense here and F5 used to wipe job state
            Web.AllowExternalDrop = false;
            Web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // the page's renderer can crash or hang (GPU driver, out of memory): reload it instead of
            // leaving a blank window without buttons; the page re-sends "ready" and gets a fresh init
            Web.CoreWebView2.ProcessFailed += (_, e) =>
            {
                LogDiag($"WebView2 process failed: {e.ProcessFailedKind} reason={e.Reason} exit={e.ExitCode}");
                Dispatcher.BeginInvoke(() =>
                {
                    if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
                    {
                        // the whole WebView2 runtime is gone — only a restart brings the UI back
                        new MessageBox("Интерфейс лаунчера неожиданно закрылся. Blockify перезапустится.",
                            MessageBox.TypeMessage.Error).ShowDialog();
                        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }); } catch { }
                        Application.Current.Shutdown();
                        return;
                    }
                    _webReady = false;
                    try { Web.CoreWebView2?.Reload(); } catch { }
                });
            };

            // mc.assets maps the whole .minecraft folder — serve images only (screenshots, covers,
            // world icons); account/token files and other data stay unreachable from the page
            Web.CoreWebView2.AddWebResourceRequestedFilter("https://mc.assets/*", CoreWebView2WebResourceContext.All);
            Web.CoreWebView2.WebResourceRequested += (_, e) =>
            {
                if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var u) || u.Host != "mc.assets") return;
                string ext = Path.GetExtension(Uri.UnescapeDataString(u.AbsolutePath)).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif") return;
                e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            };

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

        // only the clipboard / editing commands survive in the default menu (names are WebView2's stable ids)
        private static readonly HashSet<string> ContextMenuKeep = new(StringComparer.OrdinalIgnoreCase)
        {
            "undo", "redo", "cut", "copy", "paste", "pasteAndMatchStyle", "selectAll"
        };

        private static void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                var target = e.ContextMenuTarget;
                if (target == null || (!target.IsEditable && !target.HasSelection))
                {
                    e.Handled = true;   // no menu on buttons, cards, images, empty space
                    return;
                }
                var items = e.MenuItems;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    var it = items[i];
                    if (it.Kind == CoreWebView2ContextMenuItemKind.Separator) continue;
                    if (!ContextMenuKeep.Contains(it.Name ?? "")) items.RemoveAt(i);
                }
                // tidy separators: none at the edges, never two in a row
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    bool sep = items[i].Kind == CoreWebView2ContextMenuItemKind.Separator;
                    bool edge = i == 0 || i == items.Count - 1;
                    bool doubled = i > 0 && items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator;
                    if (sep && (edge || doubled)) items.RemoveAt(i);
                }
                if (items.Count == 0) e.Handled = true;
            }
            catch { e.Handled = true; }
        }

        // «О программе»: folder with launcher.log (created on demand so the button never does nothing)
        private void OpenLogsFolder()
        {
            try { Directory.CreateDirectory(LogDir); } catch { }
            OpenPath(LogDir);
        }

        // «Лицензии»: third-party notices shipped next to the exe
        private void OpenLicenses()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
            if (File.Exists(path)) OpenPath(path);
            else Post(new { type = "error", message = "Файл с лицензиями (THIRD-PARTY-NOTICES.txt) не найден рядом с программой. Переустанови Blockify или посмотри лицензии на GitHub." });
        }

        // ── inbound messages from JS ──
        private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!IsAppUrl(e.Source)) return;   // only our own page may drive the launcher
            JObject m;
            try { m = JObject.Parse(e.WebMessageAsJson); } catch { return; }
            string msgType = m.Value<string>("type") ?? "";
            switch (msgType)
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
                    _packType = m.Value<string>("ptype") is string pt && pt is "mod" or "shader" or "resourcepack" ? pt : "modpack";
                    _ = PushPacksAsync(m.Value<int?>("seq") ?? 0);
                    break;
                case "openPack":
                {
                    string ptOpen = m.Value<string>("ptype") ?? "modpack";
                    OpenUrl($"https://modrinth.com/{ptOpen}/" + (m.Value<string>("slug") ?? ""));
                    break;
                }
                case "installContent":
                    _ = InstallContentAsync(m.Value<string>("slug") ?? "", m.Value<string>("title") ?? "",
                        m.Value<string>("ptype") ?? "mod", m.Value<string>("packSlug") ?? "");
                    break;
                case "openLink": OpenUrl(m.Value<string>("url") ?? ""); break;
                case "openLogs": OpenLogsFolder(); break;
                case "openLicenses": OpenLicenses(); break;

                case "addOffline": AddOfflineAccount(m.Value<string>("nick") ?? ""); break;
                case "msLogin": _ = MicrosoftLoginAsync(); break;
                case "setActive": SetActiveAccount(m.Value<string>("id") ?? ""); break;
                case "removeAccount": RemoveAccount(m.Value<string>("id") ?? ""); break;

                case "setting": ApplySetting(m.Value<string>("key") ?? "", m["value"]); break;
                case "browseJava": BrowseJava(); break;

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
                case "savePackSettings": SavePackSettings(m.Value<string>("slug") ?? "", m["settings"]); break;
                case "exportPack": _ = ExportPackAsync(m.Value<string>("slug") ?? ""); break;
                case "importPack": _ = ImportPackAsync(); break;
                case "diagnosePack": _ = DiagnosePackAsync(m.Value<string>("slug") ?? "", auto: false); break;
                case "loadSnapshots": _ = PushSnapshotsAsync(m.Value<string>("slug") ?? ""); break;
                case "makeSnapshot": _ = MakeSnapshotAsync(m.Value<string>("slug") ?? "", m.Value<string>("label") ?? ""); break;
                case "restoreSnapshot": _ = RestoreSnapshotAsync(m.Value<string>("slug") ?? "", m.Value<string>("id") ?? ""); break;
                case "deleteSnapshot": _ = DeleteSnapshotAsync(m.Value<string>("slug") ?? "", m.Value<string>("id") ?? ""); break;
                case "crashFix":
                    _ = ApplyCrashFixAsync(m.Value<string>("slug") ?? "", m.Value<string>("kind") ?? "",
                        m.Value<string>("file") ?? "", m.Value<string>("modSlug") ?? "", m.Value<int?>("ram") ?? 0);
                    break;
                case "loadPackMods": _ = PushPackModsAsync(m.Value<string>("slug") ?? ""); break;
                case "togglePackMod": _ = TogglePackModAsync(m.Value<string>("slug") ?? "", m.Value<string>("file") ?? ""); break;
                case "updatePackMod": _ = UpdatePackModAsync(m.Value<string>("slug") ?? "", m.Value<string>("file") ?? "", m.Value<string>("hash") ?? ""); break;
                case "deletePackMod": _ = DeletePackModAsync(m.Value<string>("slug") ?? "", m.Value<string>("file") ?? ""); break;
                case "installVersion": _ = InstallVersionAsync(m.Value<string>("name") ?? ""); break;

                // feature modules (each lives in its own partial file and owns its message types)
                default:
                    if (TryHandleShareMessage(msgType, m)) break;
                    if (TryHandleVibeMessage(msgType, m)) break;
                    if (TryHandleShotsMessage(msgType, m)) break;
                    if (TryHandleStatsMessage(msgType, m)) break;
                    break;
            }
        }

        // ── initial data push (once page + data are both ready) ──
        private void MarkDataReady() { _dataReady = true; TryPushInit(); }
        private void TryPushInit() { if (_webReady && _dataReady) PushInit(); }

        private void PushInit()
        {
            string ver = BlockifyLauncher.Core.AppInfo.Version;
            var s = new Properties.Settings();
            int ramGb = s.GetMemoryRAM() >= 64 ? s.GetMemoryRAM() / 1024 : s.GetMemoryRAM();

            Post(new
            {
                type = "init",
                verTag = $"v{ver}",
                footVer = $"Blockify {ver}",
                version = ver,
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
                    swClose = s.GetHideLauncher() == 0,  // true = close launcher after game starts
                    lang = s.GetLanguage() ?? "ru-RU"
                },
                javaList = new[] { s.GetJavaPath() ?? "javaw.exe" }
            });

            _ = PushNewsAsync();
            _ = PushPacksAsync();
            _ = PushMojangVersionsAsync();
            PushInstalledPacks();

            // feature modules push their own initial data
            InitShareFeature();
            InitVibeFeature();
            InitShotsFeature();
            InitStatsFeature();
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
            // none = no profile yet: the page shows onboarding instead of a fake "online" account
            if (cur?.Username == null)
                return new { name = "Нет профиля", type = "none", none = true };
            return new { name = cur.Username, type = cur.UserType == "msa" ? "lic" : "off", none = false };
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
            // the nick ends up on the java command line and in the game profile: 3–16 of [A-Za-z0-9_] only
            if (!System.Text.RegularExpressions.Regex.IsMatch(nick, "^[A-Za-z0-9_]{3,16}$"))
            {
                HandleException(new ArgumentException("Ник должен быть от 3 до 16 символов: латинские буквы, цифры и «_»."));
                return;
            }
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
                MinecraftAccountComboBox.Items.Add(a.Username ?? "");
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
                case "mcDir":
                {
                    string dir = val.Value<string>() ?? "";
                    // empty = default .minecraft; anything else must be a usable, writable folder
                    if (dir.Trim().Length > 0 && !BlockifyLauncher.Core.AppPaths.TryValidateMinecraftDir(dir, out var dirErr))
                    {
                        Post(new { type = "error", message = "Папка Minecraft недоступна: " + dirErr });
                        break;
                    }
                    s.SetMinecraftDir(dir);
                    break;
                }
                case "swJvm": s.SetJvmAikar(val.Value<bool>()); break;
                case "swDiscord": s.SetDiscordRpc(val.Value<bool>()); break;
                case "swSnap": s.SetShowSnapshots(val.Value<bool>()); break;
                case "swClose": s.SetHideLauncher(val.Value<bool>() ? 0 : 1); break;
                case "java":
                {
                    string p = (val.Value<string>() ?? "").Trim();
                    s.SetJavaPath(p.Length == 0 ? "javaw.exe" : p);   // empty = auto (launcher-managed JRE)
                    break;
                }
                case "lang":
                {
                    string lang = val.Value<string>() ?? "";
                    if (lang.Length > 0)
                    {
                        s.SetLauguage(lang);
                        try { ResxLocalizationProvider.Instance.ChangeLanguage(lang); } catch { }
                    }
                    break;
                }
            }
        }

        // pick javaw.exe through the system dialog; result goes back to the settings field
        private void BrowseJava()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выбери javaw.exe",
                Filter = "Java|javaw.exe;java.exe|Все файлы|*.*",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true) return;
            new Properties.Settings().SetJavaPath(dlg.FileName);
            Post(new { type = "javaPath", path = dlg.FileName });
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

        // seq: echo of the page's request number, so a slow old answer can't overwrite a newer search
        private async Task PushPacksAsync(int seq = 0)
        {
            List<ModpackInfo> packs;
            string ptype = _packType;
            try
            {
                packs = await ModrinthService.SearchAsync(
                    loader: string.IsNullOrEmpty(_packLoader) ? null : _packLoader,
                    query: string.IsNullOrWhiteSpace(_packQuery) ? null : _packQuery,
                    gameVersion: string.IsNullOrWhiteSpace(_packMc) ? null : _packMc.Trim(),
                    sortIndex: string.IsNullOrEmpty(_packSort) ? null : _packSort,
                    limit: 30, projectType: ptype);
            }
            catch (Exception ex)
            {
                // a failed request is not "nothing found": the page shows «нет соединения» + «Повторить»
                LogDiag("Modrinth search failed: " + ex.Message);
                Post(new { type = "packs", items = Array.Empty<object>(), error = "offline", seq });
                return;
            }

            var items = (packs ?? new List<ModpackInfo>()).Select(p => new
            {
                ptype,
                slug = p.Slug,
                title = p.Title,
                description = p.Description,
                loader = p.Loader,
                gameVersion = p.GameVersion,
                downloads = p.DownloadsText,
                banner = p.BannerUrl ?? "",
                icon = p.IconUrl ?? ""
            }).ToList();
            Post(new { type = "packs", items, seq });
        }

        private static bool IsAppUrl(string? url)
            => url != null && url.StartsWith("https://app.blockify/", StringComparison.OrdinalIgnoreCase);

        // web links only: a shell-executed string could otherwise be a local exe, file:// or a custom protocol
        private static void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)) return;
            try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }
    }
}
