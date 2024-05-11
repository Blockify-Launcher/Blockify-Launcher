using BlockifyLib.Launcher.Version;
using BlockifyLib.Launcher.Minecraft.Auth;
using BlockifyLib.Launcher.Minecraft;
using static BlockifyLauncher.MVVM.Views.Pages.SettingPage;
using BlockifyLib.Launcher.src;

namespace BlockifyLauncher.MVVM.Views.Pages.Func.Setting
{
    /// <summary>
    /// Minecraft settings.
    /// </summary>
    public class Setting
    {
        public Account accountSession;
        public BlockifyLib.BlockifyLibLauncher launcher;
        public LaunchOption optionLaunch;
        public VersionCollection CollectionVerion;
        public BlockifyLib.Launcher.Version.Version Version;

        public MinecraftPath minecraftPath;
        public string javaPath;

        public Display screadFormat;

        private int _RamMb;
        public int RamMB
        {
            get { return _RamMb; }
            set { _RamMb = 1024 < value ? value : 1024; }
        }
        public Setting()
        {
            javaPath = Properties.Settings.Default.JavaVersion;
            RamMB = Properties.Settings.Default.UseRam;

            screadFormat.ScreenWidth = Properties.Settings.Default.ScreenWidth;
            screadFormat.ScreenHeight = Properties.Settings.Default.ScreenHeight;
            screadFormat.FullScrean = Properties.Settings.Default.FullScreanGame;
            minecraftPath = new MinecraftPath();
            optionLaunch = new LaunchOption();
        }

        public void SetDisplay(int W, int H)
        {
            screadFormat.ScreenWidth = W;
            screadFormat.ScreenHeight = H;

            Properties.Settings.Default.ScreenWidth = W;
            Properties.Settings.Default.ScreenHeight = H;
        }

        public void SetFullScrean(bool fullScr)
        {
            screadFormat.FullScrean = fullScr;
            Properties.Settings.Default.FullScreanGame = fullScr;
        }

        public void SetMemoryRAM(int RAM)
        {
            RamMB = RAM;
            Properties.Settings.Default.UseRam = RAM;
        }
    }

}
