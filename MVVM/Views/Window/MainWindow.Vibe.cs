using Newtonsoft.Json.Linq;

namespace BlockifyLauncher
{
    // Feature module: living background ("vibe"). Owned by the vibe feature.
    public partial class MainWindow
    {
        private void InitVibeFeature() { }

        private bool TryHandleVibeMessage(string type, JObject m) => false;
    }
}
