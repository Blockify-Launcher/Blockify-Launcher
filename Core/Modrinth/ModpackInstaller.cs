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

        // per-pack launch overrides (0 / empty = use the launcher's global setting)
        public int RamMb = 0;
        public string JavaPath = "";
        public string JvmArgs = "";
        public int ScreenW = 0;
        public int ScreenH = 0;

        // one-click FPS boost: optimisation mods added by the launcher (tracked so it can be undone)
        public bool FpsBoost = false;
        public List<string> FpsFiles = new();
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
            return await InstallFromMrpackAsync(mrpackBytes, slug, title, icon, ver.VersionNumber, ver.Id, ver.GameVersion,
                versionsDir, instanceDir, log, installLoader);
        }

        /// <summary>Installs a .mrpack already in memory (catalog download or a local file).</summary>
        public static async Task<InstalledPack> InstallFromMrpackAsync(
            byte[] mrpackBytes, string slug, string title, string icon, string packVersion, string versionId, string fallbackMc,
            string versionsDir, string instanceDir, IProgress<InstallProgress>? log,
            Func<string, string, string, Task<string>>? installLoader = null)
        {
            using var zip = new ZipArchive(new MemoryStream(mrpackBytes), ZipArchiveMode.Read);
            var indexEntry = zip.GetEntry("modrinth.index.json")
                ?? throw new InvalidOperationException("В .mrpack нет modrinth.index.json");
            JObject index;
            using (var r = new StreamReader(indexEntry.Open()))
                index = JObject.Parse(await r.ReadToEndAsync());
            if (string.IsNullOrWhiteSpace(title)) title = index.Value<string>("name") ?? slug;
            if (string.IsNullOrWhiteSpace(packVersion)) packVersion = index.Value<string>("versionId") ?? "local";

            var deps = (JObject?)index["dependencies"] ?? new JObject();
            string mc = deps.Value<string>("minecraft") ?? fallbackMc;

            string loader, loaderVer;
            if (deps["fabric-loader"] != null) { loader = "fabric"; loaderVer = deps.Value<string>("fabric-loader")!; }
            else if (deps["quilt-loader"] != null) { loader = "quilt"; loaderVer = deps.Value<string>("quilt-loader")!; }
            else if (deps["forge"] != null) { loader = "forge"; loaderVer = deps.Value<string>("forge")!; }
            else if (deps["neoforge"] != null) { loader = "neoforge"; loaderVer = deps.Value<string>("neoforge")!; }
            else throw new NotSupportedException("Не удалось определить загрузчик модов этой сборки.");

            // 1. install the loader → get the launch profile id
            string versionName = await InstallLoaderAsync(mc, loader, loaderVer, versionsDir, log, installLoader);

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
                InstanceDir = instanceDir, PackVersion = packVersion, VersionId = versionId
            };
        }

        // ── loader install (shared by catalog install, local .mrpack and imports) ──
        public static async Task<string> InstallLoaderAsync(string mc, string loader, string loaderVer, string versionsDir,
            IProgress<InstallProgress>? log, Func<string, string, string, Task<string>>? installLoader)
        {
            log?.Report(new($"Ставлю загрузчик {loader} {loaderVer}…", 0, 0));
            if (loader is "fabric" or "quilt")
            {
                // Fabric/Quilt: fetch a ready profile JSON (libraries carry their own urls)
                string profileUrl = loader == "fabric"
                    ? $"https://meta.fabricmc.net/v2/versions/loader/{mc}/{loaderVer}/profile/json"
                    : $"https://meta.quiltmc.org/v3/versions/loader/{mc}/{loaderVer}/profile/json";
                var profileJson = JObject.Parse(await Http.GetStringAsync(profileUrl));
                string versionName = profileJson.Value<string>("id") ?? $"{loader}-loader-{loaderVer}-{mc}";
                string vdir = Path.Combine(versionsDir, versionName);
                Directory.CreateDirectory(vdir);
                await File.WriteAllTextAsync(Path.Combine(vdir, versionName + ".json"), profileJson.ToString());
                return versionName;
            }
            // Forge/NeoForge: run the official installer headlessly (needs a JVM → app layer)
            if (installLoader == null)
                throw new NotSupportedException("Установка Forge/NeoForge недоступна в этой сборке лаунчера.");
            string vn = await installLoader(mc, loader, loaderVer);
            if (string.IsNullOrWhiteSpace(vn))
                throw new InvalidOperationException($"Не удалось установить {loader} {loaderVer}.");
            return vn;
        }

        // ── import an instance from another launcher (Prism/MultiMC, CurseForge App) ──
        private static readonly HashSet<string> SkipOnCopy = new(StringComparer.OrdinalIgnoreCase)
            { "logs", "crash-reports", ".fabric", ".mixin.out", "cache", "versions", "libraries", "assets", "runtime", "natives" };

        public static async Task<InstalledPack> ImportInstanceAsync(string descriptorPath, string versionsDir, string packsRoot,
            IProgress<InstallProgress>? log, Func<string, string, string, Task<string>>? installLoader)
        {
            string dir = Path.GetDirectoryName(descriptorPath)!;
            string file = Path.GetFileName(descriptorPath).ToLowerInvariant();
            string title, mc = "", loader = "", loaderVer = "", srcDir = dir;

            if (file is "mmc-pack.json" or "instance.cfg")
            {
                // Prism / MultiMC: components list the game + loader versions
                var pack = JObject.Parse(File.ReadAllText(Path.Combine(dir, "mmc-pack.json")));
                foreach (var c in (JArray?)pack["components"] ?? new JArray())
                {
                    string uid = c.Value<string>("uid") ?? "", ver = c.Value<string>("version") ?? "";
                    switch (uid)
                    {
                        case "net.minecraft": mc = ver; break;
                        case "net.fabricmc.fabric-loader": loader = "fabric"; loaderVer = ver; break;
                        case "org.quiltmc.quilt-loader": loader = "quilt"; loaderVer = ver; break;
                        case "net.minecraftforge": loader = "forge"; loaderVer = ver; break;
                        case "net.neoforged.neoforge": loader = "neoforge"; loaderVer = ver; break;
                    }
                }
                title = Path.GetFileName(dir);
                string cfg = Path.Combine(dir, "instance.cfg");
                if (File.Exists(cfg))
                    foreach (var line in File.ReadAllLines(cfg))
                        if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) { title = line[5..].Trim(); break; }
                srcDir = Directory.Exists(Path.Combine(dir, ".minecraft")) ? Path.Combine(dir, ".minecraft")
                       : Directory.Exists(Path.Combine(dir, "minecraft")) ? Path.Combine(dir, "minecraft") : dir;
            }
            else if (file == "minecraftinstance.json")
            {
                // CurseForge App: baseModLoader.name is e.g. "forge-47.2.0" / "fabric-0.16.9"
                var j = JObject.Parse(File.ReadAllText(descriptorPath));
                title = j.Value<string>("name") ?? Path.GetFileName(dir);
                mc = j["baseModLoader"]?.Value<string>("minecraftVersion") ?? j.Value<string>("gameVersion") ?? "";
                string ln = j["baseModLoader"]?.Value<string>("name") ?? "";
                int dash = ln.IndexOf('-');
                if (dash > 0) { loader = ln[..dash].ToLowerInvariant(); loaderVer = ln[(dash + 1)..]; }
            }
            else throw new NotSupportedException(
                "Не узнаю формат. Выбери .mrpack, mmc-pack.json / instance.cfg (Prism, MultiMC) или minecraftinstance.json (CurseForge App).");

            if (string.IsNullOrEmpty(mc)) throw new InvalidOperationException("Не удалось определить версию Minecraft у этой сборки.");

            string slug = UniqueSlug(Slugify(title), packsRoot);
            string instanceDir = Path.Combine(packsRoot, slug);
            log?.Report(new("Копирую файлы сборки…", 0, 0));
            await Task.Run(() => CopyInstance(srcDir, instanceDir));

            string versionName;
            if (string.IsNullOrEmpty(loader)) { loader = "vanilla"; versionName = mc; }
            else versionName = await InstallLoaderAsync(mc, loader, loaderVer, versionsDir, log, installLoader);

            return new InstalledPack
            {
                Slug = slug, Title = title, Icon = "",
                VersionName = versionName, McVersion = mc, Loader = loader, LoaderVersion = loaderVer,
                InstanceDir = instanceDir, PackVersion = "import"
            };
        }

        public static string Slugify(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in (s ?? "").ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
            string r = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
            return r.Length == 0 ? "imported-pack" : r;
        }

        private static string UniqueSlug(string slug, string packsRoot)
        {
            string s = slug; int n = 2;
            while (Directory.Exists(Path.Combine(packsRoot, s))) s = slug + "-" + n++;
            return s;
        }

        private static void CopyInstance(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var d in Directory.GetDirectories(src))
            {
                if (SkipOnCopy.Contains(Path.GetFileName(d))) continue;
                CopyInstance(d, Path.Combine(dst, Path.GetFileName(d)));
            }
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        }

        // ── one-click FPS boost ──
        // Each entry is a group of alternatives; the first project with a matching version wins.
        private static readonly string[][] FpsModsFabric =
            { new[] { "sodium" }, new[] { "lithium" }, new[] { "ferrite-core" }, new[] { "immediatelyfast" }, new[] { "entityculling" } };
        private static readonly string[][] FpsModsForge =
            { new[] { "embeddium", "sodium" }, new[] { "ferrite-core" }, new[] { "immediatelyfast" }, new[] { "entityculling" }, new[] { "modernfix" } };
        private static readonly string[][] FpsModsNeoForge =
            { new[] { "sodium", "embeddium" }, new[] { "ferrite-core" }, new[] { "immediatelyfast" }, new[] { "entityculling" }, new[] { "modernfix" } };

        /// <summary>Downloads optimisation mods for the pack's loader/version into its mods folder.
        /// Returns the file names it added (already-present files are skipped and not tracked).</summary>
        public static async Task<List<string>> InstallFpsBoostAsync(InstalledPack pack, IProgress<InstallProgress>? log)
        {
            var groups = pack.Loader switch
            {
                "forge" => FpsModsForge,
                "neoforge" => FpsModsNeoForge,
                _ => FpsModsFabric
            };
            // Quilt runs Fabric mods; Modrinth often lists only "fabric"
            string[] loaders = pack.Loader == "quilt" ? new[] { "quilt", "fabric" } : new[] { pack.Loader };

            string modsDir = Path.Combine(pack.InstanceDir, "mods");
            Directory.CreateDirectory(modsDir);
            var added = new List<string>();
            int i = 0;
            foreach (var group in groups)
            {
                log?.Report(new("Буст FPS: " + group[0], ++i, groups.Length));
                foreach (var slug in group)
                {
                    (string Url, string FileName)? hit = null;
                    foreach (var ld in loaders)
                    {
                        hit = await ModrinthService.GetLatestVersionFileAsync(slug, ld, pack.McVersion);
                        if (hit != null) break;
                    }
                    if (hit == null) continue;
                    string dest = Path.Combine(modsDir, Path.GetFileName(hit.Value.FileName));
                    if (File.Exists(dest) || File.Exists(dest + ".disabled")) break;   // pack already ships it
                    try
                    {
                        await File.WriteAllBytesAsync(dest, await Http.GetByteArrayAsync(hit.Value.Url));
                        added.Add(Path.GetFileName(dest));
                    }
                    catch { }
                    break;
                }
            }
            return added;
        }

        // ── export an instance back to .mrpack ──
        private static readonly string[] OverrideDirs = { "config", "resourcepacks", "shaderpacks", "datapacks", "kubejs", "scripts", "defaultconfigs" };

        /// <summary>Builds a Modrinth-format pack: mods known to Modrinth go into the index
        /// (downloadable), everything else is bundled under overrides/.</summary>
        public static async Task ExportAsync(InstalledPack pack, string outPath, IProgress<InstallProgress>? log)
        {
            string modsDir = Path.Combine(pack.InstanceDir, "mods");
            var jars = Directory.Exists(modsDir) ? new DirectoryInfo(modsDir).GetFiles("*.jar") : Array.Empty<FileInfo>();

            log?.Report(new("Считаю хэши модов", 0, jars.Length));
            var byHash = new Dictionary<string, FileInfo>();
            foreach (var f in jars) { try { byHash[FileSha1(f.FullName)] = f; } catch { } }

            log?.Report(new("Сверяю с Modrinth", 0, 0));
            var known = await ModrinthService.GetVersionsByHashAsync(byHash.Keys.ToList());

            var files = new JArray();
            var unknown = new List<FileInfo>();
            foreach (var (sha1, f) in byHash)
            {
                var ver = known[sha1];
                var vf = ((JArray?)ver?["files"])?.FirstOrDefault(x => x["hashes"]?.Value<string>("sha1") == sha1);
                if (vf == null) { unknown.Add(f); continue; }
                files.Add(new JObject
                {
                    ["path"] = "mods/" + f.Name,
                    ["hashes"] = new JObject { ["sha1"] = sha1, ["sha512"] = vf["hashes"]?.Value<string>("sha512") ?? FileSha512(f.FullName) },
                    ["env"] = new JObject { ["client"] = "required", ["server"] = "required" },
                    ["downloads"] = new JArray(vf.Value<string>("url") ?? ""),
                    ["fileSize"] = f.Length
                });
            }

            string depKey = pack.Loader switch { "fabric" => "fabric-loader", "quilt" => "quilt-loader", _ => pack.Loader };
            var index = new JObject
            {
                ["formatVersion"] = 1,
                ["game"] = "minecraft",
                ["versionId"] = string.IsNullOrEmpty(pack.PackVersion) ? "1.0.0" : pack.PackVersion,
                ["name"] = pack.Title,
                ["summary"] = "Exported from Blockify Launcher",
                ["files"] = files,
                ["dependencies"] = new JObject { ["minecraft"] = pack.McVersion, [depKey] = pack.LoaderVersion }
            };

            log?.Report(new("Пакую .mrpack", 0, 0));
            if (File.Exists(outPath)) File.Delete(outPath);
            using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create);
            using (var w = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
                await w.WriteAsync(index.ToString());

            foreach (var f in unknown)
                zip.CreateEntryFromFile(f.FullName, "overrides/mods/" + f.Name);
            foreach (var dir in OverrideDirs)
            {
                string full = Path.Combine(pack.InstanceDir, dir);
                if (!Directory.Exists(full)) continue;
                foreach (var f in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(f, "overrides/" + Path.GetRelativePath(pack.InstanceDir, f).Replace('\\', '/'));
            }
        }

        private static string FileSha512(string path)
        {
            using var s = System.Security.Cryptography.SHA512.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(s.ComputeHash(fs)).ToLowerInvariant();
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
