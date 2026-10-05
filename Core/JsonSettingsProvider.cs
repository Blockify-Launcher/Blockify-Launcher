using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Specialized;
using System.Configuration;
using System.Xml.Linq;

namespace BlockifyLauncher.Core
{
    /// <summary>
    /// Storage for Properties.Settings: %APPDATA%\BlockifyLauncher\settings.json.
    /// The stock LocalFileSettingsProvider keeps user.config under %LOCALAPPDATA%\&lt;company&gt;\&lt;exe&gt;_Url_&lt;hash of the exe path&gt;\&lt;version&gt;,
    /// so every update or unpacking into another folder silently reset RAM, Java, the Minecraft folder and the
    /// active account. This file doesn't depend on the exe version or location.
    /// On first run the newest old user.config is imported once. Writes are atomic (tmp → replace, previous copy
    /// kept as .bak); a damaged file is moved aside as .broken-&lt;stamp&gt; and the .bak or defaults are used instead.
    /// </summary>
    public sealed class JsonSettingsProvider : SettingsProvider
    {
        private const int SchemaVersion = 1;

        private static readonly object _lock = new();
        private static Dictionary<string, string?>? _values;
        private static string? _warning;

        public static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

        public override string ApplicationName { get => "BlockifyLauncher"; set { } }

        // ApplicationSettingsBase calls Initialize(null, null); ProviderBase rejects a null name
        public override void Initialize(string name, NameValueCollection config)
            => base.Initialize(string.IsNullOrEmpty(name) ? nameof(JsonSettingsProvider) : name, config);

        /// <summary>A message for the user when the settings file had to be recovered (shown once).</summary>
        public static string? TakeWarning()
        {
            lock (_lock)
            {
                string? w = _warning;
                _warning = null;
                return w;
            }
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            Dictionary<string, string?> stored;
            lock (_lock) stored = new Dictionary<string, string?>(LoadLocked());

            var values = new SettingsPropertyValueCollection();
            foreach (SettingsProperty prop in collection)
            {
                var value = CreateValue(prop, stored.TryGetValue(prop.Name, out var s) ? s : null);
                try
                {
                    _ = value.PropertyValue;   // a hand-edited or imported "abc" for an int must not crash every read
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"settings: bad value for {prop.Name} ('{s}'), using the default: {ex.Message}");
                    value = CreateValue(prop, null);
                }
                value.IsDirty = false;
                values.Add(value);
            }
            return values;
        }

