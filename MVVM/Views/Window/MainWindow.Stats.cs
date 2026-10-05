using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace BlockifyLauncher
{
    /// <summary>
    /// Local play statistics rebuilt from Minecraft's own logs (one log file = one session),
    /// so play time is counted even when the game ran while the launcher was closed.
    /// Parsed sessions are cached in %APPDATA%\BlockifyLauncher\stats.json keyed by path + length + mtime.
    /// </summary>
    public partial class MainWindow
    {
        private const int StatsChunk = 8 * 1024;
        private const int StatsWideChunk = 128 * 1024;
        private const int StatsDays = 14;
        private static readonly TimeSpan StatsMaxSession = TimeSpan.FromHours(16);
        private static readonly TimeSpan StatsMinSession = TimeSpan.FromMinutes(1);
        private static readonly object StatsLock = new();

        // "[12:34:56]" (latest.log) or "[28Sep2026 12:34:56.789]" (debug-style pattern)
        private static readonly Regex StatsTsRx = new(@"^\[(?:[^\]\r\n]*?\s)?(\d{1,2}):(\d{2}):(\d{2})(?:[.,]\d+)?\]",
            RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex StatsGzNameRx = new(@"^(\d{4}-\d{2}-\d{2})-\d+\.log\.gz$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private sealed class StatsFileEntry
        {
            [JsonProperty("len")] public long Len;
            [JsonProperty("ticks")] public long Ticks;
            [JsonProperty("start")] public DateTime? Start;   // null = no timestamps (not a session)
            [JsonProperty("end")] public DateTime? End;
        }

        private sealed class StatsCacheFile
        {
            [JsonProperty("v")] public int V = 1;
            [JsonProperty("files")] public Dictionary<string, StatsFileEntry> Files = new(StringComparer.OrdinalIgnoreCase);
        }

        private static string StatsJsonPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "BlockifyLauncher", "stats.json");

        private void InitStatsFeature() => StartStatsScan();

        private bool TryHandleStatsMessage(string type, JObject m)
        {
            if (type != "statsLoad") return false;
            StartStatsScan();
            return true;
        }

        private void StartStatsScan()
        {
            // paths come from settings/launcher objects, so resolve them here on the UI thread
            var targets = new List<(string slug, string logs)>();
            string mainLogs = "";
            try
            {
                foreach (var p in LoadPacks())
                {
                    if (string.IsNullOrWhiteSpace(p.Slug)) continue;
                    // stored dir if it still exists, else <packs root>/<slug> (moved .minecraft)
                    targets.Add((p.Slug, Path.Combine(PackHome(p), "logs")));
                }
                mainLogs = Path.Combine(McBase(), "logs");
            }
            catch (Exception ex) { LogDiag("stats: " + ex.Message); }

            Task.Run(() =>
            {
                object payload;
                try { payload = BuildStatsPayload(targets, mainLogs); }
                catch (Exception ex) { LogDiag("stats scan failed: " + ex); return; }
                try { Dispatcher.BeginInvoke(() => Post(payload)); } catch { }
            });
        }

        private static object BuildStatsPayload(List<(string slug, string logs)> targets, string mainLogs)
        {
            lock (StatsLock)
            {
                var cache = LoadStatsCache();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool dirty = false;

                var packs = new Dictionary<string, object>();
                foreach (var (slug, logs) in targets)
                    packs[slug] = AggregateStats(CollectSessions(logs, cache, seen, ref dirty));
                object? main = string.IsNullOrEmpty(mainLogs)
                    ? null : AggregateStats(CollectSessions(mainLogs, cache, seen, ref dirty));

                // forget logs that were deleted or belong to removed packs
                foreach (var k in cache.Files.Keys.Where(k => !seen.Contains(k)).ToList())
                {
                    cache.Files.Remove(k);
                    dirty = true;
                }
                if (dirty) SaveStatsCache(cache);

                return new { type = "stats", packs, main };
            }
        }

        private static List<(DateTime start, DateTime end)> CollectSessions(
            string logsDir, StatsCacheFile cache, HashSet<string> seen, ref bool dirty)
        {
            var list = new List<(DateTime start, DateTime end)>();
            List<FileInfo> files;
            try
            {
                if (!Directory.Exists(logsDir)) return list;
                // debug.log / debug-N.log.gz duplicate the same session, so only latest.log + dated archives count
                files = new DirectoryInfo(logsDir).EnumerateFiles()
                    .Where(f => f.Name.Equals("latest.log", StringComparison.OrdinalIgnoreCase) || StatsGzNameRx.IsMatch(f.Name))
                    .ToList();
            }
            catch { return list; }

            foreach (var fi in files)
            {
                string key = fi.FullName;
                seen.Add(key);
                long len, ticks;
                try
                {
                    // enumeration metadata can be stale for a log the game still holds open
                    fi.Refresh();
                    len = fi.Length;
                    ticks = fi.LastWriteTimeUtc.Ticks;
                }
                catch { continue; }

                if (!cache.Files.TryGetValue(key, out var e) || e == null || e.Len != len || e.Ticks != ticks)
                {
                    (DateTime start, DateTime end)? s;
                    try { s = ParseSessionLog(fi); }
                    catch (InvalidDataException) { s = null; }   // corrupt archive: cache as empty so it isn't retried
                    catch { continue; }                          // locked/unreadable right now: retry next scan
                    e = new StatsFileEntry { Len = len, Ticks = ticks, Start = s?.start, End = s?.end };
                    cache.Files[key] = e;
                    dirty = true;
                }
                if (e.Start is DateTime a && e.End is DateTime b && b - a >= StatsMinSession)
                    list.Add((a, b));
            }
            return list;
        }

        private static (DateTime start, DateTime end)? ParseSessionLog(FileInfo fi)
        {
            bool gz = fi.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
            var (head, tail) = gz ? ReadGzHeadTail(fi.FullName) : ReadPlainHeadTail(fi.FullName);
            var first = FirstStatsTs(head);
            var last = LastStatsTs(tail);
            if (first == null || last == null) return null;

            TimeSpan f = first.Value, l = last.Value;
            DateTime mt = DateTime.SpecifyKind(fi.LastWriteTime, DateTimeKind.Unspecified);
            DateTime start, end;
            if (gz)
            {
                var nm = StatsGzNameRx.Match(fi.Name);
                DateTime day = nm.Success && DateTime.TryParseExact(nm.Groups[1].Value, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : mt.Date;
                bool crossed = l < f;
                start = day + f;
                end = (crossed ? day.AddDays(1) : day) + l;
                // the archive is written when the next session starts, so its session can't end after that
                if (crossed && end > mt.AddMinutes(5))
                {
                    start = start.AddDays(-1);
                    end = end.AddDays(-1);
                }
            }
            else
            {
                // latest.log: anchor the end to the last write (creation time is unreliable due to NTFS tunneling)
                end = mt.Date + l;
                if (end > mt.AddMinutes(5)) end = end.AddDays(-1);
                start = end.Date + f;
                if (start > end) start = start.AddDays(-1);
            }

            if (end - start > StatsMaxSession) end = start + StatsMaxSession;
            return (DateTime.SpecifyKind(start, DateTimeKind.Unspecified), DateTime.SpecifyKind(end, DateTimeKind.Unspecified));
        }

        private static (string head, string tail) ReadPlainHeadTail(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long len = fs.Length;
            string head = StatsReadAt(fs, 0, StatsChunk, len);
            if (FirstStatsTs(head) == null && len > StatsChunk) head = StatsReadAt(fs, 0, StatsWideChunk, len);
            string tail = StatsReadTail(fs, StatsChunk, len);
            if (LastStatsTs(tail) == null && len > StatsChunk) tail = StatsReadTail(fs, StatsWideChunk, len);
            return (head, tail);
        }

        private static (string head, string tail) ReadGzHeadTail(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            // gzip has no random access: stream through, keeping the first block and the last two
            var older = new byte[StatsWideChunk];
            var newer = new byte[StatsWideChunk];
            var scratch = new byte[StatsWideChunk];
            int olderLen = 0, newerLen = 0, blocks = 0, n;
            string? head = null;
            while ((n = StatsReadFull(gz, scratch, scratch.Length)) > 0)
            {
                head ??= Encoding.UTF8.GetString(scratch, 0, n);
                (older, newer, scratch) = (newer, scratch, older);
                olderLen = newerLen;
                newerLen = n;
                blocks++;
            }
            if (head == null) return ("", "");

            var tailBytes = new byte[olderLen + newerLen];
            Buffer.BlockCopy(older, 0, tailBytes, 0, olderLen);
            Buffer.BlockCopy(newer, 0, tailBytes, olderLen, newerLen);
            string tail = Encoding.UTF8.GetString(tailBytes);
            return (head, blocks > 2 ? StatsDropPartialLine(tail) : tail);
        }

        private static string StatsReadAt(FileStream fs, long offset, int count, long len)
        {
            int size = (int)Math.Max(0, Math.Min(count, len - offset));
            var buf = new byte[size];
            fs.Position = offset;
            int n = StatsReadFull(fs, buf, size);
            return Encoding.UTF8.GetString(buf, 0, n);
        }

        private static string StatsReadTail(FileStream fs, int count, long len)
        {
            long from = Math.Max(0, len - count);
            string s = StatsReadAt(fs, from, count, len);
            return from > 0 ? StatsDropPartialLine(s) : s;
        }

        private static string StatsDropPartialLine(string s)
        {
            int i = s.IndexOf('\n');
            return i >= 0 ? s[(i + 1)..] : "";
        }

        private static int StatsReadFull(Stream s, byte[] buf, int count)
        {
            int total = 0, n;
            while (total < count && (n = s.Read(buf, total, count - total)) > 0) total += n;
            return total;
        }

        private static TimeSpan? StatsTsOf(Match m)
        {
            int h = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int mi = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int s = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            return h < 24 && mi < 60 && s < 60 ? new TimeSpan(h, mi, s) : null;
        }

        private static TimeSpan? FirstStatsTs(string text)
        {
            for (var m = StatsTsRx.Match(text); m.Success; m = m.NextMatch())
                if (StatsTsOf(m) is TimeSpan t) return t;
            return null;
        }

        private static TimeSpan? LastStatsTs(string text)
        {
            TimeSpan? last = null;
            for (var m = StatsTsRx.Match(text); m.Success; m = m.NextMatch())
                if (StatsTsOf(m) is TimeSpan t) last = t;
            return last;
        }

        private static object AggregateStats(List<(DateTime start, DateTime end)> sessions)
        {
            var today = DateTime.Now.Date;
            var from = today.AddDays(-(StatsDays - 1));
            var perDay = new double[StatsDays];
            double total = 0;
            (DateTime start, DateTime end)? last = null;

            foreach (var (s, e) in sessions)
            {
                total += (e - s).TotalMinutes;
                if (last == null || e > last.Value.end) last = (s, e);
                // split sessions that cross midnight between the two days
                for (var d = s.Date; d <= e.Date; d = d.AddDays(1))
                {
                    int i = (d - from).Days;
                    if (i < 0 || i >= StatsDays) continue;
                    var a = s > d ? s : d;
                    var b = e < d.AddDays(1) ? e : d.AddDays(1);
                    if (b > a) perDay[i] += (b - a).TotalMinutes;
                }
            }

            return new
            {
                totalMin = (int)Math.Round(total),
                sessions = sessions.Count,
                lastPlayed = last?.end.ToString("s", CultureInfo.InvariantCulture),
                lastStart = last?.start.ToString("s", CultureInfo.InvariantCulture),
                lastMin = last == null ? 0 : (int)Math.Round((last.Value.end - last.Value.start).TotalMinutes),
                days = Enumerable.Range(0, StatsDays).Select(i => new
                {
                    date = from.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    min = (int)Math.Round(perDay[i])
                }).ToList()
            };
        }

        private static StatsCacheFile LoadStatsCache()
        {
            try
            {
                string path = StatsJsonPath();
                if (File.Exists(path))
                {
                    var c = JsonConvert.DeserializeObject<StatsCacheFile>(File.ReadAllText(path));
                    if (c?.Files != null && c.V == 1)
                    {
                        var files = new Dictionary<string, StatsFileEntry>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in c.Files)
                            if (kv.Value != null) files[kv.Key] = kv.Value;
                        c.Files = files;
                        return c;
                    }
                }
            }
            catch { }
            return new StatsCacheFile();
        }

        private static void SaveStatsCache(StatsCacheFile c)
        {
            try
            {
                string path = StatsJsonPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(c));
                File.Move(tmp, path, true);
            }
            catch (Exception ex) { LogDiag("stats cache save: " + ex.Message); }
        }
    }
}
