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

        // "java.lang.Foo(Exception|Error): message"
        private static readonly Regex ExcRx = new(@"\b((?:[a-z][\w]*\.)+[A-Z][\w$]*(?:Exception|Error))(?::\s*([^\r\n]*))?", RegexOptions.Compiled);
        // start of a log entry: "[12:34:56] ..." (vanilla/Fabric) or "[28Sep2026 12:34:56.789] ..." (Forge/NeoForge)
        private static readonly Regex LogEntryRx = new(@"^\[[^\]\n]*\d{1,2}:\d{2}:\d{2}[^\]\n]*\]", RegexOptions.Compiled);
        private static readonly Regex LogErrRx = new(@"^\[[^\]\n]*\d{1,2}:\d{2}:\d{2}[^\]\n]*\]\s*\[[^\]\n]*/(ERROR|FATAL)\]", RegexOptions.Compiled);

        // exceptions a healthy session logs all the time (offline auth, update checkers, optional integrations)
        private static readonly string[] BenignExc =
        {
            "java.net.", "javax.net.ssl.", "java.nio.channels.ClosedChannelException", "com.mojang.authlib.",
            "java.lang.InterruptedException", "java.util.concurrent.CancellationException", "java.util.concurrent.TimeoutException",
            "java.lang.ClassNotFoundException", "java.io.FileNotFoundException", "java.nio.file.NoSuchFileException"
        };
        private static readonly string[] BenignMarkers =
            { "realms", "telemetry", "profile key", "authentication", "update check", "updatechecker", "version check" };

        private static bool IsBenignLogError(string header, string excClass)
        {
            if (BenignExc.Any(p => excClass.StartsWith(p, StringComparison.Ordinal))) return true;
            string h = header.ToLowerInvariant();
            return BenignMarkers.Any(m => h.Contains(m, StringComparison.Ordinal));
        }

        // Only a report written during/after the last session counts: an older one belongs to a crash
        // that is already gone (or another setup) and would blame the wrong mod.
        private static FileInfo? PickCrashReport(string crashDir, FileInfo latestLog, DateTime? since)
        {
            FileInfo? newest;
            try
            {
                if (!Directory.Exists(crashDir)) return null;
                newest = new DirectoryInfo(crashDir).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            }
            catch { return null; }
            if (newest == null) return null;

            DateTime threshold;
            if (since is DateTime s) threshold = s.AddSeconds(-5);
            else if (latestLog.Exists)
            {
                // session start from the log's own timestamps (its creation time lies due to NTFS tunneling)
                DateTime? start = null;
                try { start = ParseSessionLog(latestLog)?.start; } catch { }
                threshold = (start ?? latestLog.LastWriteTime.AddHours(-1)).AddMinutes(-1);
            }
            else threshold = DateTime.Now.AddDays(-7);   // no log at all: only a recent report is worth reading
            return newest.LastWriteTime >= threshold ? newest : null;
        }

        // crash report: the exception right after "Description:", up to the first blank line
        private static string? CrashReportBlock(string crashText)
        {
            int from = Math.Max(0, crashText.IndexOf("Description:", StringComparison.Ordinal));
            var m = ExcRx.Match(crashText, from);
            if (!m.Success) return null;
            string block = crashText.Substring(m.Index);
            int end = block.IndexOf("\n\n", StringComparison.Ordinal);
            return end > 0 ? block[..end] : block;
        }

        // log fallback: only ERROR/FATAL entries that carry a stack trace and aren't known noise;
        // the last FATAL wins, otherwise the last ERROR (the crash is usually at the end)
        private static string? LogErrorBlock(string logText)
        {
            var lines = logText.Split('\n');
            string? best = null;
            bool bestFatal = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var em = LogErrRx.Match(lines[i]);
                if (!em.Success) continue;
                int j = i + 1;
                while (j < lines.Length && !LogEntryRx.IsMatch(lines[j])) j++;
                int next = j - 1;
                bool hasTrace = false;
                for (int k = i + 1; k < j && !hasTrace; k++)
                    hasTrace = lines[k].TrimStart().StartsWith("at ", StringComparison.Ordinal);
                if (hasTrace)
                {
                    string block = string.Join("\n", lines, i, j - i);
                    var ex = ExcRx.Match(block);
                    if (ex.Success && !IsBenignLogError(lines[i], ex.Groups[1].Value))
                    {
                        bool fatal = em.Groups[1].Value == "FATAL";
                        if (fatal || !bestFatal) { best = block.Substring(ex.Index); bestFatal = fatal; }
                    }
                }
                i = next;
            }
            return best;
        }

        // first stack frame that belongs to a mod jar
        private static ModJar? FindCulprit(string block, List<ModJar> jars)
        {
            // Forge/NeoForge frames carry the jar name: "~[endofdays-2.5.jar%23360!/:…]"
            foreach (Match fr in Regex.Matches(block, @"~\[([^\]%!/\r\n]+\.jar)"))
            {
                var j = jars.FirstOrDefault(x => string.Equals(x.File, fr.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                if (j != null) return j;
            }
            // Fabric/Quilt frames don't — map the class package to a jar instead
            foreach (Match fr in Regex.Matches(block, @"\bat (?:knot//|TRANSFORMER/[^/]+/|MC-BOOTSTRAP/[^/]+/)?([\w.$]+)\.[\w$<>]+\("))
            {
                var j = JarForClass(jars, fr.Groups[1].Value);
                if (j != null) return j;
            }
            return null;
        }

        private static string ShortExc(string fqcn) => fqcn[(fqcn.LastIndexOf('.') + 1)..];
        private static string Gb(int mb) => (mb / 1024.0).ToString("0.#");

        // ── the diagnosis itself (runs off the UI thread) ──
        // since: when the game was started (known for auto-diagnosis after exit)
        private object Diagnose(InstalledPack pack, DateTime? since = null)
        {
            string inst = PackHome(pack);
            string crashDir = Path.Combine(inst, "crash-reports");
            string latestLog = Path.Combine(inst, "logs", "latest.log");
            var logInfo = new FileInfo(latestLog);
            FileInfo? crash = PickCrashReport(crashDir, logInfo, since);

            // CRLF → LF once, so "blank line" and line splitting work on Windows-written files
            string crashText = crash != null ? SafeRead(crash.FullName, 1_500_000).Replace("\r\n", "\n") : "";
            string logText = logInfo.Exists ? SafeRead(latestLog, 1_500_000).Replace("\r\n", "\n") : "";
            string text = crashText + "\n" + logText;
            if (text.Trim().Length == 0)
                return new { found = false, headline = "Логов нет — запусти сборку хотя бы раз." };

            var jars = ScanModJars(Path.Combine(inst, "mods"));
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
                bool missing = have.Contains("MISSING");
                // the game / loader itself can't be "installed" from the catalog — only the requesting mod can go
                bool platform = PlatformIds.Contains(dep);
                var f = new Finding
                {
                    title = platform ? $"«{by}» не подходит к этой версии {dep}"
                          : missing ? $"Не хватает зависимости «{dep}»" : $"«{dep}» не той версии",
                    detail = platform
                        ? $"Мод «{by}» требует {dep} {want}, а в сборке {have}. Отключи {by} или поставь его версию под эту сборку."
                        : missing
                        ? $"Мод «{by}» требует {dep} {want}, а его нет в сборке."
                        : $"Мод «{by}» требует {dep} {want}, а стоит {have}. Обнови {dep} в «Модах» сборки или отключи {by}.",
                };
                // only a missing dependency gets a one-click install; "update" would drop a second jar next to the old one
                if (missing && !platform) f.fixes.Add(new Fix { kind = "install", slug = dep, label = $"Установить {dep}" });
                if (byJar != null) f.fixes.Add(Disable(byJar));
                findings.Add(f);
            }

            // 3. Out of memory
            if (text.Contains("OutOfMemoryError"))
            {
                int cur = pack.RamMb > 0 ? pack.RamMb : setting.GetMemoryRAM();
                // never suggest more than ~70% of the PC's memory: Windows and the game's native memory need the rest
                long physMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
                int cap = physMb > 0 ? (int)Math.Min(16384, physMb * 7 / 10 / 512 * 512) : 16384;
                int next = Math.Min(cur + 2048, cap);
                var f = new Finding { title = "Игре не хватило памяти" };
                if (next > cur)
                {
                    f.detail = $"Сейчас выделено {Gb(cur)} ГБ. Для сборки с {jars.Count} модами попробуй {Gb(next)} ГБ.";
                    f.fixes.Add(new Fix { kind = "ram", ram = next, label = $"Выделить {Gb(next)} ГБ" });
                }
                else
                    f.detail = $"Сейчас выделено {Gb(cur)} ГБ — это уже предел для этого ПК (всего {Gb((int)physMb)} ГБ). " +
                               "Закрой лишние программы (браузер, Discord) или отключи часть тяжёлых модов.";
                findings.Add(f);
            }

            // 4. Java errors → the culprit jar. The crash report is the source of truth; the log is only
            //    a fallback (ERROR/FATAL entries with a stack trace, known noise skipped), and is ignored
            //    entirely when the session ended normally — the game survived those errors.
            bool fromLog = crash == null;
            bool cleanStop = fromLog && logText.Length > 0
                && logText.Substring(Math.Max(0, logText.Length - 4000)).Contains("Stopping!", StringComparison.Ordinal);
            string? block = !fromLog ? CrashReportBlock(crashText) : cleanStop ? null : LogErrorBlock(logText);
            string headline = crash != null ? crash.Name : "latest.log";
            var exc = block != null ? ExcRx.Match(block) : Match.Empty;
            if (block != null && exc.Success)
            {
                string kindTop = ShortExc(exc.Groups[1].Value), msgTop = exc.Groups[2].Value.Trim();
                headline = kindTop + (msgTop.Length > 0 ? ": " + Trunc(msgTop, 90) : "");

                // the deepest "Caused by" is where it really broke: classify and search that part first
                int ci = block.LastIndexOf("Caused by:", StringComparison.Ordinal);
                string rootPart = ci >= 0 ? block[ci..] : block;
                var rootExc = ci >= 0 ? ExcRx.Match(rootPart) : exc;
                string kind = rootExc.Success ? ShortExc(rootExc.Groups[1].Value) : kindTop;
                string msg = rootExc.Success ? rootExc.Groups[2].Value.Trim() : msgTop;
                ModJar? culprit = FindCulprit(rootPart, jars) ?? (ci >= 0 ? FindCulprit(block, jars) : null);

                // log-only verdicts are a guess: say so instead of naming a "culprit"
                string hedge = fromLog ? "Отчёта о падении нет, вывод сделан только по логу — это предположение, а не точный диагноз. " : "";
                if (culprit != null && seenFiles.Add(culprit.File))
                {
                    bool init = kind == "ExceptionInInitializerError"
                        || (fromLog ? !logText.Contains("Sound engine started", StringComparison.Ordinal)
                                    : Regex.IsMatch(crashText, @"Description: (Initializing game|Mod loading|Loading)"));
                    string why = kind is "NoSuchMethodError" or "NoClassDefFoundError" or "NoSuchFieldError" or "AbstractMethodError"
                        ? $"Мод обращается к коду, которого нет в текущих версиях игры или библиотек ({Trunc(msg, 60)}). Типичный конфликт версий — например, библиотека kotlin/coroutines обновилась, а мод под неё нет."
                        : init
                            ? "Мод упал при загрузке игры. Обычно помогает отключить его или обновить до версии под текущий набор модов."
                            : $"Мод выбросил ошибку уже в игре ({kind}: {Trunc(msg, 60)}). Если это ключевой мод сборки — сначала попробуй обновить его через «Моды», отключение оставь на крайний случай.";
                    findings.Add(new Finding
                    {
                        title = fromLog ? $"Возможный виновник — «{culprit.Name}»" : $"Виновник — «{culprit.Name}»",
                        detail = hedge + why,
                        fixes = { Disable(culprit) }
                    });
                }
                else if (findings.Count == 0)
                {
                    findings.Add(new Finding
                    {
                        title = fromLog ? "Возможная ошибка вне модов" : "Ошибка вне модов",
                        detail = hedge + $"{kind}: {Trunc(msg, 160)}. Стек не указывает на конкретный мод — возможно, проблема в драйверах, Java или файлах игры. Помогает «Починить» у сборки (проверка файлов).",
                        fixes = { new Fix { kind = "repair", label = "Починить сборку" } }
                    });
                }
            }

            if (findings.Count == 0 && cleanStop)
            {
                headline = "Последний запуск без падения";
                findings.Add(new Finding
                {
                    title = "Последний запуск завершился без падения",
                    detail = "Свежего отчёта о падении нет, а лог заканчивается обычным выходом из игры — ошибки в нём игра пережила, они не причина. Если вылет повторится, открой Crash Doctor сразу после него.",
                    fixes = { new Fix { kind = "openLogs", label = "Открыть папку логов" } }
                });
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
                source = fromLog ? "log" : "crash",          // optional: where the verdict comes from
                confidence = fromLog ? "low" : "high",       // optional: the UI may soften log-only verdicts
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
        // since: game start time when known (auto-diagnosis after exit); otherwise taken from latest.log
        private async Task DiagnosePackAsync(string slug, bool auto, DateTime? since = null)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            object result;
            try { result = await Task.Run(() => Diagnose(pack, since)); }
            catch (Exception ex) { result = new { found = false, headline = "Не смог прочитать логи: " + ex.Message }; }
            Post(new { type = "crashDoctor", slug, packTitle = pack.Title, auto, result });
        }

        // called when a pack's game process exits while the launcher is still open
        private void OnPackExited(string slug, DateTime launchedAt, int exitCode)
        {
            var pack = LoadPacks().FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            string crashDir = Path.Combine(PackHome(pack), "crash-reports");
            bool newCrash = false;
            try
            {
                newCrash = Directory.Exists(crashDir) &&
                    new DirectoryInfo(crashDir).GetFiles("*.txt").Any(f => f.LastWriteTime >= launchedAt.AddSeconds(-5));
            }
            catch { }
            LogDiag($"pack '{slug}' exited code={exitCode} newCrash={newCrash}");
            if (exitCode != 0 || newCrash) _ = DiagnosePackAsync(slug, auto: true, since: launchedAt);
        }

        // mod ids that are the game or a loader, not installable mods
        private static readonly HashSet<string> PlatformIds = new(StringComparer.OrdinalIgnoreCase)
            { "minecraft", "forge", "neoforge", "fml", "javafml", "lowcodefml", "java", "fabricloader", "fabric-loader", "quilt_loader" };

        // ── fixes ──
        private async Task ApplyCrashFixAsync(string slug, string kind, string file, string modSlug, int ram)
        {
            var list = LoadPacks();
            var pack = list.FirstOrDefault(p => p.Slug == slug);
            if (pack == null) return;
            string inst = PackHome(pack);
            try
            {
                // changing mods under a running game leaves duplicates / half-applied fixes
                if ((kind is "disable" or "install" or "repair") && await Task.Run(() => IsInstanceRunning(inst)))
                    throw new InvalidOperationException(GameRunningMsg);
                if (kind is "disable" or "install") await AutoSnapshotAsync(pack, "перед фиксом Crash Doctor");
                switch (kind)
                {
                    case "disable":
                    {
                        string p = Path.Combine(inst, "mods", SafeName(file));
                        if (File.Exists(p)) File.Move(p, p + ".disabled", overwrite: true);   // the auto-snapshot keeps any older .disabled copy
                        else if (!File.Exists(p + ".disabled")) throw new FileNotFoundException($"Файл мода «{SafeName(file)}» не найден — возможно, его уже удалили.");
                        break;
                    }
                    case "install":
                    {
                        if (PlatformIds.Contains(modSlug)) throw new Exception($"«{modSlug}» — это сама игра или загрузчик, его нельзя поставить как мод.");
                        var proj = await ModrinthService.GetProjectAsync(modSlug);
                        // search fallback may return an unrelated project — accept only an exact id/slug match
                        static string Norm(string s) => s.ToLowerInvariant().Replace('_', '-');
                        if (proj == null || Norm(proj.Value.Slug) != Norm(modSlug))
                            throw new Exception($"Не нашёл на Modrinth мод с id «{modSlug}» — поставь его вручную через «Моды» сборки.");
                        if (!await InstallContentAsync(proj.Value.Slug, proj.Value.Title, "mod", slug))
                            throw new Exception($"Не удалось установить «{proj.Value.Title}» — подробности в панели загрузок.");
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
                        OpenPath(Path.Combine(inst, "logs"));
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
