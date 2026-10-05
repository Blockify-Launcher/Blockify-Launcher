using BlockifyLauncher.Core;
using BlockifyLib.Launcher.Minecraft.Auth;
using Newtonsoft.Json;
using System.Collections;
using System.IO;

namespace BlockifyLauncher.MVVM.Views.Pages.Func.Setting
{
    /// <summary>
    /// Saved launcher accounts: %APPDATA%\BlockifyLauncher\account.json.
    /// It used to be read relative to the working directory, so a launch from a blockify:// link (which starts
    /// in the browser's folder) or from a shortcut with another "Start in" saw no accounts at all. The old file
    /// next to the exe / in the working directory is copied over once.
    /// Writes are atomic (tmp → rename); a damaged file is moved aside as account.json.corrupt-&lt;stamp&gt;
    /// and the launcher starts with no accounts instead of crashing.
    /// </summary>
    public class Account : IEnumerable, IEnumerator
    {
        internal string filePath { get; set; } = AppPaths.DataDir;
        internal string fileName { get; set; } = "account.json";
        private string FullPath => Path.Combine(filePath, fileName);

        // every Account instance (window, settings page) works on the same file
        private static readonly object _fileLock = new();
        private static bool _migrationChecked;

        private SessionStruct[] _session;
        private int index = -1;

        public Account()
        {
            _session = new SessionStruct[0];
            AccountInnitialization();
        }

        public void AccountInnitialization()
        {
            lock (_fileLock)
            {
                MigrateLegacyFile();
                try { _session = ReadFile().ToArray(); }
                catch (Exception ex)
                {
                    // locked / no access: start without accounts for now, the file itself is left untouched
                    AppLog.Warn("accounts: cannot read " + FullPath + ": " + ex.Message);
                    _session = new SessionStruct[0];
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => _session.GetEnumerator();

        object IEnumerator.Current => Current;

        public SessionStruct Current
        {
            get
            {
                if (index == -1 || index >= _session.Length)
                    throw new ArgumentException();
                return _session[index];
            }
        }

        public bool MoveNext()
        {
            if (index < _session.Length - 1)
            {
                index++;
                return true;
            }
            return false;
        }

        public void Reset() =>
            index = -1;

        public void RemoveUser(string id)
        {
            try
            {
                lock (_fileLock)
                {
                    var list = ReadFile();
                    // remove exactly the entries with this id (the old index loop skipped neighbours)
                    list.RemoveAll(s => s.Id == id);
                    WriteFile(list);
                    _session = list.ToArray();
                }
            }
            catch (Exception e)
            {
                AppLog.Error("accounts: remove failed", e);
                new MessageBox("Не удалось удалить аккаунт: " + e.Message, MessageBox.TypeMessage.Error).Show();
            }
        }

        public void EditUser(string id, string userName)
        {
            try
            {
                lock (_fileLock)
                {
                    var list = ReadFile();
                    foreach (var s in list)
                        if (s.Id == id)
                            s.Username = userName;
                    WriteFile(list);
                    _session = list.ToArray();
                }
            }
            catch (Exception e)
            {
                AppLog.Error("accounts: edit failed", e);
                new MessageBox("Не удалось сохранить аккаунт: " + e.Message, MessageBox.TypeMessage.Error).Show();
            }
        }

        public void CreateUser(string username) =>
            CreateUser(Session.GetOfflineSession(username));

        public void CreateUser(SessionStruct sessionUser)
        {
            try
            {
                lock (_fileLock)
                {
                    var list = ReadFile();
                    // Re-login of an existing account replaces the old entry.
                    list.RemoveAll(s => s.Id == sessionUser.Id);
                    list.Add(sessionUser);
                    WriteFile(list);
                    _session = list.ToArray();
                }
            }
            catch (Exception e)
            {
                AppLog.Error("accounts: save failed", e);
                new MessageBox("Не удалось сохранить аккаунт: " + e.Message, MessageBox.TypeMessage.Error).Show();
            }
        }

        public SessionStruct[] GetAllUserArray() => _session;
        public List<SessionStruct> GetAllUserList()
        {
            var list = new List<SessionStruct>();
            list.AddRange(_session);
            return list;
        }

        // ── file (callers hold _fileLock) ──

        // Parse errors → the file is moved aside and an empty list returned (the next save starts a fresh file).
        // I/O errors propagate: overwriting a file we merely couldn't read would lose every account.
        private List<SessionStruct> ReadFile()
        {
            string path = FullPath;
            if (!File.Exists(path)) return new List<SessionStruct>();

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new List<SessionStruct>();
            try
            {
                var list = JsonConvert.DeserializeObject<List<SessionStruct>>(json) ?? new List<SessionStruct>();
                list.RemoveAll(s => s == null);   // keeps indices in line with the account combo
                return list;
            }
            catch (JsonException ex)
            {
                string aside = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Move(path, aside); }
                catch (Exception mex) { AppLog.Warn("accounts: cannot move the damaged file aside: " + mex.Message); }
                AppLog.Warn($"accounts: {path} is damaged ({ex.Message}), moved to {aside}");
                App.AddStartupWarning("Файл аккаунтов был повреждён — он сохранён как " + Path.GetFileName(aside) +
                                      ", а лаунчер запущен без аккаунтов. Добавь аккаунт заново.");
                return new List<SessionStruct>();
            }
        }

        private void WriteFile(List<SessionStruct> list)
        {
            Directory.CreateDirectory(filePath);
            string path = FullPath;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(list, Formatting.Indented));
            File.Move(tmp, path, overwrite: true);   // same-volume rename: the old file is never half-written
        }

        // one-time copy from where older builds kept it: next to the exe or in the working directory
        private void MigrateLegacyFile()
        {
            if (_migrationChecked) return;
            _migrationChecked = true;
            try
            {
                string target = FullPath;
                if (File.Exists(target)) return;
                foreach (string dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
                {
                    string old = Path.GetFullPath(Path.Combine(dir, fileName));
                    if (!File.Exists(old) ||
                        string.Equals(old, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                        continue;
                    Directory.CreateDirectory(filePath);
                    File.Copy(old, target, overwrite: false);
                    AppLog.Info("accounts: account.json copied from " + old + " to " + target);
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("accounts: migration of the old account.json failed: " + ex.Message);
            }
        }
    }
}
