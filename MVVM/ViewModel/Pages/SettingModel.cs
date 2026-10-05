using System.Configuration;

namespace BlockifyLauncher.MVVM.ViewModel.Pages
{
    public class SettingModel
    {
        private readonly Properties.Settings settings
            = new Properties.Settings();

        public int GetMemoryRAM() => settings.GetMemoryRAM();
        public void SetMemoryRAM(int value) => settings.SetMemoryRAM(value);
    }

}
