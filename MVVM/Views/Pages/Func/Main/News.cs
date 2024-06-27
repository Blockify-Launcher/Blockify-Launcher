using Newtonsoft.Json;
using System.Drawing;
using System.IO;

namespace BlockifyLauncher.MVVM.Views.Pages.Func.Main
{
    public class News
    {
        internal string fileName { get; set; } = @"InformationNews.json";
        internal string filePath { get; set; } = @"C:\Users\Palma\source\repos\BlockifyLauncher\Blockify-Launcher\Image\News\";

        public NewsStr[] __list;

        public News()
        {
            InitializationNews();
        }

        private void InitializationNews()
        {
            List<NewsStr> _list = new List<NewsStr>();
            testIntialization(); // test news
        }

        private void testIntialization()
        {
            if (File.Exists(filePath + fileName))
            {
                string jsonFile = File.ReadAllText(filePath + fileName);
                __list = JsonConvert.
                            DeserializeObject<NewsStr[]>(jsonFile)!;

                for (int i = 0; i < __list.Length; i++)
                    __list[i].Image = Image.FromFile(filePath + __list[i].NameImage + ".jpg");
            }
        }

        public NewsStr get(int id) => __list[id];
        public NewsStr[] getAllNews() => __list;
    }
}
