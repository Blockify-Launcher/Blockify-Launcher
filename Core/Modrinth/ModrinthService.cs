using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace BlockifyLauncher.Core.Modrinth
{
    public class ModpackInfo
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Slug { get; set; } = "";
        public string? IconUrl { get; set; }
        public long Downloads { get; set; }
        public string Loader { get; set; } = "";
        public string GameVersion { get; set; } = "";

        public string PageUrl => "https://modrinth.com/modpack/" + Slug;

        public string DownloadsText => Downloads switch
        {
            >= 1_000_000 => (Downloads / 1_000_000.0).ToString("0.#") + " M",
            >= 1_000 => (Downloads / 1_000.0).ToString("0.#") + " K",
            _ => Downloads.ToString()
        };
    }

    /// <summary>
    /// Modpack catalog backed by the open Modrinth API (no key required).
    /// Search only for now; .mrpack installation is the next phase.
    /// </summary>
    public static class ModrinthService
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            // Modrinth asks clients to identify themselves.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BlockifyLauncher/0.2 (github.com/Blockify-Launcher)");
            return http;
        }

        private static readonly string[] KnownLoaders = { "fabric", "forge", "neoforge", "quilt" };

        public static async Task<List<ModpackInfo>> SearchAsync(
            string? loader = null, string? query = null, int limit = 20)
        {
            string facets = loader == null
                ? "[[\"project_type:modpack\"]]"
                : $"[[\"project_type:modpack\"],[\"categories:{loader}\"]]";

            string url = "https://api.modrinth.com/v2/search"
                + "?limit=" + limit
                + "&index=" + (string.IsNullOrEmpty(query) ? "downloads" : "relevance")
                + "&facets=" + Uri.EscapeDataString(facets);
            if (!string.IsNullOrEmpty(query))
                url += "&query=" + Uri.EscapeDataString(query);

            var json = JObject.Parse(await Http.GetStringAsync(url));
            var result = new List<ModpackInfo>();

            foreach (var hit in (JArray?)json["hits"] ?? new JArray())
            {
                var categories = ((JArray?)hit["categories"])?.Select(c => (string?)c) ?? Enumerable.Empty<string?>();
                string loaderName = KnownLoaders.FirstOrDefault(l => categories.Contains(l)) ?? "";

                // newest supported game version (the "versions" array is ordered oldest→newest)
                string gameVersion = ((JArray?)hit["versions"])?.LastOrDefault()?.Value<string>() ?? "";

                result.Add(new ModpackInfo
                {
                    Title = hit.Value<string>("title") ?? "",
                    Description = hit.Value<string>("description") ?? "",
                    Slug = hit.Value<string>("slug") ?? "",
                    IconUrl = hit.Value<string>("icon_url"),
                    Downloads = hit.Value<long?>("downloads") ?? 0,
                    Loader = loaderName.Length > 0
                        ? char.ToUpper(loaderName[0]) + loaderName[1..]
                        : "",
                    GameVersion = gameVersion
                });
            }
            return result;
        }
    }
}
