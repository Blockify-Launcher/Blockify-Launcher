using Newtonsoft.Json;
using System.Drawing;

namespace BlockifyLauncher.MVVM.Views.Pages.Func.Main
{
    public struct NewsStr
    {
        [JsonProperty("Title")]
        public string Name;
        [JsonProperty("Description")]
        public string Description;
        [JsonProperty("NameImage")]
        public string NameImage;

        public Image Image;
    }
}
