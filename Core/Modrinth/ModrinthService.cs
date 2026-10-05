using BlockifyLauncher.Core.Net;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net;
using System.Net.Http;

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

        public string? ProjectId;
        public string? LatestVersionType;   // release / beta / alpha
        public JToken? Latest;              // full version object of the update (files + dependencies)
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
    /// Client for the open Modrinth API (no key required): catalog search, version lookup
    /// (release-first), hash lookups and update checks. HTTP goes through <see cref="BlockifyHttp"/>.
    /// </summary>
    public static class ModrinthService
    {
        public const string Api = "https://api.modrinth.com/v2";

        private static readonly string[] KnownLoaders = { "fabric", "forge", "neoforge", "quilt" };

        public static async Task<List<ModpackInfo>> SearchAsync(
            string? loader = null, string? query = null,
            string? category = null, string? gameVersion = null,
            string? sortIndex = null, int limit = 20, string projectType = "modpack")
        {
            var facets = new List<string> { $"[\"project_type:{projectType}\"]" };
            // loader facets only make sense for mods/modpacks (shaders use iris/optifine, packs use none)
            if (!string.IsNullOrEmpty(loader) && projectType is "modpack" or "mod") facets.Add($"[\"categories:{loader}\"]");
            if (!string.IsNullOrEmpty(category)) facets.Add($"[\"categories:{category}\"]");
            if (!string.IsNullOrEmpty(gameVersion)) facets.Add($"[\"versions:{gameVersion}\"]");

            string index = sortIndex
                ?? (string.IsNullOrEmpty(query) ? "downloads" : "relevance");

            string url = Api + "/search"
                + "?limit=" + limit
                + "&index=" + index
                + "&facets=" + Uri.EscapeDataString("[" + string.Join(",", facets) + "]");
            if (!string.IsNullOrEmpty(query))
                url += "&query=" + Uri.EscapeDataString(query);

            var json = JObject.Parse(await BlockifyHttp.GetStringAsync(url));
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
        /// /version_files gives the current version of each jar; /version_files/update is then asked
        /// per group of jars sharing the same filters. Loaders / game versions come from the caller,
        /// or — when the caller passes none — from each mod's own current version (never "any loader").
        /// The release channel follows the current version: a release only updates to a release.
        /// An update counts only when it is newer by date and still a .jar.
        /// </summary>
        public static async Task<Dictionary<string, ModUpdateInfo>> CheckModUpdatesAsync(
            List<string> hashes, List<string> loaders, List<string> gameVersions)
        {
            var result = new Dictionary<string, ModUpdateInfo>();
            if (hashes == null || hashes.Count == 0) return result;
            var lds = (loaders ?? new List<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();
            var gvs = (gameVersions ?? new List<string>()).Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();

            // 1. current version for each hash — a failure here throws: "couldn't check" must not look like "not on Modrinth"
            JObject current = await LookupHashesAsync(hashes);

            // 2. project titles (batch)
            var projIds = new HashSet<string>();
            foreach (var p in current.Properties())
                if (p.Value.Value<string>("project_id") is string pid && pid.Length > 0) projIds.Add(pid);

            var titles = new Dictionary<string, string>();
            try
            {
                foreach (var (id, info) in await GetProjectsAsync(projIds))
                    titles[id] = info.Title;
            }
            catch { }

            // 3. latest matching version, one request per filter group
            var groups = new Dictionary<string, (List<string> hashes, string[] loaders, string[] games, string[] types)>();
            foreach (var hash in hashes.Distinct())
            {
                var cur = current[hash];
                if (cur == null) continue;
                string[] l = lds.Count > 0 ? lds.ToArray() : Strings(cur["loaders"]);
                string[] g = gvs.Count > 0 ? gvs.ToArray() : Strings(cur["game_versions"]);
                string[] t = ChannelFor(cur.Value<string>("version_type"));
                string key = string.Join(",", l) + "|" + string.Join(",", g) + "|" + string.Join(",", t);
                if (!groups.TryGetValue(key, out var grp)) groups[key] = grp = (new List<string>(), l, g, t);
                grp.hashes.Add(hash);
            }

            var latest = new Dictionary<string, JToken>();
            foreach (var grp in groups.Values)
            {
                var body = new JObject { ["hashes"] = new JArray(grp.hashes), ["algorithm"] = "sha1" };
                // empty filter arrays would mean "anything" to Modrinth — only send real filters
                if (grp.loaders.Length > 0) body["loaders"] = new JArray(grp.loaders);
                if (grp.games.Length > 0) body["game_versions"] = new JArray(grp.games);
                if (grp.types.Length > 0) body["version_types"] = new JArray(grp.types);
                try
                {
                    var r = await PostJsonAsync(Api + "/version_files/update", body);
                    foreach (var p in r.Properties()) latest[p.Name] = p.Value;
                }
                catch { }
            }

            foreach (var hash in hashes)
            {
                var info = new ModUpdateInfo { Hash = hash };
                var cur = current[hash];
                if (cur != null)
                {
                    info.CurrentVersion = cur.Value<string>("version_number");
                    info.ProjectId = cur.Value<string>("project_id");
                    if (info.ProjectId != null && titles.TryGetValue(info.ProjectId, out var t) && t.Length > 0) info.Title = t;
                }
                if (cur != null && latest.TryGetValue(hash, out var lat))
                {
                    string curId = cur.Value<string>("id") ?? "";
                    string latId = lat.Value<string>("id") ?? "";
                    info.LatestVersion = lat.Value<string>("version_number");
                    info.LatestVersionType = lat.Value<string>("version_type");
                    var primary = PrimaryFileToken(lat);
                    string newFile = primary?.Value<string>("filename") ?? "";
                    string curFile = PrimaryFileToken(cur)?.Value<string>("filename") ?? "";
                    // a mod jar must not "update" into a datapack zip or a source archive
                    bool sameKind = !curFile.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                    || newFile.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                    if (latId.Length > 0 && latId != curId && primary != null && sameKind
                        && PublishedAt(lat) > PublishedAt(cur))
                    {
                        info.HasUpdate = true;
                        info.DownloadUrl = primary.Value<string>("url");
                        info.DownloadFileName = newFile;
                        info.Latest = lat;
                    }
                }
                result[hash] = info;
            }
            return result;
        }

        /// <summary>Downloads a file into memory (no whole-file timeout; fails only if the transfer stalls).</summary>
        public static Task<byte[]> DownloadAsync(string url) => BlockifyHttp.DownloadBytesAsync(url);

        /// <summary>Streams a file to disk via ".part" (+ sha1 check when given).</summary>
        public static Task DownloadToFileAsync(string url, string dest, string? sha1 = null)
            => BlockifyHttp.DownloadToFileAsync(url, dest, sha1);

        /// <summary>Newest version file of a project for the given loader + game version, or null if none.</summary>
        public static Task<(string Url, string FileName)?> GetLatestVersionFileAsync(string slug, string loader, string gameVersion)
            => GetLatestVersionFileAsync(slug, new[] { loader }, gameVersion);

        /// <summary>Best version (release first) matching optional loader / game-version filters; null on any error.</summary>
        public static async Task<JToken?> GetLatestVersionAsync(string slug, string[]? loaders, string? gameVersion)
        {
            try { return await GetBestVersionAsync(slug, loaders, gameVersion); }
            catch { return null; }
        }

        /// <summary>
        /// Best version of a project for the filters: newest release, else newest beta, else newest alpha.
        /// Null when the project or a fitting version doesn't exist; network problems throw.
        /// </summary>
        public static async Task<JToken?> GetBestVersionAsync(string idOrSlug, string[]? loaders, string? gameVersion)
        {
            var q = new List<string> { "include_changelog=false" };
            if (loaders != null && loaders.Length > 0)
                q.Add("loaders=" + Uri.EscapeDataString("[" + string.Join(",", loaders.Select(l => $"\"{l}\"")) + "]"));
            if (!string.IsNullOrEmpty(gameVersion))
                q.Add("game_versions=" + Uri.EscapeDataString($"[\"{gameVersion}\"]"));
            string url = $"{Api}/project/{Uri.EscapeDataString(idOrSlug)}/version?" + string.Join("&", q);
            string json;
            try { json = await BlockifyHttp.GetStringAsync(url); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
            return PickPreferred(JArray.Parse(json));
        }

        /// <summary>A single version by id; null if Modrinth doesn't know it (network problems throw).</summary>
        public static async Task<JToken?> GetVersionAsync(string versionId)
        {
            try { return JObject.Parse(await BlockifyHttp.GetStringAsync($"{Api}/version/{Uri.EscapeDataString(versionId)}")); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
        }

        /// <summary>Release first, then beta, then alpha — newest within the channel.</summary>
        public static JToken? PickPreferred(IEnumerable<JToken> versions)
        {
            var list = versions.OrderByDescending(PublishedAt).ToList();
            return list.FirstOrDefault(v => v.Value<string>("version_type") == "release")
                ?? list.FirstOrDefault(v => v.Value<string>("version_type") == "beta")
                ?? list.FirstOrDefault();
        }

        /// <summary>Which channels an update may come from, given the installed version's channel.</summary>
        public static string[] ChannelFor(string? versionType) => versionType switch
        {
            "release" => new[] { "release" },
            "beta" => new[] { "release", "beta" },
            _ => Array.Empty<string>()   // alpha / unknown: any
        };

        public static DateTime PublishedAt(JToken? version)
        {
            var t = version?["date_published"];
            if (t == null || t.Type == JTokenType.Null) return DateTime.MinValue;
            if (t.Type == JTokenType.Date) return ((DateTime)t).ToUniversalTime();
            return DateTime.TryParse(t.ToString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : DateTime.MinValue;
        }

        /// <summary>True when a version supports one of the loaders (if any given) and the game version (if given).</summary>
        public static bool Fits(JToken version, string[]? loaders, string? gameVersion)
        {
            var vl = Strings(version["loaders"]);
            if (loaders != null && loaders.Length > 0 && !vl.Intersect(loaders, StringComparer.OrdinalIgnoreCase).Any()) return false;
            if (!string.IsNullOrEmpty(gameVersion) && !Strings(version["game_versions"]).Contains(gameVersion)) return false;
            return true;
        }

        public static (string Url, string FileName)? PrimaryFile(JToken? version)
        {
            var f = PrimaryFileToken(version);
            string? u = f?.Value<string>("url"), n = f?.Value<string>("filename");
            return u == null || n == null ? null : (u, n);
        }

        /// <summary>Primary file with its sha1 (for verified downloads).</summary>
        public static (string Url, string FileName, string? Sha1)? PrimaryFileWithHash(JToken? version)
        {
            var f = PrimaryFileToken(version);
            string? u = f?.Value<string>("url"), n = f?.Value<string>("filename");
            return u == null || n == null ? null : (u, n, f?["hashes"]?.Value<string>("sha1"));
        }

        private static JToken? PrimaryFileToken(JToken? version)
        {
            var files = version?["files"] as JArray;
            return files?.FirstOrDefault(x => x.Value<bool?>("primary") == true) ?? files?.FirstOrDefault();
        }

        /// <summary>Project slug + title by id or slug; null when Modrinth doesn't know it.</summary>
        public static async Task<(string Slug, string Title)?> GetProjectAsync(string idOrSlug)
        {
            try
            {
                var j = JObject.Parse(await BlockifyHttp.GetStringAsync($"{Api}/project/{Uri.EscapeDataString(idOrSlug)}"));
                return (j.Value<string>("slug") ?? idOrSlug, j.Value<string>("title") ?? idOrSlug);
            }
            catch
            {
                // not a slug — try a mod search by that id/name
                try
                {
                    var hits = await SearchAsync(query: idOrSlug, limit: 1, projectType: "mod");
                    return hits.Count > 0 ? (hits[0].Slug, hits[0].Title) : null;
                }
                catch { return null; }
            }
        }

        /// <summary>id → (slug, title) for a batch of project ids/slugs (one request).</summary>
        public static async Task<Dictionary<string, (string Slug, string Title)>> GetProjectsAsync(IEnumerable<string> ids)
        {
            var map = new Dictionary<string, (string Slug, string Title)>();
            var list = ids.Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();
            if (list.Count == 0) return map;
            string idsParam = "[" + string.Join(",", list.Select(i => "\"" + i + "\"")) + "]";
            var arr = JArray.Parse(await BlockifyHttp.GetStringAsync($"{Api}/projects?ids=" + Uri.EscapeDataString(idsParam)));
            foreach (var pr in arr)
            {
                string id = pr.Value<string>("id") ?? "";
                if (id.Length > 0) map[id] = (pr.Value<string>("slug") ?? id, pr.Value<string>("title") ?? id);
            }
            return map;
        }

        /// <summary>Same, with optional filters: null loaders / null game version = no filter.</summary>
        public static async Task<(string Url, string FileName)?> GetLatestVersionFileAsync(string slug, string[]? loaders, string? gameVersion)
            => PrimaryFile(await GetLatestVersionAsync(slug, loaders, gameVersion));

        /// <summary>sha1 → Modrinth version object for every hash Modrinth knows (used to build .mrpack indexes).</summary>
        public static async Task<JObject> GetVersionsByHashAsync(List<string> sha1s)
        {
            if (sha1s.Count == 0) return new JObject();
            try { return await LookupHashesAsync(sha1s); }
            catch { return new JObject(); }
        }

        /// <summary>sha1 → version object; unlike <see cref="GetVersionsByHashAsync"/> network errors throw.</summary>
        public static async Task<JObject> LookupHashesAsync(IEnumerable<string> sha1s)
        {
            var list = sha1s.Where(h => !string.IsNullOrEmpty(h)).Distinct().ToList();
            if (list.Count == 0) return new JObject();
            return await PostJsonAsync(Api + "/version_files",
                new JObject { ["hashes"] = new JArray(list), ["algorithm"] = "sha1" });
        }

        /// <summary>Modrinth CDN urls carry the project id: cdn.modrinth.com/data/{project}/versions/{version}/file.</summary>
        public static string? ProjectIdFromUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(url, @"cdn\.modrinth\.com/data/([A-Za-z0-9]{8})/");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string[] Strings(JToken? arr)
            => (arr as JArray)?.Select(x => x.Type == JTokenType.String ? (string?)x : null)
                   .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToArray()
               ?? Array.Empty<string>();

        private static async Task<JObject> PostJsonAsync(string url, JObject body)
            => JObject.Parse(await BlockifyHttp.PostJsonAsync(url, body.ToString(Newtonsoft.Json.Formatting.None)));
    }
}
