using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace BlockifyLauncher
{
    /// <summary>
    /// Share an installed Modrinth pack as a short code (BLK-XXXX-…) and handle
    /// blockify://install/&lt;code&gt; deep links (protocol registered per-user in HKCU).
    /// Code = Crockford base32 of UTF-8 "m:{slug}@{versionId}", grouped by 4.
    /// </summary>
    public partial class MainWindow
    {
        private const string ShareCodePrefix = "BLK";
        private const string ShareLinkPrefix = "blockify://install/";
        private const string ShareAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        private static readonly HttpClient _shareHttp = CreateShareHttp();

        private bool _shareStartupChecked;
        // kept so a late-loading share.js can ask for the startup link result again
        private object? _shareStartupResult;
        // every opened link gets its own id: share.js shows each one once, even when the result is re-posted
        private int _shareLinkSeq;
        // links from a second start that arrived before the page got its init data
        private readonly List<string> _pendingShareLinks = new();

        private static HttpClient CreateShareHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd(BlockifyLauncher.Core.AppInfo.UserAgent);
            return http;
        }

        private void InitShareFeature()
        {
            RegisterShareProtocol();

            if (_shareStartupChecked) return;   // PushInit can run again after a page reload
            _shareStartupChecked = true;
            try
            {
                // the link this process was started with, then any that came from later starts meanwhile;
                // only the newest one is opened
                var links = new List<string>();
                string? startup = FindShareLink(App.PendingArgs);
                if (startup != null) links.Add(startup);
                links.AddRange(_pendingShareLinks);
                _pendingShareLinks.Clear();

                string? link = links.LastOrDefault();
                if (link != null)
                {
                    LogDiag("share: startup deep link " + link);
                    _ = ShareResolveAsync(link, auto: true);
                }
            }
            catch (Exception ex) { LogDiag("share: startup args failed: " + ex.Message); }
        }

        private static string? FindShareLink(IEnumerable<string>? args)
            => args?.FirstOrDefault(a => a != null && a.StartsWith(ShareLinkPrefix, StringComparison.OrdinalIgnoreCase));

        // a blockify:// link clicked while the launcher is already open: the second process hands its command
        // line over (App single instance) and App raises ExternalArgsReceived on the UI thread
        private void InitExternalLinks()
        {
            App.ExternalArgsReceived -= OnExternalArgs;
            App.ExternalArgsReceived += OnExternalArgs;
        }

        private void OnExternalArgs(string[] args)
        {
            string? link = FindShareLink(args);
            if (link == null) return;
            LogDiag("share: deep link from a second start " + link);
            if (_shareStartupChecked) _ = ShareResolveAsync(link, auto: true);   // page is up: same path as at startup
            else _pendingShareLinks.Add(link);                                   // InitShareFeature picks it up
        }

        private bool TryHandleShareMessage(string type, JObject m)
        {
            switch (type)
            {
                case "shareMake": ShareMake(m.Value<string>("slug") ?? ""); return true;
                case "shareResolve": _ = ShareResolveAsync(m.Value<string>("code") ?? "", auto: false); return true;
                case "shareReady":
                    if (_shareStartupResult != null) Post(_shareStartupResult);
                    return true;
                default: return false;
            }
        }

        // ── outgoing: pack → code ──
        private void ShareMake(string slug)
        {
            var p = LoadPacks().FirstOrDefault(x => x.Slug == slug);
            if (p == null)
            {
                Post(new { type = "shareCode", ok = false, slug, title = "", local = false, code = "", link = "", error = "Сборка не найдена." });
                return;
            }
            if (string.IsNullOrEmpty(p.VersionId))
            {
                Post(new { type = "shareCode", ok = false, slug, title = p.Title, local = true, code = "", link = "", error = "" });
                return;
            }
            string code = EncodeShareCode(p.Slug, p.VersionId);
            Post(new { type = "shareCode", ok = true, slug, title = p.Title, local = false, code, link = ShareLinkPrefix + code, error = "" });
        }

        // ── incoming: code / link → Modrinth preview ──
        private async Task ShareResolveAsync(string input, bool auto)
        {
            int linkId = auto ? ++_shareLinkSeq : 0;
            object result;
            try
            {
                if (!TryDecodeShareCode(input, out string codeSlug, out string versionId))
                    throw new ShareCodeException("Код не распознан. Проверь, что он скопирован целиком (BLK-…).");

                var ver = await ShareGetJsonAsync("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId),
                    "Версия сборки из кода не найдена на Modrinth — возможно, автор её удалил.");
                string projectId = ver.Value<string>("project_id") ?? "";
                if (projectId.Length == 0)
                    throw new ShareCodeException("Modrinth вернул неполные данные о версии.");

                var proj = await ShareGetJsonAsync("https://api.modrinth.com/v2/project/" + Uri.EscapeDataString(projectId),
                    "Проект сборки не найден на Modrinth.");
                if ((proj.Value<string>("project_type") ?? "modpack") != "modpack")
                    throw new ShareCodeException("Код ведёт не на сборку, а на другой тип проекта Modrinth.");

                // trust Modrinth's slug over the one embedded in the code
                string slug = proj.Value<string>("slug") ?? "";
                if (slug.Length == 0) slug = codeSlug;
                string title = proj.Value<string>("title") ?? slug;
                string loader = ((JArray?)ver["loaders"])?.FirstOrDefault()?.Value<string>() ?? "";
                bool supported = loader is "fabric" or "quilt" or "forge" or "neoforge";
                var installed = LoadPacks().FirstOrDefault(x => x.Slug == slug);

                result = new
                {
                    type = "shareResolved", ok = true, auto, linkId,
                    slug, versionId, title,
                    icon = proj.Value<string>("icon_url") ?? "",
                    mc = ((JArray?)ver["game_versions"])?.LastOrDefault()?.Value<string>() ?? "",
                    loader,
                    versionNumber = ver.Value<string>("version_number") ?? "",
                    versionName = ver.Value<string>("name") ?? "",
                    supported,
                    installedVersion = installed?.PackVersion ?? "",
                    isInstalled = installed != null,
                    sameVersion = installed != null && installed.VersionId == versionId,
                    code = EncodeShareCode(slug, versionId),
                    error = ""
                };
            }
            catch (ShareCodeException ex) { result = ShareFail(auto, linkId, input, ex.Message); }
            catch (TaskCanceledException) { result = ShareFail(auto, linkId, input, "Modrinth не ответил вовремя. Попробуй ещё раз чуть позже."); }
            catch (HttpRequestException ex)
            {
                LogDiag("share: http " + ex.Message);
                result = ShareFail(auto, linkId, input, "Не удалось связаться с Modrinth. Проверь подключение к интернету.");
            }
            catch (Exception ex)
            {
                LogDiag("share: resolve failed " + ex);
                result = ShareFail(auto, linkId, input, "Не удалось прочитать код: " + ex.Message);
            }

            if (auto) _shareStartupResult = result;
            Post(result);
        }

        private static object ShareFail(bool auto, int linkId, string input, string error)
            => new { type = "shareResolved", ok = false, auto, linkId, input, error };

        private static async Task<JObject> ShareGetJsonAsync(string url, string notFoundMessage)
        {
            using var resp = await _shareHttp.GetAsync(url);
            if (resp.StatusCode == HttpStatusCode.NotFound || resp.StatusCode == HttpStatusCode.Gone)
                throw new ShareCodeException(notFoundMessage);
            if ((int)resp.StatusCode == 429)
                throw new ShareCodeException("Modrinth временно ограничил запросы. Подожди минуту и попробуй снова.");
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Modrinth: HTTP {(int)resp.StatusCode}");
            return JObject.Parse(await resp.Content.ReadAsStringAsync());
        }

        private sealed class ShareCodeException : Exception
        {
            public ShareCodeException(string message) : base(message) { }
        }

        // ── code format ──
        // Short form (current): Modrinth version ids are 8 base62 chars (< 2^48), so the id alone is
        // packed into 10 Crockford chars + 1 check char → BLK-XXXX-XXXX-XXX. The slug is not needed:
        // the version id resolves to its project on Modrinth.
        private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private const int ShortDataLen = 10;

        private static string EncodeShareCode(string slug, string versionId)
        {
            if (versionId.Length == 8 && versionId.All(c => Base62.IndexOf(c) >= 0))
            {
                ulong value = 0;
                foreach (char c in versionId) value = value * 62 + (ulong)Base62.IndexOf(c);
                var digits = new char[ShortDataLen];
                for (int i = ShortDataLen - 1; i >= 0; i--) { digits[i] = ShareAlphabet[(int)(value & 31)]; value >>= 5; }
                string body = new string(digits) + ShareAlphabet[ShortCheck(digits)];
                return $"{ShareCodePrefix}-{body[..4]}-{body[4..8]}-{body[8..]}";
            }
            return EncodeLongShareCode(slug, versionId);
        }

        // weighted sum mod 31 (prime) catches any single mistyped character
        private static int ShortCheck(IReadOnlyList<char> data)
        {
            int sum = 0;
            for (int i = 0; i < ShortDataLen; i++) sum += (i + 1) * ShareAlphabet.IndexOf(data[i]);
            return sum % 31;
        }

        private static bool TryDecodeShortCode(string c, out string versionId)
        {
            versionId = "";
            if (c.Length != ShortDataLen + 1) return false;
            var data = new char[ShortDataLen];
            ulong value = 0;
            for (int i = 0; i < c.Length; i++)
            {
                char ch = c[i] switch { 'O' => '0', 'I' => '1', 'L' => '1', _ => c[i] };
                int v = ShareAlphabet.IndexOf(ch);
                if (v < 0) return false;
                if (i < ShortDataLen) { data[i] = ch; value = (value << 5) | (uint)v; }
                else if (v != ShortCheck(data)) return false;
            }
            if (value >= 218340105584896UL) return false;   // 62^8: not a valid 8-char id
            var id = new char[8];
            for (int i = 7; i >= 0; i--) { id[i] = Base62[(int)(value % 62)]; value /= 62; }
            versionId = new string(id);
            return true;
        }

        // Long form (first release of the feature): Crockford base32 of UTF-8 "m:{slug}@{versionId}".
        // Still accepted when decoding; only produced for ids that don't fit the short form.
        private static string EncodeLongShareCode(string slug, string versionId)
        {
            byte[] data = Encoding.UTF8.GetBytes($"m:{slug}@{versionId}");
            var b32 = new StringBuilder();
            int buffer = 0, bits = 0;
            foreach (byte b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    b32.Append(ShareAlphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
                buffer &= (1 << bits) - 1;
            }
            if (bits > 0) b32.Append(ShareAlphabet[(buffer << (5 - bits)) & 31]);

            var sb = new StringBuilder(ShareCodePrefix);
            for (int i = 0; i < b32.Length; i += 4)
                sb.Append('-').Append(b32.ToString(i, Math.Min(4, b32.Length - i)));
            return sb.ToString();
        }

        private static bool TryDecodeShareCode(string input, out string slug, out string versionId)
        {
            slug = versionId = "";
            string s = (input ?? "").Trim();
            try { s = Uri.UnescapeDataString(s); } catch { }
            int at = s.IndexOf(ShareLinkPrefix, StringComparison.OrdinalIgnoreCase);
            if (at >= 0) s = s[(at + ShareLinkPrefix.Length)..];

            // keep only code characters: drops dashes, spaces, slashes and stray punctuation
            var clean = new StringBuilder();
            foreach (char ch in s.ToUpperInvariant())
                if ((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z')) clean.Append(ch);
            string c = clean.ToString();
            if (c.StartsWith(ShareCodePrefix)) c = c[ShareCodePrefix.Length..];
            if (TryDecodeShortCode(c, out versionId)) return true;   // slug comes from Modrinth
            if (c.Length < 8) return false;

            var bytes = new List<byte>(c.Length * 5 / 8 + 1);
            int buffer = 0, bits = 0;
            foreach (char raw in c)
            {
                char ch = raw switch { 'O' => '0', 'I' => '1', 'L' => '1', _ => raw };
                int v = ShareAlphabet.IndexOf(ch);
                if (v < 0) return false;   // 'U' and anything else outside Crockford
                buffer = (buffer << 5) | v;
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                    bits -= 8;
                    buffer &= (1 << bits) - 1;
                }
            }

            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
            catch { return false; }
            if (!text.StartsWith("m:")) return false;
            int sep = text.LastIndexOf('@');
            if (sep <= 2 || sep == text.Length - 1) return false;
            slug = text[2..sep];
            versionId = text[(sep + 1)..];
            return Regex.IsMatch(versionId, "^[A-Za-z0-9]{4,64}$") && !slug.Any(char.IsControl);
        }

        // ── blockify:// protocol registration (per-user, no admin) ──
        private void RegisterShareProtocol()
        {
#if DEBUG
            // a debug build would point the user's blockify:// links at bin\Debug; opt in when testing links
            if (Environment.GetEnvironmentVariable("BLOCKIFY_REGISTER_PROTOCOL") != "1") return;
#endif
            // never bind links to an exe that lives in a temp folder (run from inside a zip)
            if (BlockifyLauncher.Core.AppPaths.IsRunningFromTemp()) return;
            try
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) ||
                    string.Equals(System.IO.Path.GetFileName(exe), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    return;
                string command = $"\"{exe}\" \"%1\"";

                using (var existing = Registry.CurrentUser.OpenSubKey(@"Software\Classes\blockify\shell\open\command"))
                using (var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\blockify"))
                {
                    if (existing?.GetValue("") as string == command && root?.GetValue("URL Protocol") != null)
                        return;
                }

                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\blockify"))
                {
                    key.SetValue("", "URL:Blockify Protocol");
                    key.SetValue("URL Protocol", "");
                    using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
                    using (var cmd = key.CreateSubKey(@"shell\open\command")) cmd.SetValue("", command);
                }
                LogDiag("share: registered blockify:// → " + command);
            }
            catch (Exception ex) { LogDiag("share: protocol registration failed: " + ex.Message); }
        }
    }
}
