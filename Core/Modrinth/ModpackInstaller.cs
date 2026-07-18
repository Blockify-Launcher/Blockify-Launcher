using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace BlockifyLauncher.Core.Modrinth
{
    /// <summary>A single installable version of a modpack (one Modrinth version = one MC + loader combo).</summary>
    public class PackVersionOption
    {
        public string Id = "";
        public string Name = "";
        public string VersionNumber = "";
        public string GameVersion = "";
        public string Loader = "";
        public string MrpackUrl = "";
        public string MrpackFile = "";
        public string DatePublished = "";
        public bool Supported => Loader is "fabric" or "quilt" or "forge" or "neoforge";
    }

    /// <summary>Install progress step: a human phase plus optional file counts.</summary>
    public readonly record struct InstallProgress(string Phase, int Cur, int Total);

    /// <summary>A modpack installed into its own isolated instance folder.</summary>
    public class InstalledPack
    {
        public string Slug = "";
        public string Title = "";
        public string Icon = "";
        public string VersionName = "";   // loader profile id passed to the launcher
        public string McVersion = "";
        public string Loader = "";
        public string LoaderVersion = "";
        public string InstanceDir = "";
        public string PackVersion = "";   // human version number (e.g. v26.5)
        public string VersionId = "";     // Modrinth version id (for repair/reinstall)
        public string InstalledAt = "";
    }

    /// <summary>
    /// Installs Modrinth modpacks (.mrpack) into isolated instances:
    /// downloads the pack, writes a Fabric/Quilt loader profile into the shared
    /// versions folder, pulls the pack's mods into the instance, and copies overrides.
    /// Vanilla + loader library download is done by the caller via the launcher afterwards.
    /// </summary>
    public static class ModpackInstaller
    {
        private static readonly HttpClient Http = Create();
        private static HttpClient Create()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("BlockifyLauncher/0.3 (github.com/Blockify-Launcher)");
            return h;
        }

        public static async Task<List<PackVersionOption>> GetPackVersionsAsync(string slug)
        {
            var list = new List<PackVersionOption>();
            var arr = JArray.Parse(await Http.GetStringAsync($"https://api.modrinth.com/v2/project/{slug}/version"));
            foreach (var v in arr)
            {
                var files = (JArray?)v["files"];
                var mrpack = files?.FirstOrDefault(f => (f.Value<string>("filename") ?? "").EndsWith(".mrpack"))
                             ?? files?.FirstOrDefault(f => f.Value<bool?>("primary") == true)
                             ?? files?.FirstOrDefault();
                string url = mrpack?.Value<string>("url") ?? "";
                if (url.Length == 0) continue;

                list.Add(new PackVersionOption
                {
                    Id = v.Value<string>("id") ?? "",
                    Name = v.Value<string>("name") ?? "",
                    VersionNumber = v.Value<string>("version_number") ?? "",
                    GameVersion = ((JArray?)v["game_versions"])?.LastOrDefault()?.Value<string>() ?? "",
                    Loader = ((JArray?)v["loaders"])?.FirstOrDefault()?.Value<string>() ?? "",
                    MrpackUrl = url,
                    MrpackFile = mrpack?.Value<string>("filename") ?? "",
                    DatePublished = v.Value<string>("date_published") ?? ""
                });
            }
            return list;
        }

        public static async Task<InstalledPack> InstallAsync(
            string slug, string title, string icon, PackVersionOption ver,
            string versionsDir, string instanceDir, IProgress<InstallProgress>? log,
            Func<string, string, string, Task<string>>? installLoader = null)
        {
            log?.Report(new("Скачиваю .mrpack…", 0, 0));
            var mrpackBytes = await Http.GetByteArrayAsync(ver.MrpackUrl);

            using var zip = new ZipArchive(new MemoryStream(mrpackBytes), ZipArchiveMode.Read);
            var indexEntry = zip.GetEntry("modrinth.index.json")
                ?? throw new InvalidOperationException("В .mrpack нет modrinth.index.json");
            JObject index;
            using (var r = new StreamReader(indexEntry.Open()))
                index = JObject.Parse(await r.ReadToEndAsync());

            var deps = (JObject?)index["dependencies"] ?? new JObject();
            string mc = deps.Value<string>("minecraft") ?? ver.GameVersion;

            string loader, loaderVer;
            if (deps["fabric-loader"] != null) { loader = "fabric"; loaderVer = deps.Value<string>("fabric-loader")!; }
            else if (deps["quilt-loader"] != null) { loader = "quilt"; loaderVer = deps.Value<string>("quilt-loader")!; }
            else if (deps["forge"] != null) { loader = "forge"; loaderVer = deps.Value<string>("forge")!; }
            else if (deps["neoforge"] != null) { loader = "neoforge"; loaderVer = deps.Value<string>("neoforge")!; }
            else throw new NotSupportedException("Не удалось определить загрузчик модов этой сборки.");

            // 1. install the loader → get the launch profile id
            log?.Report(new($"Ставлю загрузчик {loader} {loaderVer}…", 0, 0));
            string versionName;
            if (loader is "fabric" or "quilt")
            {
                // Fabric/Quilt: fetch a ready profile JSON (libraries carry their own urls)
                string profileUrl = loader == "fabric"
                    ? $"https://meta.fabricmc.net/v2/versions/loader/{mc}/{loaderVer}/profile/json"
                    : $"https://meta.quiltmc.org/v3/versions/loader/{mc}/{loaderVer}/profile/json";
                var profileJson = JObject.Parse(await Http.GetStringAsync(profileUrl));
                versionName = profileJson.Value<string>("id") ?? $"{loader}-loader-{loaderVer}-{mc}";
                string vdir = Path.Combine(versionsDir, versionName);
                Directory.CreateDirectory(vdir);
                await File.WriteAllTextAsync(Path.Combine(vdir, versionName + ".json"), profileJson.ToString());
            }
            else
            {
                // Forge/NeoForge: run the official installer headlessly (needs a JVM → app layer)
                if (installLoader == null)
                    throw new NotSupportedException("Установка Forge/NeoForge недоступна в этой сборке лаунчера.");
                versionName = await installLoader(mc, loader, loaderVer);
                if (string.IsNullOrWhiteSpace(versionName))
                    throw new InvalidOperationException($"Не удалось установить {loader} {loaderVer}.");
            }

            // 2. download the pack's mod files into the isolated instance
            Directory.CreateDirectory(instanceDir);
            var mods = ((JArray?)index["files"] ?? new JArray())
                .Where(f => (f["env"]?["client"]?.Value<string>() ?? "required") != "unsupported")
                .ToList();

            int total = mods.Count, done = 0;
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
            var sem = new SemaphoreSlim(4);
            var tasks = mods.Select(async f =>
            {
                await sem.WaitAsync();
                try
                {
                    string rel = f.Value<string>("path") ?? "";
                    if (!IsSafeRelative(rel)) return;
                    string dest = Path.Combine(instanceDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    string? sha1 = f["hashes"]?.Value<string>("sha1");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                    // resume: a file already present and hash-correct is kept (fast re-install)
                    if (File.Exists(dest) && (sha1 == null || FileSha1(dest) == sha1)) return;

                    var urls = ((JArray?)f["downloads"])?.Select(u => u.Value<string>())
                        .Where(u => !string.IsNullOrEmpty(u)).ToList() ?? new List<string?>();

                    bool ok = false;
                    for (int attempt = 0; attempt < 3 && !ok; attempt++)
                    {
                        foreach (var u in urls)
                        {
                            try
                            {
                                var bytes = await Http.GetByteArrayAsync(u!);
                                if (sha1 != null && Sha1(bytes) != sha1) continue; // corrupt/wrong — try next
                                await File.WriteAllBytesAsync(dest, bytes);
                                ok = true; break;
                            }
                            catch { /* try next mirror */ }
                        }
                        if (!ok) await Task.Delay(400 * (attempt + 1)); // brief backoff before retrying
                    }
                    if (!ok) failures.Add(Path.GetFileName(rel));
                }
                finally
                {
                    int n = Interlocked.Increment(ref done);
                    log?.Report(new("Скачиваю моды", n, total));
                    sem.Release();
                }
            });
            await Task.WhenAll(tasks);

            // 3. copy overrides (config, resourcepacks, etc.) into the instance
            log?.Report(new("Копирую overrides…", 0, 0));
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName.Replace('\\', '/');
                string? sub = name.StartsWith("overrides/") ? name.Substring("overrides/".Length)
                            : name.StartsWith("client-overrides/") ? name.Substring("client-overrides/".Length)
                            : null;
                if (sub == null || sub.Length == 0 || sub.EndsWith("/")) continue;
                if (!IsSafeRelative(sub)) continue;
                string dest = Path.Combine(instanceDir, sub.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }

            // a missing mod usually breaks a dependency chain — surface it so the user
            // knows the instance is incomplete. Re-running install resumes the missing ones.
            if (!failures.IsEmpty)
            {
                var names = string.Join(", ", failures.Take(5));
                throw new InvalidOperationException(
                    $"Не скачалось модов: {failures.Count} (напр.: {names}). " +
                    "Нажми «Установить» ещё раз — докачаю только недостающие.");
            }

            return new InstalledPack
            {
                Slug = slug, Title = title, Icon = icon,
                VersionName = versionName, McVersion = mc,
                Loader = loader, LoaderVersion = loaderVer,
                InstanceDir = instanceDir, PackVersion = ver.VersionNumber, VersionId = ver.Id
            };
        }

        private static string Sha1(byte[] data)
        {
            using var s = System.Security.Cryptography.SHA1.Create();
            return Convert.ToHexString(s.ComputeHash(data)).ToLowerInvariant();
        }

        private static string FileSha1(string path)
        {
            using var s = System.Security.Cryptography.SHA1.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(s.ComputeHash(fs)).ToLowerInvariant();
        }

        // reject path traversal / absolute paths inside the archive
        private static bool IsSafeRelative(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return false;
            if (rel.Contains("..")) return false;
            if (Path.IsPathRooted(rel)) return false;
            if (rel.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            return true;
        }
    }
}
