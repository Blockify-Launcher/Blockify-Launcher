using BlockifyLib.Launcher.Minecraft.Auth;
using Newtonsoft.Json;
using System.Collections;
using System.IO;

namespace BlockifyLauncher.MVVM.Views.Pages.Func.Setting
{
    public class Account : IEnumerable, IEnumerator
    {
        internal string filePath { get; set; } = "";
        internal string fileName { get; set; } = "account.json";

        private SessionStruct[] _session;
        private int index = -1;

        public Account()
        {
            _session = new SessionStruct[0];
            AccountInnitialization();
        }

        public void AccountInnitialization()
        {
            if (File.Exists(filePath + fileName))
            {
                string jsonFile = File.ReadAllText(filePath + fileName);
                _session = JsonConvert.
                            DeserializeObject<SessionStruct[]>(jsonFile)!;
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
            var sessionFromFile = JsonConvert.DeserializeObject<List<SessionStruct>>(File.ReadAllText(filePath + fileName));
            sessionFromFile?.RemoveAll(s => s.Id == id);
            File.WriteAllText(filePath + fileName, JsonConvert.SerializeObject(sessionFromFile, Formatting.Indented));

            for (int i = 0; i < _session.Length; i++)
                if (_session[i].Id == id)
                    DeleteUser(i);
        }

        public void CreateUser(string username)
        {
            try
            {
                SessionStruct sessiouUser = Session.GetOfflineSession(username);

                // Check file. 
                if (File.Exists(filePath + fileName))
                {
                    var sessionFromFile = JsonConvert.DeserializeObject<List<SessionStruct>>(File.ReadAllText(filePath + fileName));
                    sessionFromFile?.Add(sessiouUser);
                    File.WriteAllText(filePath + fileName, JsonConvert.SerializeObject(sessionFromFile, Formatting.Indented));
                }
                else
                {
                    SessionStruct[] sessions = new SessionStruct[1];
                    sessions[0] = sessiouUser;
                    File.WriteAllText(filePath + fileName, JsonConvert.SerializeObject(sessions, Formatting.Indented));
                }
                AddUser(sessiouUser);
            }
            catch (Exception e)
            {
                new MessageBox("Error : " + e.Message, MessageBox.TypeMessage.Error).Show();
            }
        }

        private void DeleteUser(int _index) =>
            _session = _session.Where(num => num != _session[_index]).ToArray();

        private void AddUser(SessionStruct newUser)
        {
            var _array_session = new SessionStruct[_session.Length + 1];
            Array.Copy(_session, _array_session, _session.Length);
            _array_session[^1] = newUser; 
            _session = _array_session;
        }

        public SessionStruct[] GetAllUserArray() => _session;
        public List<SessionStruct> GetAllUserList()
        {
            var list = new List<SessionStruct>();
            list.AddRange(_session);
            return list;
        }
    }
}
