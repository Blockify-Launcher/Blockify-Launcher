using BlockifyLauncher.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Compression;
using System.Net;
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
        public string? MrpackSha1;
        public string VersionType = "";
        public string DatePublished = "";
        public bool Supported => Loader is "fabric" or "quilt" or "forge" or "neoforge";
    }

    /// <summary>One file the pack installer put into an instance (index download or override).</summary>
    public class PackFileRecord
    {
        public string Path = "";        // relative, "/"-separated (as in the .mrpack)
        public string Sha1 = "";        // content as written by the installer
        public bool Override;           // from overrides/ (configs etc.), not a download
        public string? Project;         // Modrinth project id of a download (from its CDN url)
    }

    /// <summary>
    /// What the last install of a pack wrote (&lt;instance&gt;/.blockify/install.json). Reinstall/update
    /// uses it to remove files of the old pack version that nobody changed, keep the player's own
    /// files and edits, and keep switched-off mods switched off.
    /// </summary>
    public class PackInstallManifest
    {
        public string VersionId = "";
        public List<PackFileRecord> Files = new();
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

        // what the last (re)install changed — for the log only, not stored
        [JsonIgnore] public string LastInstallNote = "";
    }

    /// <summary>
    /// Installs Modrinth modpacks (.mrpack) into isolated instances:
    /// downloads the pack, writes a Fabric/Quilt loader profile into the shared
    /// versions folder, pulls the pack's mods into the instance, and copies overrides.
    /// Vanilla + loader library download is done by the caller via the launcher afterwards.
    /// </summary>
    public static class ModpackInstaller
    {
        public static async Task<List<PackVersionOption>> GetPackVersionsAsync(string slug)
        {
            var list = new List<PackVersionOption>();
            var arr = JArray.Parse(await BlockifyHttp.GetStringAsync(
                $"{ModrinthService.Api}/project/{Uri.EscapeDataString(slug)}/version?include_changelog=false"));
            foreach (var v in arr)
                if (ParsePackVersion(v) is { } pv) list.Add(pv);
            return list;
        }

        /// <summary>One pack version by id (no need to pull the whole version list); null if unknown.</summary>
        public static async Task<PackVersionOption?> GetPackVersionAsync(string versionId)
        {
            if (string.IsNullOrEmpty(versionId)) return null;
            var v = await ModrinthService.GetVersionAsync(versionId);
            return v == null ? null : ParsePackVersion(v);
        }

        private static PackVersionOption? ParsePackVersion(JToken v)
        {
            var files = (JArray?)v["files"];
            var mrpack = files?.FirstOrDefault(f => (f.Value<string>("filename") ?? "").EndsWith(".mrpack"))
                         ?? files?.FirstOrDefault(f => f.Value<bool?>("primary") == true)
                         ?? files?.FirstOrDefault();
            string url = mrpack?.Value<string>("url") ?? "";
            if (url.Length == 0) return null;
            return new PackVersionOption
            {
                Id = v.Value<string>("id") ?? "",
                Name = v.Value<string>("name") ?? "",
                VersionNumber = v.Value<string>("version_number") ?? "",
                GameVersion = ((JArray?)v["game_versions"])?.LastOrDefault()?.Value<string>() ?? "",
                Loader = ((JArray?)v["loaders"])?.FirstOrDefault()?.Value<string>() ?? "",
                MrpackUrl = url,
                MrpackFile = mrpack?.Value<string>("filename") ?? "",
                MrpackSha1 = mrpack?["hashes"]?.Value<string>("sha1"),
                VersionType = v.Value<string>("version_type") ?? "",
                DatePublished = v.Value<string>("date_published") ?? ""
            };
        }

        /// <summary>
        /// Newer version of an installed catalog pack on the same loader + Minecraft version
        /// (same channel: a release only offers releases). Null when up to date or not comparable.
        /// </summary>
        public static async Task<PackVersionOption?> FindPackUpdateAsync(InstalledPack p)
        {
            if (string.IsNullOrEmpty(p.VersionId) || string.IsNullOrEmpty(p.Slug)) return null;
            var q = new List<string> { "include_changelog=false" };
            if (p.Loader is "fabric" or "quilt" or "forge" or "neoforge")
                q.Add("loaders=" + Uri.EscapeDataString($"[\"{p.Loader}\"]"));
            if (!string.IsNullOrEmpty(p.McVersion))
                q.Add("game_versions=" + Uri.EscapeDataString($"[\"{p.McVersion}\"]"));
            string json;
            try
            {
                json = await BlockifyHttp.GetStringAsync(
                    $"{ModrinthService.Api}/project/{Uri.EscapeDataString(p.Slug)}/version?" + string.Join("&", q));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }

            var arr = JArray.Parse(json);
            var cur = arr.FirstOrDefault(v => v.Value<string>("id") == p.VersionId);
            if (cur == null) return null;   // installed version isn't in this loader/MC line — can't compare safely
            DateTime curAt = ModrinthService.PublishedAt(cur);
            var channel = ModrinthService.ChannelFor(cur.Value<string>("version_type"));
            var newer = arr
                .Where(v => ModrinthService.PublishedAt(v) > curAt
                            && (channel.Length == 0 || channel.Contains(v.Value<string>("version_type") ?? "")))
                .OrderByDescending(ModrinthService.PublishedAt)
                .FirstOrDefault();
            return newer == null ? null : ParsePackVersion(newer);
        }

        /// <param name="previousVersionId">Modrinth version installed in <paramref name="instanceDir"/> before
        /// (repair / update); lets older installs without install.json still clean up the old version.</param>
        public static async Task<InstalledPack> InstallAsync(
            string slug, string title, string icon, PackVersionOption ver,
            string versionsDir, string instanceDir, IProgress<InstallProgress>? log,
            Func<string, string, string, Task<string>>? installLoader = null, string? previousVersionId = null)
        {
            string tmp = TempMrpackPath(ver.Id);
            try
            {
                log?.Report(new("Скачиваю .mrpack…", 0, 0));
                await BlockifyHttp.DownloadToFileAsync(ver.MrpackUrl, tmp, ver.MrpackSha1, new MbProgress(log, "Скачиваю .mrpack"));

                // what the previous install wrote: install.json, or rebuilt from the old version's .mrpack
                PackInstallManifest? previous = ReadManifest(instanceDir);
                if (previous == null && !string.IsNullOrEmpty(previousVersionId) && Directory.Exists(instanceDir))
                {
                    previous = previousVersionId == ver.Id
                        ? BuildManifestFromFile(tmp, ver.Id)
                        : await TryBuildManifestForVersionAsync(previousVersionId, log);
                }

                return await InstallFromMrpackAsync(tmp, slug, title, icon, ver.VersionNumber, ver.Id, ver.GameVersion,
                    versionsDir, instanceDir, log, installLoader, previous);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        /// <summary>
        /// Installs a .mrpack file (catalog download or a local file) into <paramref name="instanceDir"/>.
        /// Over an existing install: files of the previous pack version that nobody changed are removed,
        /// the player's own files / worlds / options.txt / servers.dat stay, configs the player edited stay
        /// unless the pack author changed the same file, and switched-off mods stay switched off.
        /// </summary>
        public static async Task<InstalledPack> InstallFromMrpackAsync(
            string mrpackPath, string slug, string title, string icon, string packVersion, string versionId, string fallbackMc,
            string versionsDir, string instanceDir, IProgress<InstallProgress>? log,
            Func<string, string, string, Task<string>>? installLoader = null, PackInstallManifest? previous = null)
        {
            using var zip = ZipFile.OpenRead(mrpackPath);
            var index = ReadIndex(zip);
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

            Directory.CreateDirectory(instanceDir);
            previous ??= ReadManifest(instanceDir);
            log?.Report(new("Готовлю список файлов…", 0, 0));
            var fresh = BuildManifest(zip, index, versionId);

            var prevByPath = new Dictionary<string, PackFileRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in previous?.Files ?? new List<PackFileRecord>()) prevByPath.TryAdd(NormRel(r.Path), r);
            // mods the player switched off in the previous version (matched by Modrinth project)
            var disabledProjects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in previous?.Files ?? new List<PackFileRecord>())
            {
                if (r.Override || r.Project == null) continue;
                string? p = AbsPath(instanceDir, r.Path);
                if (p != null && !File.Exists(p) && File.Exists(p + ".disabled")) disabledProjects.Add(r.Project);
            }

            // 2. download the pack's files into the isolated instance
            var entries = IndexFiles(index);
            int total = entries.Count, done = 0;
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
            var sem = new SemaphoreSlim(4);
            var tasks = entries.Select(async f =>
            {
                await sem.WaitAsync();
                try
                {
                    string dest = AbsPath(instanceDir, f.Rel)!;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    string? project = f.Urls.Select(ModrinthService.ProjectIdFromUrl).FirstOrDefault(x => x != null);
                    // a mod the player switched off stays off: same file, or same project in the previous version
                    bool keepOff = f.Rel.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !File.Exists(dest)
                        && (File.Exists(dest + ".disabled") || (project != null && disabledProjects.Contains(project)));
                    string target = keepOff ? dest + ".disabled" : dest;

                    // resume: a file already present and hash-correct is kept (fast re-install)
                    if (File.Exists(target) && (f.Sha1 == null || ModScanner.Sha1File(target) == f.Sha1)) return;

                    bool ok = false;
                    for (int attempt = 0; attempt < 3 && !ok; attempt++)
                    {
                        foreach (var u in f.Urls)
                        {
                            try
                            {
                                // streamed to .part + sha1-verified; no whole-file timeout, only a stall timeout
                                await BlockifyHttp.DownloadToFileAsync(u, target, f.Sha1);
                                ok = true; break;
                            }
                            catch { /* try next mirror */ }
                        }
                        if (!ok) await Task.Delay(400 * (attempt + 1)); // brief backoff before retrying
                    }
                    if (!ok) failures.Add(Path.GetFileName(f.Rel));
                }
                finally
                {
                    int n = Interlocked.Increment(ref done);
                    log?.Report(new("Скачиваю моды", n, total));
                    sem.Release();
                }
            });
            await Task.WhenAll(tasks);

            // 3. overrides (config, resourcepacks, …): the pack's version unless the player's should win
            log?.Report(new("Копирую overrides…", 0, 0));
            var newSha = fresh.Files.Where(f => f.Override)
                .ToDictionary(f => NormRel(f.Path), f => f.Sha1, StringComparer.OrdinalIgnoreCase);
            int keptPlayer = 0;
            foreach (var (rel, entry) in CollectOverrides(zip))
            {
                string dest = AbsPath(instanceDir, rel)!;
                string sha = newSha.GetValueOrDefault(NormRel(rel)) ?? "";
                // a bundled mod the player switched off stays off
                string target = rel.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !File.Exists(dest) && File.Exists(dest + ".disabled")
                    ? dest + ".disabled" : dest;
                if (File.Exists(target))
                {
                    string cur;
                    try { cur = ModScanner.Sha1File(target); } catch { cur = ""; }
                    if (sha.Length > 0 && cur == sha) continue;
                    // the player's own settings (keybinds, video, server list) are never replaced once they exist
                    if (IsPlayerFile(rel)) { keptPlayer++; continue; }
                    // changed locally, while the pack author didn't change this file in this version → keep the local one
                    if (prevByPath.TryGetValue(NormRel(rel), out var was) && was.Sha1.Length > 0 && was.Sha1 == sha) { keptPlayer++; continue; }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            // a missing mod usually breaks a dependency chain — surface it so the user
            // knows the instance is incomplete. Re-running install resumes the missing ones
            // (the old version's files are only cleaned up once everything is in place).
            if (!failures.IsEmpty)
            {
                var names = string.Join(", ", failures.Take(5));
                throw new InvalidOperationException(
                    $"Не скачалось модов: {failures.Count} (напр.: {names}). " +
                    "Нажми «Починить» или установи ещё раз — докачаю только недостающие.");
            }

            // 4. remove what the previous version installed and the new one doesn't ship — only files nobody changed
            int removed = 0;
            if (previous != null)
            {
                var keep = new HashSet<string>(fresh.Files.Select(f => NormRel(f.Path)), StringComparer.OrdinalIgnoreCase);
                foreach (var old in previous.Files)
                {
                    if (old.Sha1.Length == 0 || keep.Contains(NormRel(old.Path)) || IsPlayerFile(old.Path)) continue;
                    string? p = AbsPath(instanceDir, old.Path);
                    if (p == null) continue;
                    foreach (var cand in new[] { p, p + ".disabled" })
                    {
                        try
                        {
                            if (File.Exists(cand) && ModScanner.Sha1File(cand) == old.Sha1) { File.Delete(cand); removed++; }
                        }
                        catch { }
                    }
                }
            }
            WriteManifest(instanceDir, fresh);

            return new InstalledPack
            {
                Slug = slug, Title = title, Icon = icon,
                VersionName = versionName, McVersion = mc,
                Loader = loader, LoaderVersion = loaderVer,
                InstanceDir = instanceDir, PackVersion = packVersion, VersionId = versionId,
                LastInstallNote = $"files={fresh.Files.Count} removedOld={removed} keptLocal={keptPlayer} previous={(previous == null ? "none" : previous.VersionId)}"
            };
        }

        // ── install manifest (<instance>/.blockify/install.json) ──

        private static string ManifestPath(string instanceDir) => Path.Combine(instanceDir, ".blockify", "install.json");

        public static PackInstallManifest? ReadManifest(string instanceDir)
        {
            try
            {
                string p = ManifestPath(instanceDir);
                return File.Exists(p) ? JsonConvert.DeserializeObject<PackInstallManifest>(File.ReadAllText(p)) : null;
            }
            catch { return null; }
        }

        private static void WriteManifest(string instanceDir, PackInstallManifest m)
        {
            string p = ManifestPath(instanceDir);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(m));
            File.Move(tmp, p, overwrite: true);
        }

        /// <summary>Manifest of a pack version without installing it (older installs have no install.json).</summary>
        public static async Task<PackInstallManifest?> TryBuildManifestForVersionAsync(string versionId, IProgress<InstallProgress>? log)
        {
            string tmp = TempMrpackPath(versionId);
            try
            {
                var old = await GetPackVersionAsync(versionId);
                if (old == null) return null;
                log?.Report(new("Сверяю с установленной версией сборки…", 0, 0));
                await BlockifyHttp.DownloadToFileAsync(old.MrpackUrl, tmp, old.MrpackSha1);
                return BuildManifestFromFile(tmp, versionId);
            }
            catch { return null; }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public static PackInstallManifest BuildManifestFromFile(string mrpackPath, string versionId)
        {
            using var zip = ZipFile.OpenRead(mrpackPath);
            return BuildManifest(zip, ReadIndex(zip), versionId);
        }

        private static PackInstallManifest BuildManifest(ZipArchive zip, JObject index, string versionId)
        {
            var m = new PackInstallManifest { VersionId = versionId };
            foreach (var f in IndexFiles(index))
                m.Files.Add(new PackFileRecord
                {
                    Path = f.Rel, Sha1 = f.Sha1 ?? "",
                    Project = f.Urls.Select(ModrinthService.ProjectIdFromUrl).FirstOrDefault(x => x != null)
                });
            foreach (var (rel, entry) in CollectOverrides(zip))
            {
                string sha;
                try { sha = EntrySha1(entry); } catch { sha = ""; }
                m.Files.Add(new PackFileRecord { Path = rel, Sha1 = sha, Override = true });
            }
            return m;
        }

        private static JObject ReadIndex(ZipArchive zip)
        {
            var indexEntry = zip.GetEntry("modrinth.index.json")
                ?? throw new InvalidOperationException("В .mrpack нет modrinth.index.json");
            using var r = new StreamReader(indexEntry.Open());
            return JObject.Parse(r.ReadToEnd());
        }

        private sealed record IndexFile(string Rel, string? Sha1, List<string> Urls);

        // client-side files of the index with a safe relative path
        private static List<IndexFile> IndexFiles(JObject index)
            => ((JArray?)index["files"] ?? new JArray())
                .Where(f => (f["env"]?["client"]?.Value<string>() ?? "required") != "unsupported")
                .Select(f => new IndexFile(
                    (f.Value<string>("path") ?? "").Replace('\\', '/'),
                    f["hashes"]?.Value<string>("sha1"),
                    ((JArray?)f["downloads"])?.Select(u => u.Value<string>() ?? "").Where(u => u.Length > 0).ToList()
                        ?? new List<string>()))
                .Where(f => IsSafeRelative(f.Rel))
                .ToList();

        // relative path → entry; client-overrides win over overrides for the same path
        private static Dictionary<string, ZipArchiveEntry> CollectOverrides(ZipArchive zip)
        {
            var map = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var prefix in new[] { "overrides/", "client-overrides/" })
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    string sub = name.Substring(prefix.Length);
                    if (sub.Length == 0 || sub.EndsWith("/") || !IsSafeRelative(sub)) continue;
                    map[sub] = entry;
                }
            return map;
        }

        // top-level files that belong to the player once they exist
        private static readonly HashSet<string> PlayerFiles = new(StringComparer.OrdinalIgnoreCase)
            { "options.txt", "optionsof.txt", "optionsshaders.txt", "servers.dat", "servers.dat_old" };

        private static bool IsPlayerFile(string rel)
        {
            string n = NormRel(rel);
            return !n.Contains('/') && PlayerFiles.Contains(n);
        }

        private static string NormRel(string rel) => (rel ?? "").Replace('\\', '/').TrimStart('/');

        private static string? AbsPath(string root, string rel)
            => IsSafeRelative(rel) ? Path.Combine(root, NormRel(rel).Replace('/', Path.DirectorySeparatorChar)) : null;

        private static string EntrySha1(ZipArchiveEntry e)
        {
            using var s = System.Security.Cryptography.SHA1.Create();
            using var st = e.Open();
            return Convert.ToHexString(s.ComputeHash(st)).ToLowerInvariant();
        }

        private static string TempMrpackPath(string versionId)
            => Path.Combine(Path.GetTempPath(), $"blockify-{ModScanner.SafeFileName(versionId)}-{Guid.NewGuid():N}.mrpack");

        /// <summary>
        /// A jar the pack ships must not sit next to another copy of the same mod (same mod id).
        /// FPS-boost copies are deleted (and dropped from FpsFiles); the player's own copies are switched
        /// off (.disabled), never deleted. Without install.json (older installs) only the launcher's own
        /// boost copies are touched — that also cleans up duplicates made by the old name-based boost.
        /// Returns the affected file names.
        /// </summary>
        public static List<string> DisableDuplicateMods(InstalledPack pack, string? instanceDir = null)
        {
            var changed = new List<string>();
            string inst = string.IsNullOrEmpty(instanceDir) ? pack.InstanceDir : instanceDir;
            pack.FpsFiles ??= new List<string>();
            var man = ReadManifest(inst);
            if (man == null && pack.FpsFiles.Count == 0) return changed;
            string modsDir = Path.Combine(inst, "mods");
            var mods = ModScanner.Scan(modsDir, hash: false);
            var packJars = man != null
                ? new HashSet<string>(man.Files
                    .Select(f => NormRel(f.Path))
                    .Where(p => p.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && p.IndexOf('/', 5) < 0)
                    .Select(p => p.Substring(5)), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(mods.Select(m => m.FileName)
                    .Where(n => !pack.FpsFiles.Contains(n, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
            var packIds = new HashSet<string>(mods.Where(m => m.Enabled && packJars.Contains(m.FileName)).SelectMany(m => m.ModIds),
                StringComparer.OrdinalIgnoreCase);
            bool packRenderer = packIds.Overlaps(RendererIds);
            foreach (var m in mods)
            {
                if (!m.Enabled || packJars.Contains(m.FileName)) continue;
                bool boost = pack.FpsFiles.Contains(m.FileName, StringComparer.OrdinalIgnoreCase);
                if (!boost && man == null) continue;
                bool dup = m.ModIds.Overlaps(packIds) || (boost && packRenderer && m.ModIds.Overlaps(RendererIds));
                if (!dup) continue;
                try
                {
                    if (boost)
                    {
                        File.Delete(m.Path);   // the pack ships this optimisation itself
                        pack.FpsFiles.RemoveAll(f => string.Equals(f, m.FileName, StringComparison.OrdinalIgnoreCase));
                    }
                    else File.Move(m.Path, m.Path + ".disabled", overwrite: true);
                    changed.Add(m.FileName);
                }
                catch { }
            }
            return changed;
        }

        // throttled "N МБ" progress for big single downloads
        private sealed class MbProgress : IProgress<long>
        {
            private readonly IProgress<InstallProgress>? _log;
            private readonly string _what;
            private long _lastMb = -1;
            public MbProgress(IProgress<InstallProgress>? log, string what) { _log = log; _what = what; }
            public void Report(long bytes)
            {
                long mb = bytes / (1024 * 1024);
                if (mb == _lastMb) return;
                _lastMb = mb;
                _log?.Report(new($"{_what}… {mb} МБ", 0, 0));
            }
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
                var profileJson = JObject.Parse(await BlockifyHttp.GetStringAsync(profileUrl));
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
            string title, mc = "", loader = "", loaderVer = "", srcDir = dir, importJvm = "";
            int importRam = 0;

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
                        case "net.neoforged":                                    // Prism's uid
                        case "net.neoforged.neoforge": loader = "neoforge"; loaderVer = ver; break;
                    }
                }
                title = Path.GetFileName(dir);
                string cfg = Path.Combine(dir, "instance.cfg");
                if (File.Exists(cfg))
                {
                    var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in File.ReadAllLines(cfg))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) kv[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                    }
                    if (kv.TryGetValue("name", out var nm) && nm.Length > 0) title = nm;
                    // carry over the instance's own memory / JVM settings when it overrides the global ones
                    if (kv.TryGetValue("OverrideMemory", out var om) && om == "true"
                        && kv.TryGetValue("MaxMemAlloc", out var mx) && int.TryParse(mx, out var mxMb)) importRam = mxMb;
                    if (kv.TryGetValue("OverrideJavaArgs", out var oj) && oj == "true"
                        && kv.TryGetValue("JvmArgs", out var ja)) importJvm = ja;
                }
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
                // some entries carry the game version too: "fabric-0.15.11-1.20.1" / "forge-1.20.1-47.2.0"
                if (mc.Length > 0)
                {
                    if (loaderVer.EndsWith("-" + mc)) loaderVer = loaderVer[..^(mc.Length + 1)];
                    if (loaderVer.StartsWith(mc + "-")) loaderVer = loaderVer[(mc.Length + 1)..];
                }
                string? fv = j["baseModLoader"]?.Value<string>("forgeVersion");
                if (!string.IsNullOrEmpty(fv) && loader is "forge" or "neoforge") loaderVer = fv;
            }
            else throw new NotSupportedException(
                "Не узнаю формат. Выбери .mrpack, mmc-pack.json / instance.cfg (Prism, MultiMC) или minecraftinstance.json (CurseForge App).");

            if (string.IsNullOrEmpty(mc)) throw new InvalidOperationException("Не удалось определить версию Minecraft у этой сборки.");

            string slug = UniqueSlug(Slugify(title), packsRoot);
            string instanceDir = Path.Combine(packsRoot, slug);

            // loader first: if it fails, no multi-GB copy is left behind
            string versionName;
            if (string.IsNullOrEmpty(loader)) { loader = "vanilla"; versionName = mc; }
            else versionName = await InstallLoaderAsync(mc, loader, loaderVer, versionsDir, log, installLoader);

            log?.Report(new("Копирую файлы сборки…", 0, 0));
            try { await Task.Run(() => CopyInstance(srcDir, instanceDir)); }
            catch
            {
                try { if (Directory.Exists(instanceDir)) Directory.Delete(instanceDir, true); } catch { }
                throw;
            }

            return new InstalledPack
            {
                Slug = slug, Title = title, Icon = "",
                VersionName = versionName, McVersion = mc, Loader = loader, LoaderVersion = loaderVer,
                InstanceDir = instanceDir, PackVersion = "import",
                RamMb = importRam, JvmArgs = importJvm
            };
        }

        /// <summary>
        /// Reinstall/update produces a fresh record; keep what the player set up on the old one
        /// (launch overrides, FPS boost state, a local cover) so repair doesn't wipe them.
        /// </summary>
        public static InstalledPack MergeInstalled(InstalledPack? existing, InstalledPack fresh)
        {
            if (existing == null) return fresh;
            fresh.RamMb = existing.RamMb;
            fresh.JavaPath = existing.JavaPath;
            fresh.JvmArgs = existing.JvmArgs;
            fresh.ScreenW = existing.ScreenW;
            fresh.ScreenH = existing.ScreenH;
            fresh.FpsBoost = existing.FpsBoost;
            fresh.FpsFiles = existing.FpsFiles ?? new();
            // a cover set from a screenshot lives in the instance folder — don't swap it for the store icon
            if (existing.Icon.Contains("/cover.png", StringComparison.OrdinalIgnoreCase)) fresh.Icon = existing.Icon;
            if (string.IsNullOrEmpty(fresh.Title)) fresh.Title = existing.Title;
            return fresh;
        }

        public static string Slugify(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in (s ?? "").ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
            string r = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
            return r.Length == 0 ? "imported-pack" : r;
        }

        public static string UniqueSlug(string slug, string packsRoot)
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

        // renderer replacements — only one of them may be in a pack (OptiFine is matched by file name)
        private static readonly HashSet<string> RendererSlugs = new(StringComparer.OrdinalIgnoreCase)
            { "sodium", "embeddium", "rubidium", "magnesium" };
        public static readonly HashSet<string> RendererIds = new(StringComparer.OrdinalIgnoreCase)
            { "sodium", "embeddium", "rubidium", "magnesium", "optifine" };

        /// <summary>Downloads optimisation mods for the pack's loader/version into its mods folder.
        /// A mod counts as present when the pack has the same Modrinth project (sha1 lookup) or the same
        /// mod id under any file name; one renderer per pack. Returns the file names it added.</summary>
        public static async Task<List<string>> InstallFpsBoostAsync(InstalledPack pack, IProgress<InstallProgress>? log,
            string? instanceDir = null)
        {
            var groups = pack.Loader switch
            {
                "forge" => FpsModsForge,
                "neoforge" => FpsModsNeoForge,
                _ => FpsModsFabric
            };
            string[] loaders = ModResolver.LoadersFor(pack.Loader);   // Quilt runs Fabric mods

            string inst = string.IsNullOrEmpty(instanceDir) ? pack.InstanceDir : instanceDir;
            string modsDir = Path.Combine(inst, "mods");
            Directory.CreateDirectory(modsDir);

            log?.Report(new("Буст FPS: проверяю моды сборки…", 0, groups.Length));
            var installed = ModScanner.Scan(modsDir);
            try { await ModScanner.ResolveProjectsAsync(installed); }
            catch { /* offline lookup: mod-id matching below still catches copies */ }
            var haveProjects = new HashSet<string>(installed.Where(m => m.ProjectId != null).Select(m => m.ProjectId!));
            var haveIds = new HashSet<string>(installed.SelectMany(m => m.ModIds), StringComparer.OrdinalIgnoreCase);
            bool haveRenderer = installed.Any(m => m.ModIds.Overlaps(RendererIds)
                                                   || m.FileName.Contains("optifine", StringComparison.OrdinalIgnoreCase));

            var added = new List<string>();
            int i = 0, netErrors = 0;
            string tmpDir = Path.Combine(inst, ".blockify", "tmp-fps");
            try
            {
                foreach (var group in groups)
                {
                    log?.Report(new("Буст FPS: " + group[0], ++i, groups.Length));
                    bool renderer = group.Any(RendererSlugs.Contains);
                    if (renderer && haveRenderer) continue;

                    // resolve every alternative first: if the pack already has any of them, the group is covered
                    var cands = new List<JToken>();
                    foreach (var slug in group)
                    {
                        try
                        {
                            var v = await ModrinthService.GetBestVersionAsync(slug, loaders, pack.McVersion);
                            if (v != null) cands.Add(v);
                        }
                        catch { netErrors++; }
                    }
                    if (cands.Any(v => haveProjects.Contains(v.Value<string>("project_id") ?? ""))) continue;

                    var pick = cands.FirstOrDefault();
                    var file = ModrinthService.PrimaryFileWithHash(pick);
                    if (file == null) continue;
                    string name = ModScanner.SafeFileName(file.Value.FileName);
                    string dest = Path.Combine(modsDir, name);
                    if (File.Exists(dest) || File.Exists(dest + ".disabled")) continue;   // pack already ships it

                    string tmp = Path.Combine(tmpDir, name);
                    try { await BlockifyHttp.DownloadToFileAsync(file.Value.Url, tmp, file.Value.Sha1); }
                    catch { netErrors++; continue; }

                    // same mod under another file name / from another site — keep the pack's copy
                    var ids = ModScanner.ReadModIds(tmp);
                    if (ids.Overlaps(haveIds)) { try { File.Delete(tmp); } catch { } continue; }

                    File.Move(tmp, dest);
                    added.Add(name);
                    haveIds.UnionWith(ids);
                    if (renderer) haveRenderer = true;
                }
            }
            finally
            {
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true); } catch { }
            }

            if (added.Count == 0 && netErrors > 0)
                throw new InvalidOperationException("Нет связи с Modrinth — моды буста не скачались. Проверь интернет и попробуй ещё раз.");
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