        private static SettingsPropertyValue CreateValue(SettingsProperty prop, string? serialized)
        {
            var value = new SettingsPropertyValue(prop);
            if (serialized != null) value.SerializedValue = serialized;
            else if (prop.DefaultValue != null) value.SerializedValue = prop.DefaultValue;
            else value.PropertyValue = null;
            return value;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            lock (_lock)
            {
                var data = LoadLocked();
                bool changed = !File.Exists(FilePath);
                foreach (SettingsPropertyValue value in collection)
                {
                    object? raw = value.SerializedValue;
                    string? s = raw as string ?? (raw == null ? null : Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture));
                    if (!data.TryGetValue(value.Name, out var old) || old != s)
                    {
                        data[value.Name] = s;
                        changed = true;
                    }
                    value.IsDirty = false;
                }
                if (changed) WriteLocked(data);
            }
        }

        // ── file ──
        private static Dictionary<string, string?> LoadLocked()
        {
            if (_values != null) return _values;

            string path = FilePath;
            var data = TryRead(path, moveAsideIfBroken: true);
            if (data == null && File.Exists(path + ".bak"))
            {
                data = TryRead(path + ".bak", moveAsideIfBroken: false);
                if (data != null) AppLog.Warn("settings: restored from settings.json.bak");
            }
            if (data == null && !File.Exists(path) && !File.Exists(path + ".bak") && _warning == null)
            {
                // first start with the new storage: take over what the old builds saved
                data = ImportLegacyUserConfig();
                if (data != null) WriteLocked(data);
            }
            _values = data ?? new Dictionary<string, string?>();
            return _values;
        }

        private static Dictionary<string, string?>? TryRead(string path, bool moveAsideIfBroken)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                var result = new Dictionary<string, string?>();
                if (root["values"] is JObject vals)
                    foreach (var p in vals.Properties())
                        result[p.Name] = p.Value.Type == JTokenType.Null ? null : p.Value.ToString();
                return result;
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidCastException || ex is FormatException)
            {
                AppLog.Warn($"settings: {Path.GetFileName(path)} is damaged: {ex.Message}");
                if (moveAsideIfBroken)
                {
                    string aside = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Move(path, aside); } catch (Exception mex) { AppLog.Warn("settings: cannot move the damaged file: " + mex.Message); }
                    _warning = "Файл настроек лаунчера был повреждён (например, после внезапного выключения ПК). " +
                               "Настройки восстановлены из резервной копии или сброшены на стандартные.";
                }
                return null;
            }
            catch (Exception ex)
            {
                // locked by an antivirus, no access…: work with defaults for this session, keep the file as is
                AppLog.Warn($"settings: cannot read {path}: {ex.Message}");
                return null;
            }
        }

        private static void WriteLocked(Dictionary<string, string?> data)
        {
            string path = FilePath;
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var root = new JObject
                {
                    ["schemaVersion"] = SchemaVersion,
                    ["values"] = JObject.FromObject(data)
                };
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
                {
                    w.Write(root.ToString(Formatting.Indented));
                    w.Flush();
                    fs.Flush(true);   // the data is on disk before the old file is replaced
                }

                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true); }
                    catch (IOException)
                    {
                        // File.Replace isn't supported everywhere (FAT, some network shares)
                        File.Copy(path, path + ".bak", overwrite: true);
                        File.Move(tmp, path, overwrite: true);
                    }
                }
                else File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                // a failed save must not crash the caller; the values stay in memory for this session
                AppLog.Warn("settings: cannot save settings.json: " + ex.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        // %LOCALAPPDATA%\Blockify_Launcher\BlockifyLauncher_Url_<hash>\<version>\user.config (and the older
        // "BlockifyLauncher" company folder) — take the most recently written one that parses
        private static Dictionary<string, string?>? ImportLegacyUserConfig()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var files = new List<FileInfo>();
                foreach (string companyDir in Directory.EnumerateDirectories(local, "Blockify*"))
                {
                    string company = Path.GetFileName(companyDir);
                    if (!company.StartsWith("BlockifyLauncher", StringComparison.OrdinalIgnoreCase) &&
                        !company.StartsWith("Blockify_Launcher", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // only <exe>_Url_<hash>\<version>\user.config — never walk into the WebView2 cache
                    foreach (string appDir in Directory.EnumerateDirectories(companyDir, "*_Url_*"))
                        foreach (string verDir in Directory.EnumerateDirectories(appDir))
                        {
                            var f = new FileInfo(Path.Combine(verDir, "user.config"));
                            if (f.Exists) files.Add(f);
                        }
                }

                foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc))
                {
                    try
                    {
                        var section = XDocument.Load(f.FullName).Root?
                            .Element("userSettings")?
                            .Element("BlockifyLauncher.Properties.Settings");
                        if (section == null) continue;
                        var result = new Dictionary<string, string?>();
                        foreach (var s in section.Elements("setting"))
                        {
                            string? name = (string?)s.Attribute("name");
                            if (string.IsNullOrEmpty(name)) continue;
                            if (((string?)s.Attribute("serializeAs") ?? "String") != "String") continue;
                            result[name] = (string?)s.Element("value") ?? "";
                        }
                        if (result.Count == 0) continue;
                        AppLog.Info($"settings: imported {result.Count} values from {f.FullName}");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn($"settings: skipped unreadable {f.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("settings: user.config import failed: " + ex.Message);
            }
            return null;
        }
    }
}
