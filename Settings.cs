using BlockifyLauncher.MVVM.Views.Pages.Func.Setting;
using BlockifyLib;
using BlockifyLib.Launcher.Minecraft;
using BlockifyLib.Launcher.src;
using BlockifyLib.Launcher.Version;
using Microsoft.Windows.Themes;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Media.Media3D;

namespace BlockifyLauncher.Properties {
    
    /// <summary>
    /// Display struct;
    /// </summary>
    public struct Display { 
        public int w, h;
    }
    
    /// <summary>
    /// Launcher Setting.
    /// Values live in %APPDATA%\BlockifyLauncher\settings.json (JsonSettingsProvider) — not in the per-version,
    /// per-exe-path user.config, so updates and moving the launcher keep the settings.
    /// Every `new Properties.Settings()` reads and writes the one shared instance (Settings.Default):
    /// the ~30 independent instances used to cache their own copies, so «Сделать активным» or a new RAM value
    /// written through one instance was never seen by the launch code holding another one.
    /// </summary>
    [global::System.Configuration.SettingsProvider(typeof(BlockifyLauncher.Core.JsonSettingsProvider))]
    internal sealed partial class Settings {
        public LaunchOption optionLaunch;
        public VersionCollection collectionVersion;
        public BlockifyLib.Launcher.Version.Version version;
        public string javaPath;

        // created on first use, not in the constructor: a bad «Папка Minecraft» or a damaged account.json
        // used to throw from `new Settings()` and the launcher died on every start
        private Account? _accountSession;
        private MinecraftPath? _minecraftPath;
        private BlockifyLibLauncher? _launcher;

        public Account accountSession
        {
            get => _accountSession ??= new Account();
            set => _accountSession = value;
        }

        public MinecraftPath minecraftPath
        {
            get => _minecraftPath ??= CreateMinecraftPath();
            set => _minecraftPath = value;
        }

        public BlockifyLibLauncher launcher
        {
            get => _launcher ??= new BlockifyLibLauncher(minecraftPath);
            set => _launcher = value;
        }

        private static readonly object _saveLock = new();

        /// <summary>
        /// Set at startup when the saved Minecraft folder is unusable (unplugged drive, bad path): this session
        /// works with the default .minecraft, the user's choice stays saved for the next start.
        /// </summary>
        internal static bool MinecraftDirFallback { get; set; }

        public Settings()
        {
            this.PropertyChanged += PropertyChangedEventHandler;
        }

        public void SettingsInitialize()
        {
            this._minecraftPath = CreateMinecraftPath();
            this._launcher = new BlockifyLibLauncher(this._minecraftPath);
            this._accountSession = new Account();
        }

        private MinecraftPath CreateMinecraftPath()
        {
            string dir = GetMinecraftDir();
            if (!string.IsNullOrEmpty(dir))
            {
                try { return new MinecraftPath(dir); }
                catch (Exception ex)
                {
                    BlockifyLauncher.Core.AppLog.Warn($"Minecraft folder '{dir}' is unusable, using the default one: {ex.Message}");
                    MinecraftDirFallback = true;
                }
            }
            return new MinecraftPath();
        }

        // ── one shared store ──
        // Designer properties go through this indexer; anything but the shared instance forwards to it.
        // While Default itself is being constructed defaultInstance is still null — it then uses its own storage.
        public override object this[string propertyName]
        {
            get
            {
                var shared = defaultInstance;
                return shared == null || ReferenceEquals(shared, this) ? base[propertyName] : shared[propertyName];
            }
            set
            {
                var shared = defaultInstance;
                if (shared == null || ReferenceEquals(shared, this)) base[propertyName] = value;
                else shared[propertyName] = value;
            }
        }

        public override void Save()
        {
            var shared = defaultInstance;
            if (shared != null && !ReferenceEquals(shared, this))
            {
                shared.Save();
                return;
            }
            lock (_saveLock) base.Save();
        }

        // every Set* is saved right away (the shared instance raises PropertyChanged)
        private void PropertyChangedEventHandler(object sender, PropertyChangedEventArgs e) =>
            this.Save();

        /* Останній користувач в лаунчері */
        public void SetLastUser(string id) => this.UserLastID = id;
        public string GetLastUser() => this.UserLastID;

