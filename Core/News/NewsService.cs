using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Net.Http;

namespace BlockifyLauncher.Core.News
{
    public class NewsItem
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public DateTime Date { get; set; }
        public string? Link { get; set; }
        public string? ImageUrl { get; set; }
        public string? LocalImagePath { get; set; }
        public bool IsOwn { get; set; }
    }

    /// <summary>
    /// Live launcher news: official Mojang launcher-content feed merged with
    /// Blockify's own feed (a JSON file in a GitHub repo — editable without
    /// shipping a new launcher). JSON and images are cached locally so the
    /// news survive offline starts.
    /// </summary>
    public static class NewsService
    {
        private const string MojangFeed = "https://launchercontent.mojang.com/v2/news.json";
        private const string MojangBase = "https://launchercontent.mojang.com";
        private const string BlockifyFeed = "https://raw.githubusercontent.com/Blockify-Launcher/Blockify-News/main/news.json";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlockifyLauncher/0.2");
            return http;
        }

        private static string CacheDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlockifyLauncher", "News");

        private static string FeedCachePath => Path.Combine(CacheDir, "feed.json");

        public static async Task<List<NewsItem>> GetAsync(int max = 6)
        {
            Directory.CreateDirectory(CacheDir);
            var items = new List<NewsItem>();

            try { items.AddRange(await FetchMojangAsync()); }
            catch { /* offline or feed changed — fall back below */ }

            try { items.AddRange(await FetchBlockifyAsync()); }
            catch { /* own feed is optional */ }

            if (items.Count == 0)
            {
                try
                {
                    if (File.Exists(FeedCachePath))
                        items = JsonConvert.DeserializeObject<List<NewsItem>>(
                            File.ReadAllText(FeedCachePath)) ?? new List<NewsItem>();
                }
                catch { /* no cache — empty news, UI falls back to local file */ }
                return items;
            }

            items = items.OrderByDescending(i => i.Date).Take(max).ToList();

            foreach (var item in items)
                item.LocalImagePath = await CacheImageAsync(item.ImageUrl);

            try { File.WriteAllText(FeedCachePath, JsonConvert.SerializeObject(items, Formatting.Indented)); }
            catch { /* cache write is best-effort */ }

            return items;
        }

        private static async Task<List<NewsItem>> FetchMojangAsync()
        {
            var result = new List<NewsItem>();
            var json = JObject.Parse(await Http.GetStringAsync(MojangFeed));

            foreach (var e in (JArray?)json["entries"] ?? new JArray())
            {
                string category = e.Value<string>("category") ?? "";
                if (!category.Contains("Java", StringComparison.OrdinalIgnoreCase))
                    continue;

                string? imageUrl = e["newsPageImage"]?.Value<string>("url")
                                ?? e["playPageImage"]?.Value<string>("url");
                if (imageUrl != null && imageUrl.StartsWith('/'))
                    imageUrl = MojangBase + imageUrl;

                DateTime.TryParse(e.Value<string>("date"), out var date);

                result.Add(new NewsItem
                {
                    Title = e.Value<string>("title") ?? "",
                    Subtitle = "MOJANG · " + date.ToString("d MMM yyyy"),
                    Date = date,
                    Link = e.Value<string>("readMoreLink"),
                    ImageUrl = imageUrl,
                    IsOwn = false
                });
            }
            return result;
        }

        private static async Task<List<NewsItem>> FetchBlockifyAsync()
        {
            var result = new List<NewsItem>();
            var arr = JArray.Parse(await Http.GetStringAsync(BlockifyFeed));

            foreach (var e in arr)
            {
                DateTime.TryParse(e.Value<string>("date"), out var date);
                result.Add(new NewsItem
                {
                    Title = e.Value<string>("title") ?? "",
                    Subtitle = "BLOCKIFY · " + date.ToString("d MMM yyyy"),
                    Date = date,
                    Link = e.Value<string>("link"),
                    ImageUrl = e.Value<string>("image"),
                    IsOwn = true
                });
            }
            return result;
        }

        private static async Task<string?> CacheImageAsync(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            try
            {
                string ext = Path.GetExtension(new Uri(url).AbsolutePath);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                string file = Path.Combine(CacheDir,
                    (uint)url.GetHashCode() + ext);

                if (!File.Exists(file))
                {
                    byte[] bytes = await Http.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(file, bytes);
                }
                return file;
            }
            catch { return null; }
        }
    }
}
