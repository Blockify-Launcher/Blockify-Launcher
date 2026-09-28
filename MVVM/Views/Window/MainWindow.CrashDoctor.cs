using BlockifyLauncher.Core.Modrinth;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace BlockifyLauncher
{
    /// <summary>
    /// Crash Doctor: reads a pack's newest crash report + latest.log, maps stack frames and
    /// loader messages back to the mod jars in the instance, and proposes one-click fixes
    /// (disable the culprit, install a missing dependency, raise RAM).
    /// </summary>
    public partial class MainWindow
    {
        private sealed class ModJar
        {
            public string File = "";
            public string Id = "";
            public string Name = "";
            public HashSet<string> Packages = new();
        }

        private sealed class Fix { public string kind = ""; public string label = ""; public string file = ""; public string slug = ""; public int ram; }
        private sealed class Finding { public string title = ""; public string detail = ""; public List<Fix> fixes = new(); }

        // frames from these namespaces are never "the culprit"
        private static readonly string[] SkipPkg =
        {
            "java.", "javax.", "jdk.", "sun.", "net.minecraft.", "com.mojang.", "net.fabricmc.", "org.spongepowered.",
            "org.quiltmc.", "cpw.mods.", "net.minecraftforge.", "net.neoforged.", "kotlin.", "kotlinx.", "org.lwjgl.",
            "io.netty.", "com.google.", "org.apache.", "it.unimi.", "org.joml.", "com.llamalad7.", "org.objectweb."
        };

        // ── jar index: mod id / name / package prefixes for every jar in the instance ──
        private static List<ModJar> ScanModJars(string modsDir)
        {
            var list = new List<ModJar>();
            if (!Directory.Exists(modsDir)) return list;
            foreach (var f in Directory.GetFiles(modsDir, "*.jar"))
            {
                var mj = new ModJar { File = Path.GetFileName(f) };
                try
                {
                    using var zip = ZipFile.OpenRead(f);
                    foreach (var e in zip.Entries)
                    {
                        string n = e.FullName;
                        if (n.EndsWith(".class", StringComparison.Ordinal) && !n.StartsWith("META-INF/", StringComparison.Ordinal))
                        {
                            var parts = n.Split('/');
                            int pkgLen = parts.Length - 1;                  // drop the class file itself
                            if (pkgLen >= 3) mj.Packages.Add(string.Join('.', parts, 0, 3));
                            else if (pkgLen >= 1) mj.Packages.Add(string.Join('.', parts, 0, pkgLen));
                        }
                    }
                    var fab = zip.GetEntry("fabric.mod.json") ?? zip.GetEntry("quilt.mod.json");
                    if (fab != null)
                    {
                        using var r = new StreamReader(fab.Open());
                        var j = Newtonsoft.Json.Linq.JObject.Parse(r.ReadToEnd());
                        mj.Id = j.Value<string>("id") ?? j["quilt_loader"]?.Value<string>("id") ?? "";
                        mj.Name = j.Value<string>("name") ?? j["quilt_loader"]?["metadata"]?.Value<string>("name") ?? "";
                    }
                    var toml = zip.GetEntry("META-INF/mods.toml") ?? zip.GetEntry("META-INF/neoforge.mods.toml");
                    if (toml != null && mj.Id.Length == 0)
                    {
                        using var r = new StreamReader(toml.Open());
                        string t = r.ReadToEnd();
                        mj.Id = Regex.Match(t, "modId\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
                        mj.Name = Regex.Match(t, "displayName\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
                    }
                }
                catch { }
                if (mj.Name.Length == 0) mj.Name = Path.GetFileNameWithoutExtension(mj.File);
                list.Add(mj);
            }
            return list;
        }

        private static ModJar? JarForClass(List<ModJar> jars, string fqcn)
        {
            if (SkipPkg.Any(p => fqcn.StartsWith(p, StringComparison.Ordinal))) return null;
            ModJar? best = null; int bestLen = 0;
            foreach (var j in jars)
                foreach (var p in j.Packages)
                    if ((fqcn == p || fqcn.StartsWith(p + ".", StringComparison.Ordinal)) && p.Length > bestLen) { best = j; bestLen = p.Length; }
            return best;
        }

        private static ModJar? JarForId(List<ModJar> jars, string id)
            => jars.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? jars.FirstOrDefault(j => j.File.StartsWith(id, StringComparison.OrdinalIgnoreCase));

        private static Fix Disable(ModJar j) => new() { kind = "disable", file = j.File, label = $"Отключить «{j.Name}»" };

        // ── the diagnosis itself (runs off the UI thread) ──
        private object Diagnose(InstalledPack pack)
        {
            string crashDir = Path.Combine(pack.InstanceDir, "crash-reports");
            string latestLog = Path.Combine(pack.InstanceDir, "logs", "latest.log");
            FileInfo? crash = Directory.Exists(crashDir)
                ? new DirectoryInfo(crashDir).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault()
                : null;

            string text = "";
            if (crash != null) text = SafeRead(crash.FullName, 1_500_000);
            if (File.Exists(latestLog)) text += "\n" + SafeRead(latestLog, 1_500_000);
            if (text.Trim().Length == 0)
                return new { found = false, headline = "Логов нет — запусти сборку хотя бы раз." };

            var jars = ScanModJars(Path.Combine(pack.InstanceDir, "mods"));
            var findings = new List<Finding>();
            var seenFiles = new HashSet<string>();

            // 1. Fabric/Quilt dependency resolver messages (latest.log / crash-report)
            foreach (Match m in Regex.Matches(text, @"Mod '([^']+)' \(([^)]+)\)[^\n]*? requires [^\n]*? of ([a-z0-9_\-]+), which is missing"))
            {
                string needer = m.Groups[1].Value, neederId = m.Groups[2].Value, dep = m.Groups[3].Value;
                var f = new Finding
                {
                    title = $"Не хватает зависимости «{dep}»",
                    detail = $"Мод «{needer}» ({neederId}) требует {dep}, а его нет в сборке. Обычно достаточно доустановить.",
                    fixes = { new Fix { kind = "install", slug = dep, label = $"Установить {dep}" } }
                };
                var nj = JarForId(jars, neederId);
                if (nj != null) f.fixes.Add(Disable(nj));
                findings.Add(f);
            }
            foreach (Match m in Regex.Matches(text, @"Mod '([^']+)' \(([^)]+)\)[^\n]*? requires [^\n]*? of 'Minecraft' \(minecraft\), but only the wrong version is present: ([^\n!]+)"))
            {
                var j = JarForId(jars, m.Groups[2].Value);
                if (j == null || !seenFiles.Add(j.File)) continue;
                findings.Add(new Finding
                {
                    title = $"«{m.Groups[1].Value}» — не для этой версии игры",
                    detail = $"Мод собран под другую версию Minecraft, а запущена {m.Groups[3].Value.Trim()}. Его нужно отключить или заменить версией под {pack.McVersion}.",
                    fixes = { Disable(j) }
                });
            }

            // 2. Mixin failures name the mod directly
            foreach (Match m in Regex.Matches(text, @"Mixin apply for mod ([a-z0-9_\-]+) failed"))
            {
                var j = JarForId(jars, m.Groups[1].Value);
                if (j == null || !seenFiles.Add(j.File)) continue;
                findings.Add(new Finding
                {
                    title = $"«{j.Name}» ломает загрузку (mixin)",
                    detail = "Мод пытается изменить код игры или другого мода и не совпадает по версии. Отключи его или обнови.",
                    fixes = { Disable(j) }
                });
            }

            // 2b. Forge/NeoForge dependency errors:
            //   Mod ID: 'jei', Requested by: 'jeresources', Expected range: '[15,)', Actual version: '[MISSING]'
            foreach (Match m in Regex.Matches(text, @"Mod ID: '([^']+)', Requested by: '([^']+)', Expected range: '([^']*)', Actual version: '([^']*)'"))
            {
                string dep = m.Groups[1].Value, by = m.Groups[2].Value, want = m.Groups[3].Value, have = m.Groups[4].Value;
                var byJar = JarForId(jars, by);
                var f = new Finding
                {
                    title = have.Contains("MISSING") ? $"Не хватает зависимости «{dep}»" : $"«{dep}» не той версии",
                    detail = have.Contains("MISSING")
                        ? $"Мод «{by}» требует {dep} {want}, а его нет в сборке."
                        : $"Мод «{by}» требует {dep} {want}, а стоит {have}. Обнови {dep} или отключи {by}.",
                    fixes = { new Fix { kind = "install", slug = dep, label = have.Contains("MISSING") ? $"Установить {dep}" : $"Обновить {dep}" } }
                };
                if (byJar != null) f.fixes.Add(Disable(byJar));
                findings.Add(f);
            }

            // 3. Out of memory
            if (text.Contains("OutOfMemoryError"))
            {
                int cur = pack.RamMb > 0 ? pack.RamMb : setting.GetMemoryRAM();
                int next = Math.Min(cur + 2048, 16384);
                findings.Add(new Finding
                {
                    title = "Игре не хватило памяти",
                    detail = $"Сейчас выделено {cur / 1024} ГБ. Для сборки с {jars.Count} модами обычно нужно {Math.Max(6, next / 1024)}+ ГБ.",
                    fixes = { new Fix { kind = "ram", ram = next, label = $"Выделить {next / 1024} ГБ" } }
                });
            }

            // 4. Java errors: the first exception after "Description:" (crash report) or anywhere (log),
            //    then the first stack frame that belongs to a mod jar
            int from = Math.Max(0, text.IndexOf("Description:", StringComparison.Ordinal));
            var exc = Regex.Match(text.Substring(from), @"\b((?:[a-z][\w]*\.)+[A-Z][\w$]*(?:Exception|Error))(?::\s*([^\r\n]*))?");
            string headline = crash != null ? Path.GetFileName(crash.Name) : "latest.log";
            if (exc.Success)
            {
                string kind = exc.Groups[1].Value, msg = exc.Groups[2].Value.Trim();
                kind = kind[(kind.LastIndexOf('.') + 1)..];
                headline = kind + (msg.Length > 0 ? ": " + Trunc(msg, 90) : "");
                string block = text.Substring(from + exc.Index);
                int end = block.IndexOf("\n\n", StringComparison.Ordinal);
                if (end > 0) block = block[..end];                         // just this stack trace

                ModJar? culprit = null;
                // Forge/NeoForge frames carry the jar name: "~[endofdays-2.5.jar%23360!/:…]"
                foreach (Match fr in Regex.Matches(block, @"~\[([^\]%!/\r\n]+\.jar)"))
                {
                    var j = jars.FirstOrDefault(x => string.Equals(x.File, fr.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                    if (j != null) { culprit = j; break; }
                }
                // Fabric/Quilt frames don't — map the class package to a jar instead
                if (culprit == null)
                    foreach (Match fr in Regex.Matches(block, @"\bat (?:knot//|TRANSFORMER/[^/]+/|MC-BOOTSTRAP/[^/]+/)?([\w.$]+)\.[\w$<>]+\("))
                    {
                        culprit = JarForClass(jars, fr.Groups[1].Value);
                        if (culprit != null) break;
                    }
                if (culprit != null && seenFiles.Add(culprit.File))
                {
                    bool init = text.Contains("Initializing game") || text.Contains("Loading mods") || kind == "ExceptionInInitializerError";
                    string why = kind is "NoSuchMethodError" or "NoClassDefFoundError" or "NoSuchFieldError" or "AbstractMethodError"
                        ? $"Мод обращается к коду, которого нет в текущих версиях игры или библиотек ({Trunc(msg, 60)}). Типичный конфликт версий — например, библиотека kotlin/coroutines обновилась, а мод под неё нет."
                        : init
                            ? "Мод упал при загрузке игры. Обычно помогает отключить его или обновить до версии под текущий набор модов."
                            : $"Мод выбросил ошибку уже в игре ({kind}: {Trunc(msg, 60)}). Если это ключевой мод сборки — сначала попробуй обновить его через «Моды», отключение оставь на крайний случай.";
                    findings.Add(new Finding { title = $"Виновник — «{culprit.Name}»", detail = why, fixes = { Disable(culprit) } });
                }
                else if (findings.Count == 0)
                {
                    findings.Add(new Finding
                    {
                        title = "Ошибка вне модов",
                        detail = $"{kind}: {Trunc(msg, 160)}. Стек не указывает на конкретный мод — возможно, проблема в драйверах, Java или файлах игры. Помогает «Починить» у сборки (проверка файлов).",
                        fixes = { new Fix { kind = "repair", label = "Починить сборку" } }
                    });
                }
            }

            if (findings.Count == 0)
                findings.Add(new Finding
                {
                    title = "Явной причины не нашёл",
                    detail = "Ни одно правило не сработало. Открой лог и отправь автору сборки — или пришли мне отчёт, добавлю правило.",
                    fixes = { new Fix { kind = "openLogs", label = "Открыть папку логов" } }
                });

            return new
            {
                found = true,
                file = crash?.Name ?? "latest.log",
                when = (crash?.LastWriteTime ?? File.GetLastWriteTime(latestLog)).ToString("dd.MM.yyyy HH:mm"),
                headline,
                mods = jars.Count,
                items = findings.Select(f => new { f.title, f.detail, fixes = f.fixes.Select(x => new { x.kind, x.label, x.file, x.slug, x.ram }) })
            };
        }

        private static string SafeRead(string path, int maxBytes)
        {
            try
            {
                var fi = new FileInfo(path);
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fi.Length > maxBytes) fs.Seek(fi.Length - maxBytes, SeekOrigin.Begin);   // tail of huge logs
                using var r = new StreamReader(fs);
                return r.ReadToEnd();
            }
            catch { return ""; }
        }

        private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

        // ── entry points ──
        private async Task DiagnosePackAsync(string slug, bool auto)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            object result;
            try { result = await Task.Run(() => Diagnose(pack)); }
            catch (Exception ex) { result = new { found = false, headline = "Не смог прочитать логи: " + ex.Message }; }
            Post(new { type = "crashDoctor", slug, packTitle = pack.Title, auto, result });
        }

        // called when a pack's game process exits while the launcher is still open
        private void OnPackExited(string slug, DateTime launchedAt, int exitCode)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            string crashDir = Path.Combine(pack.InstanceDir, "crash-reports");
            bool newCrash = Directory.Exists(crashDir) &&
                new DirectoryInfo(crashDir).GetFiles("*.txt").Any(f => f.LastWriteTime >= launchedAt.AddSeconds(-5));
            LogDiag($"pack '{slug}' exited code={exitCode} newCrash={newCrash}");
            if (exitCode != 0 || newCrash) _ = DiagnosePackAsync(slug, auto: true);
        }

        // ── fixes ──
        private async Task ApplyCrashFixAsync(string slug, string kind, string file, string modSlug, int ram)
        {
            var list = LoadPacks();
            var pack = list.FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            try
            {
                if (kind is "disable" or "install") await AutoSnapshotAsync(pack, "перед фиксом Crash Doctor");
                switch (kind)
                {
                    case "disable":
                    {
                        string p = Path.Combine(pack.InstanceDir, "mods", SafeName(file));
                        if (File.Exists(p) && !File.Exists(p + ".disabled")) File.Move(p, p + ".disabled");
                        break;
                    }
                    case "install":
                    {
                        var proj = await ModrinthService.GetProjectAsync(modSlug);
                        if (proj == null) throw new Exception($"Не нашёл «{modSlug}» на Modrinth — поставь вручную.");
                        await InstallContentAsync(proj.Value.Slug, proj.Value.Title, "mod", slug);
                        break;
                    }
                    case "ram":
                        pack.RamMb = Math.Max(1024, ram);
                        SavePacks(list);
                        PushInstalledPacks();
                        break;
                    case "repair":
                        await ReinstallPack(slug);
                        break;
                    case "openLogs":
                        OpenPath(Path.Combine(pack.InstanceDir, "logs"));
                        break;
                }
                Post(new { type = "crashFixDone", slug, kind, file, ok = true });
            }
            catch (Exception ex)
            {
                Post(new { type = "crashFixDone", slug, kind, file, ok = false, error = ex.Message });
            }
        }
    }
}