        /* Параметр відображення лаучнеру після відкривання гри */
        public void SetHideLauncher(int num) => this.HideLauncher = num;
        public int GetHideLauncher() => this.HideLauncher;

        /* Мова лаунчеру */
        public void SetLauguage(string num) => this.Language = num;
        public string GetLanguage() => this.Language;

        /* Хуй знает что за хуета */
        public void SetVersionDisplay(int num) => this.VersionDisplay = num;
        public int GetVersionDisplay() => this.VersionDisplay;

        /* Відкривання в повному екрані */
        public void SetFullScrean(bool val) => this.FullScreanGame = val;
        public bool GetFullScrean() => this.FullScreanGame;

        /* Виділення оперативної пам'яті */
        public void SetMemoryRAM(int RAM) => this.UseRam = RAM;
        public int  GetMemoryRAM() => this.UseRam;

        /* Вайб фону лаунчера: auto | ocean | sunset | nether | cherry | end */
        public void SetVibe(string vibe) => this.Vibe = vibe;
        public string GetVibe() => this.Vibe;

        /* Статистика запусків для quick-row на головній */
        public void RegisterLaunch(string version)
        {
            this.LastVersion = version;
            this.LastPlayed = DateTime.Now.ToString("yyyy-MM-dd");
            this.LaunchCount = this.LaunchCount + 1;
        }
        public string GetLastVersion() => this.LastVersion;
        public string GetLastPlayed() => this.LastPlayed;
        public int GetLaunchCount() => this.LaunchCount;
        public string GetFavoriteServer() => this.FavoriteServer;
        public void SetFavoriteServer(string host) => this.FavoriteServer = host;

        /* Тека Minecraft (порожньо = стандартна .minecraft) */
        // stored cleaned (no «Копировать как путь» quotes / stray spaces); while the startup check fell back to
        // the default folder this returns "" (= default .minecraft) without overwriting the user's choice
        public void SetMinecraftDir(string dir)
        {
            this.MinecraftDir = BlockifyLauncher.Core.AppPaths.NormalizeDir(dir);
            MinecraftDirFallback = false;
        }
        public string GetMinecraftDir() =>
            MinecraftDirFallback ? "" : BlockifyLauncher.Core.AppPaths.NormalizeDir(this.MinecraftDir);

        /* Шлях до Java (javaw.exe = системна) */
        public void SetJavaPath(string path) => this.JavaVersion = path;
        public string GetJavaPath() => this.JavaVersion;

        /* Фільтри списку версій */
        public void SetShowSnapshots(bool v) => this.ShowSnapshots = v;
        public bool GetShowSnapshots() => this.ShowSnapshots;
        public void SetShowBetas(bool v) => this.ShowBetas = v;
        public bool GetShowBetas() => this.ShowBetas;
        public void SetShowAlphas(bool v) => this.ShowAlphas = v;
        public bool GetShowAlphas() => this.ShowAlphas;

        /* JVM-прапори Aikar, парад мобів, Discord Rich Presence */
        public void SetJvmAikar(bool v) => this.JvmAikar = v;
        public bool GetJvmAikar() => this.JvmAikar;
        public void SetShowParade(bool v) => this.ShowParade = v;
        public bool GetShowParade() => this.ShowParade;
        public void SetDiscordRpc(bool v) => this.DiscordRpc = v;
        public bool GetDiscordRpc() => this.DiscordRpc;

        /* Розміри екрану гри */
        public void SetSettingDisplayGame(Display display)
        {
            this.ScreenWidth = display.w;
            this.ScreenHeight = display.h;
        }
        
        public Display GetSettingDisplayGame() => new Display { w = this.ScreenWidth, h = this.ScreenHeight };

        /* Розміри форми лаунчеру */
        public void SettingsSavingSizeForms(Display display)
        {
            this.WidthProgram = display.w;
            this.HeightProgram = display.h;
        }

        public void SettingsSavingSizeForms(int width, int height)
        {
            this.WidthProgram = width;
            this.HeightProgram = height;
        }
    }
}


/*
 
TODO : Задачи по настройкам

1. Добавить инициализацию переменных при открытие лаунчера.
 
 */