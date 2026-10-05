using BlockifyLauncher.Core.Net;
using Newtonsoft.Json.Linq;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BlockifyLauncher.Core.Modrinth
{
    /// <summary>A jar in a mods folder: hash, Modrinth project (when known) and the mod ids it declares.</summary>
    public class InstalledMod
    {
        public string Path = "";
        public string FileName = "";
        public bool Enabled;
        public string Sha1 = "";
        public string? ProjectId;
        public HashSet<string> ModIds = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads what is actually installed: mod ids from fabric.mod.json / quilt.mod.json /
    /// mods.toml / neoforge.mods.toml / mcmod.info, and Modrinth projects by sha1. Used to tell
    /// "same mod, other file name" apart, so installs replace instead of duplicating.
    /// </summary>
    public static class ModScanner
    {
        // the game and loaders themselves — never treated as a mod identity
        private static readonly HashSet<string> PlatformIds = new(StringComparer.OrdinalIgnoreCase)
            { "minecraft", "java", "forge", "neoforge", "fml", "javafml", "lowcodefml", "mcp",
              "fabricloader", "fabric-loader", "quilt_loader", "quilt-loader" };

        private static readonly Regex ModIdRx = new(@"^modId\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        private static readonly Regex JsonIdRx = new(@"""id""\s*:\s*""([^""]+)""");

        /// <summary>Enabled (.jar) and disabled (.jar.disabled) jars of a mods folder.</summary>
        public static List<InstalledMod> Scan(string modsDir, bool hash = true)
        {
            var list = new List<InstalledMod>();
            if (!Directory.Exists(modsDir)) return list;
            foreach (var f in Directory.GetFiles(modsDir))
            {
                string name = System.IO.Path.GetFileName(f);
                bool enabled = name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                bool disabled = name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
                if (!enabled && !disabled) continue;
                var m = new InstalledMod { Path = f, FileName = name, Enabled = enabled, ModIds = ReadModIds(f) };
                if (hash) { try { m.Sha1 = Sha1File(f); } catch { } }
                list.Add(m);
            }
            return list;
        }

        /// <summary>Fills <see cref="InstalledMod.ProjectId"/> via one /version_files lookup (network errors throw).</summary>
        public static async Task ResolveProjectsAsync(List<InstalledMod> mods)
        {
            var hashes = mods.Where(m => m.Sha1.Length > 0).Select(m => m.Sha1).Distinct().ToList();
            if (hashes.Count == 0) return;
            var map = await ModrinthService.LookupHashesAsync(hashes);
            foreach (var m in mods)
                if (m.Sha1.Length > 0 && map[m.Sha1] is JToken v)
                    m.ProjectId = v.Value<string>("project_id");
        }

        public static HashSet<string> ReadModIds(string jarPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(jarPath);
                return ReadModIds(zip);
            }
            catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        }

        /// <summary>Mod ids a jar declares at top level (not its jar-in-jar libraries).</summary>
        public static HashSet<string> ReadModIds(ZipArchive zip)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? id) { if (!string.IsNullOrWhiteSpace(id) && !PlatformIds.Contains(id)) ids.Add(id.Trim()); }

            if (zip.GetEntry("fabric.mod.json") is { } fab)
            {
                string text = ReadText(fab);
                if (TryParse(text) is JObject j) Add(j.Value<string>("id"));
                else if (JsonIdRx.Match(text) is { Success: true } rm) Add(rm.Groups[1].Value);   // lenient fallback
            }
            if (zip.GetEntry("quilt.mod.json") is { } qu && TryParse(ReadText(qu)) is JObject q)
                Add(q["quilt_loader"]?.Value<string>("id"));
            foreach (var tomlName in new[] { "META-INF/mods.toml", "META-INF/neoforge.mods.toml" })
                if (zip.GetEntry(tomlName) is { } toml)
                    foreach (var id in TomlModIds(ReadText(toml))) Add(id);
            if (zip.GetEntry("mcmod.info") is { } mi)
            {
                var t = TryParse(ReadText(mi));
                var arr = t as JArray ?? (t as JObject)?["modList"] as JArray;
                foreach (var entry in arr ?? new JArray())
                    if (entry is JObject mo) Add(mo.Value<string>("modid"));
            }
            return ids;
        }

        /// <summary>Mod ids of the libraries a jar ships inside itself (Fabric "jars", Forge/NeoForge jarjar).</summary>
        public static HashSet<string> ReadEmbeddedModIds(string jarPath)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var zip = ZipFile.OpenRead(jarPath);
                var paths = new List<string>();
                if (zip.GetEntry("fabric.mod.json") is { } fab && TryParse(ReadText(fab)) is JObject j)
                    foreach (var x in j["jars"] as JArray ?? new JArray())
                        if (x is JObject xo && xo.Value<string>("file") is string f) paths.Add(f);
                if (zip.GetEntry("quilt.mod.json") is { } qu && TryParse(ReadText(qu)) is JObject q)
                    foreach (var x in q["quilt_loader"]?["jars"] as JArray ?? new JArray())
                        if (x.Type == JTokenType.String) paths.Add((string)x!);
                if (zip.GetEntry("META-INF/jarjar/metadata.json") is { } jj && TryParse(ReadText(jj)) is JObject meta)
                    foreach (var x in meta["jars"] as JArray ?? new JArray())
                        if (x is JObject xo && xo.Value<string>("path") is string p) paths.Add(p);

                foreach (var p in paths.Distinct().Take(64))
                {
                    var e = zip.GetEntry(p.TrimStart('/'));
                    if (e == null || e.Length > 32L * 1024 * 1024) continue;
                    try
                    {
                        using var ms = new MemoryStream();
                        using (var s = e.Open()) s.CopyTo(ms);
                        ms.Position = 0;
                        using var inner = new ZipArchive(ms, ZipArchiveMode.Read);
                        ids.UnionWith(ReadModIds(inner));
                    }
                    catch { }
                }
            }
            catch { }
            return ids;
        }

        // modId lines inside [[mods]] tables only — [[dependencies.x]] tables carry modId too
        private static IEnumerable<string> TomlModIds(string toml)
        {
            bool inMods = false;
            foreach (var raw in toml.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("["))
                {
                    inMods = line.Replace(" ", "").StartsWith("[[mods]]");
                    continue;
                }
                if (!inMods) continue;
                var m = ModIdRx.Match(line);
                if (m.Success) yield return m.Groups[1].Value;
            }
        }

        private static string ReadText(ZipArchiveEntry e)
        {
            if (e.Length > 1024 * 1024) return "";
            using var r = new StreamReader(e.Open());
            return r.ReadToEnd();
        }

        private static JToken? TryParse(string text)
        {
            try { return string.IsNullOrWhiteSpace(text) ? null : JToken.Parse(text); }
            catch { return null; }
        }

        public static string Sha1File(string path)
        {
            using var s = System.Security.Cryptography.SHA1.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(s.ComputeHash(fs)).ToLowerInvariant();
        }

        /// <summary>A single, harmless file name (no folders, no ".." / ":" / invalid chars).</summary>
        public static string SafeFileName(string name)
        {
            string n = System.IO.Path.GetFileName(name ?? "").TrimEnd('.', ' ');
            if (n.Length == 0 || n.Contains(':') || n.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return "_invalid_";
            return n;
        }

        // "cloth-config" / "cloth_config" / "ClothConfig" compare equal
        public static string NormId(string s) => (s ?? "").ToLowerInvariant().Replace("-", "").Replace("_", "");
    }

    /// <summary>What an install / update actually changed.</summary>
    public class ModInstallResult
    {
        public List<string> Added = new();      // jars written
        public List<string> Replaced = new();   // older copies removed (same project or same mod id)
        public List<string> Enabled = new();    // switched-off dependencies switched back on
        public List<string> Embedded = new();   // dependencies skipped because another jar ships them
        public bool AlreadyInstalled;           // the requested file was already there

        public string Summary()
        {
            var parts = new List<string>();
            if (AlreadyInstalled && Added.Count == 0) parts.Add("уже установлен");
            if (Added.Count > 0) parts.Add("добавлено: " + string.Join(", ", Added));
            if (Replaced.Count > 0) parts.Add("заменено: " + string.Join(", ", Replaced));
            if (Enabled.Count > 0) parts.Add("включено: " + string.Join(", ", Enabled));
            return parts.Count > 0 ? string.Join("; ", parts) : "без изменений";
        }
    }

    /// <summary>
    /// Installs a mod version with its required dependencies (recursively, release-first,
    /// for the pack's loaders + game version). Already-installed projects are detected by
    /// sha1 → Modrinth project and by declared mod id, so a new version replaces the old jar
    /// instead of sitting next to it. Everything is downloaded and verified into a temp folder
    /// first; if a required dependency can't be found nothing in mods/ changes.
    /// </summary>
    public static class ModResolver
    {
        private const int MaxDepth = 6;
        private const int MaxItems = 60;

        /// <summary>Loaders a pack can use mods for (Quilt runs Fabric mods).</summary>
        public static string[] LoadersFor(string packLoader)
            => packLoader == "quilt" ? new[] { "quilt", "fabric" } : new[] { packLoader };

        /// <param name="replaceFile">update: the file being replaced (removed even if it can't be matched otherwise)</param>
        public static async Task<ModInstallResult> InstallAsync(string modsDir, JToken rootVersion, string[] loaders,
            string? gameVersion, IProgress<InstallProgress>? log = null, string? replaceFile = null)
        {
            Directory.CreateDirectory(modsDir);
            var res = new ModInstallResult();

            log?.Report(new("Проверяю установленные моды…", 0, 0));
            var installed = ModScanner.Scan(modsDir);
            try { await ModScanner.ResolveProjectsAsync(installed); }
            catch { /* lookup failed: mod-id matching below still prevents duplicates */ }

            // 1. plan: requested version + required dependencies, breadth-first
            string rootPid = rootVersion.Value<string>("project_id") ?? "";
            var plan = new List<JToken> { rootVersion };
            var visited = new HashSet<string>(StringComparer.Ordinal) { rootPid };
            var enable = new List<InstalledMod>();
            var missing = new List<string>();
            var queue = new Queue<(JToken ver, int depth)>();
            queue.Enqueue((rootVersion, 0));
            while (queue.Count > 0)
            {
                var (ver, depth) = queue.Dequeue();
                foreach (var d in ver["dependencies"] as JArray ?? new JArray())
                {
                    if (d.Value<string>("dependency_type") != "required") continue;
                    string? pid = d.Value<string>("project_id");
                    string? vid = d.Value<string>("version_id");
                    JToken? dv = null;
                    if (string.IsNullOrEmpty(pid) && !string.IsNullOrEmpty(vid))
                    {
                        dv = await ModrinthService.GetVersionAsync(vid);
                        pid = dv?.Value<string>("project_id");
                    }
                    if (string.IsNullOrEmpty(pid) || !visited.Add(pid)) continue;

                    var have = installed.Where(m => m.ProjectId == pid).ToList();
                    if (have.Count > 0)
                    {
                        // already in the pack; a switched-off library is switched back on
                        if (!have.Any(m => m.Enabled))
                            enable.Add(have.OrderByDescending(m => File.GetLastWriteTimeUtc(m.Path)).First());
                        continue;
                    }

                    log?.Report(new("Ищу зависимости…", plan.Count, 0));
                    if (dv == null && !string.IsNullOrEmpty(vid)) dv = await ModrinthService.GetVersionAsync(vid);
                    // a pinned version for another loader / MC is replaced by the best fitting one
                    if (dv != null && !ModrinthService.Fits(dv, loaders, gameVersion)) dv = null;
                    dv ??= await ModrinthService.GetBestVersionAsync(pid, loaders, gameVersion);
                    if (dv == null) { missing.Add(pid); continue; }
                    if (plan.Count >= MaxItems) continue;
                    plan.Add(dv);
                    if (depth + 1 < MaxDepth) queue.Enqueue((dv, depth + 1));
                }
            }

            // 2. download + verify everything into a temp folder next to mods/
            string tmpDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(modsDir))!,
                ".blockify", "tmp-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tmpDir);
            try
            {
                var staged = new List<(JToken ver, bool root, string tmp, string name, HashSet<string> ids)>();
                int i = 0;
                foreach (var v in plan)
                {
                    bool isRoot = ReferenceEquals(v, rootVersion);
                    var f = ModrinthService.PrimaryFileWithHash(v);
                    if (f == null)
                    {
                        if (isRoot) throw new InvalidOperationException("У этой версии мода нет файла для скачивания.");
                        continue;
                    }
                    // the very same file is already in mods/ (maybe switched off)
                    var same = f.Value.Sha1 != null ? installed.FirstOrDefault(m => m.Sha1 == f.Value.Sha1) : null;
                    if (same != null)
                    {
                        if (isRoot) res.AlreadyInstalled = true;
                        if (!same.Enabled && !enable.Contains(same)) enable.Add(same);
                        continue;
                    }
                    string name = ModScanner.SafeFileName(f.Value.FileName);
                    log?.Report(new("Скачиваю " + name, ++i, plan.Count));
                    string tmp = System.IO.Path.Combine(tmpDir, name);
                    await BlockifyHttp.DownloadToFileAsync(f.Value.Url, tmp, f.Value.Sha1);
                    staged.Add((v, isRoot, tmp, name, ModScanner.ReadModIds(tmp)));
                }

                // 3. drop dependencies another jar already ships (jar-in-jar) or that are installed
                //    under another project (e.g. a CurseForge copy with the same mod id)
                var embedded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var st in staged) embedded.UnionWith(ModScanner.ReadEmbeddedModIds(st.tmp));
                var installedIds = new HashSet<string>(installed.SelectMany(m => m.ModIds), StringComparer.OrdinalIgnoreCase);
                foreach (var dep in staged.Where(x => !x.root).ToList())
                {
                    if (dep.ids.Count > 0 && (dep.ids.All(embedded.Contains) || dep.ids.Overlaps(installedIds)))
                    {
                        staged.Remove(dep);
                        res.Embedded.Add(dep.name);
                        try { File.Delete(dep.tmp); } catch { }
                    }
                }

                // a required dependency Modrinth has no fitting version of: fine only if something ships it
                if (missing.Count > 0)
                {
                    Dictionary<string, (string Slug, string Title)> info;
                    try { info = await ModrinthService.GetProjectsAsync(missing); }
                    catch { info = new(); }
                    var known = new HashSet<string>(embedded.Concat(installedIds).Concat(staged.SelectMany(x => x.ids))
                        .Select(ModScanner.NormId));
                    var still = missing.Where(id => !(info.TryGetValue(id, out var pr) && known.Contains(ModScanner.NormId(pr.Slug))))
                        .Select(id => info.TryGetValue(id, out var pr) ? pr.Title : id)
                        .ToList();
                    if (still.Count > 0)
                        throw new InvalidOperationException(
                            $"Нет версии обязательной зависимости под {string.Join("/", loaders)} {gameVersion}: " +
                            string.Join(", ", still) + ". Ничего не установлено.");
                }

                // 4. commit: park older copies of the same mod as *.blockify-old, move the new jars in;
                //    on any failure the old jars go back, so mods/ never ends up with a duplicate or a gap
                log?.Report(new("Устанавливаю…", 0, 0));
                var parked = new List<(string from, string bak)>();
                try
                {
                    foreach (var s in staged)
                    {
                        string pid = s.ver.Value<string>("project_id") ?? "";
                        var old = installed.Where(m =>
                                (pid.Length > 0 && m.ProjectId == pid)
                                || (s.ids.Count > 0 && m.ModIds.Overlaps(s.ids))
                                || (s.root && replaceFile != null && string.Equals(m.FileName, replaceFile, StringComparison.OrdinalIgnoreCase)))
                            .ToList();
                        foreach (var om in old)
                        {
                            if (!File.Exists(om.Path)) continue;
                            string bak = om.Path + ".blockify-old";
                            try { File.Move(om.Path, bak, overwrite: true); }
                            catch (IOException ex)
                            {
                                throw new IOException($"Не удалось заменить {om.FileName}: файл занят (игра запущена?).", ex);
                            }
                            parked.Add((om.Path, bak));
                            res.Replaced.Add(om.FileName);
                        }
                        installed.RemoveAll(old.Contains);
                        File.Move(s.tmp, System.IO.Path.Combine(modsDir, s.name), overwrite: true);
                        res.Added.Add(s.name);
                    }
                }
                catch
                {
                    foreach (var name in res.Added)
                        try { File.Delete(System.IO.Path.Combine(modsDir, name)); } catch { }
                    foreach (var (from, bak) in parked)
                        try { if (!File.Exists(from)) File.Move(bak, from); } catch { }
                    throw;
                }
                foreach (var (_, bak) in parked)
                    try { File.Delete(bak); } catch { }

                foreach (var em in enable)
                {
                    if (em.Enabled || !File.Exists(em.Path)) continue;
                    string target = em.Path[..^".disabled".Length];
                    if (File.Exists(target)) continue;
                    File.Move(em.Path, target);
                    res.Enabled.Add(System.IO.Path.GetFileName(target));
                }
                return res;
            }
            finally
            {
                try { Directory.Delete(tmpDir, true); } catch { }
            }
        }
    }
}
