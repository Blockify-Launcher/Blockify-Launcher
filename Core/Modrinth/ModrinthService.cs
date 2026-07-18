using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Text;

namespace BlockifyLauncher.Core.Modrinth
{
    /// <summary>Update status of a single installed mod jar (matched on Modrinth by file hash).</summary>
    public class ModUpdateInfo
    {
        public string Hash = "";
        public string? Title;
        public string? CurrentVersion;
        public string? LatestVersion;
        public bool HasUpdate;
        public string? DownloadUrl;
        public string? DownloadFileName;
    }

    public class ModpackInfo
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Slug { get; set; } = "";
        public string? IconUrl { get; set; }
        public string? FeaturedImageUrl { get; set; }
        public long Downloads { get; set; }
        public string Loader { get; set; } = "";
        public string GameVersion { get; set; } = "";

        /// <summary>Banner for the card: featured gallery shot, icon as fallback.</summary>
        public string? BannerUrl => FeaturedImageUrl ?? IconUrl;

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
            string? loader = null, string? query = null,
            string? category = null, string? gameVersion = null,
            string? sortIndex = null, int limit = 20)
        {
            var facets = new List<string> { "[\"project_type:modpack\"]" };
            if (!string.IsNullOrEmpty(loader)) facets.Add($"[\"categories:{loader}\"]");
            if (!string.IsNullOrEmpty(category)) facets.Add($"[\"categories:{category}\"]");
            if (!string.IsNullOrEmpty(gameVersion)) facets.Add($"[\"versions:{gameVersion}\"]");

            string index = sortIndex
                ?? (string.IsNullOrEmpty(query) ? "downloads" : "relevance");

            string url = "https://api.modrinth.com/v2/search"
                + "?limit=" + limit
                + "&index=" + index
                + "&facets=" + Uri.EscapeDataString("[" + string.Join(",", facets) + "]");
            if (!string.IsNullOrEmpty(query))
                url += "&query=" + Uri.EscapeDataString(query);

            var json = JObject.Parse(await Http.GetStringAsync(url));
            var result = new List<ModpackInfo>();

            foreach (var hit in (JArray?)json["hits"] ?? new JArray())
            {
                var categories = ((JArray?)hit["categories"])?.Select(c => (string?)c) ?? Enumerable.Empty<string?>();
                string loaderName = KnownLoaders.FirstOrDefault(l => categories.Contains(l)) ?? "";

                // newest supported game version (the "versions" array is ordered oldest→newest)
                string packGameVersion = ((JArray?)hit["versions"])?.LastOrDefault()?.Value<string>() ?? "";

                string? featured = hit.Value<string>("featured_gallery")
                    ?? ((JArray?)hit["gallery"])?.FirstOrDefault()?.Value<string>();

                result.Add(new ModpackInfo
                {
                    Title = hit.Value<string>("title") ?? "",
                    Description = hit.Value<string>("description") ?? "",
                    Slug = hit.Value<string>("slug") ?? "",
                    IconUrl = hit.Value<string>("icon_url"),
                    FeaturedImageUrl = featured,
                    Downloads = hit.Value<long?>("downloads") ?? 0,
                    Loader = loaderName.Length > 0
                        ? char.ToUpper(loaderName[0]) + loaderName[1..]
                        : "",
                    GameVersion = packGameVersion
                });
            }
            return result;
        }

        // ── mod update checks (installed jars matched by sha1) ──

        /// <summary>
        /// Look installed mod hashes up on Modrinth and report which have a newer version.
        /// Two calls: /version_files (current) and /version_files/update (latest for the
        /// given loaders/game versions). Project titles are resolved in a batch.
        /// </summary>
        public static async Task<Dictionary<string, ModUpdateInfo>> CheckModUpdatesAsync(
            List<string> hashes, List<string> loaders, List<string> gameVersions)
        {
            var result = new Dictionary<string, ModUpdateInfo>();
            if (hashes == null || hashes.Count == 0) return result;

            // 1. current version for each hash
            JObject current;
            try
            {
                current = await PostJsonAsync("https://api.modrinth.com/v2/version_files",
                    new JObject { ["hashes"] = new JArray(hashes), ["algorithm"] = "sha1" });
            }
            catch { current = new JObject(); }

            // 2. project titles (batch)
            var projIds = new HashSet<string>();
            foreach (var p in current.Properties())
                if (p.Value.Value<string>("project_id") is string pid && pid.Length > 0) projIds.Add(pid);

            var titles = new Dictionary<string, string>();
            if (projIds.Count > 0)
            {
                try
                {
                    string idsParam = "[" + string.Join(",", projIds.Select(i => "\"" + i + "\"")) + "]";
                    var projJson = JArray.Parse(await Http.GetStringAsync(
                        "https://api.modrinth.com/v2/projects?ids=" + Uri.EscapeDataString(idsParam)));
                    foreach (var pr in projJson)
                        titles[pr.Value<string>("id") ?? ""] = pr.Value<string>("title") ?? "";
                }
                catch { }
            }

            // 3. latest matching version for each hash
            JObject latest;
            try
            {
                latest = await PostJsonAsync("https://api.modrinth.com/v2/version_files/update",
                    new JObject
                    {
                        ["hashes"] = new JArray(hashes),
                        ["algorithm"] = "sha1",
                        ["loaders"] = new JArray(loaders ?? new List<string>()),
                        ["game_versions"] = new JArray(gameVersions ?? new List<string>())
                    });
            }
            catch { latest = new JObject(); }

            foreach (var hash in hashes)
            {
                var info = new ModUpdateInfo { Hash = hash };
                var cur = current[hash];
                if (cur != null)
                {
                    info.CurrentVersion = cur.Value<string>("version_number");
                    string pid = cur.Value<string>("project_id") ?? "";
                    if (titles.TryGetValue(pid, out var t) && t.Length > 0) info.Title = t;
                }
                var lat = latest[hash];
                if (lat != null && cur != null)
                {
                    string curId = cur.Value<string>("id") ?? "";
                    string latId = lat.Value<string>("id") ?? "";
                    info.LatestVersion = lat.Value<string>("version_number");
                    if (latId.Length > 0 && latId != curId)
                    {
                        info.HasUpdate = true;
                        var files = (JArray?)lat["files"];
                        var primary = files?.FirstOrDefault(f => f.Value<bool?>("primary") == true) ?? files?.FirstOrDefault();
                        info.DownloadUrl = primary?.Value<string>("url");
                        info.DownloadFileName = primary?.Value<string>("filename");
                    }
                }
                result[hash] = info;
            }
            return result;
        }

        public static Task<byte[]> DownloadAsync(string url) => Http.GetByteArrayAsync(url);

        private static async Task<JObject> PostJsonAsync(string url, JObject body)
        {
            using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync(url, content);
            resp.EnsureSuccessStatusCode();
            return JObject.Parse(await resp.Content.ReadAsStringAsync());
        }
    }
}
