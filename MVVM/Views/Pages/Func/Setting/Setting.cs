using CmlLib.Core;
using static BlockifyLauncher.MVVM.Views.Pages.SettingPage;

namespace BlockifyLauncher.MVVM.Views.Pages.Func.Setting
{
    /// <summary>
    /// Project settings.
    /// </summary>
    public class Setting
    {
        public CmlLib.Core.Version.MVersionCollection CollectionVerion;
        public CmlLib.Core.Version.MVersion Version;
        public CmlLib.Core.Auth.MSession Session;

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

            Session = CmlLib.Core.Auth.MSession
                .GetOfflineSession(Properties.Settings.Default.UserName);
            RamMB = Properties.Settings.Default.UseRam;

            screadFormat.ScreenWidth = Properties.Settings.Default.ScreenWidth;
            screadFormat.ScreenHeight = Properties.Settings.Default.ScreenHeight;
            screadFormat.FullScrean = Properties.Settings.Default.FullScreanGame;

            minecraftPath = new MinecraftPath();
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
