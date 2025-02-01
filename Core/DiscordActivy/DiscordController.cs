using Discord;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace BlockifyLauncher.Core.DiscordActivy
{
    public class DiscordController
    {
        private bool _valid = true;
        private bool _play = false;

        internal string _filePath { get; } = @"C:\Users\Palma\source\repos\BlockifyLauncher\Blockify-Launcher\Core\DiscordActivy\discord_config.json";
        internal string _fileName { get; } = "discord_config.json";
        internal string _SDKPath
        {
            get => Environment.Is64BitProcess ?
                @"SDK\Discord\x86_64\" : @"SDK\Discord\x86\";
        }

        private DiscordStructurInitaliz _discordStr;
        private Discord.Discord? _discord;
        private Discord.Activity _activity;

        private int _delayUpdate = 0;

        private static bool IsDiscordRunning() =>
            (Process.GetProcessesByName("Discord")).Length > 0;

        private static bool IsProcessRunning(string processName) =>
            Process.GetProcessesByName(processName).Length > 0;

        private static bool IsBlockifyLauncherRunning()
        {
            var processes = Process.GetProcessesByName("javaw");

            foreach (var process in processes)
            {
                try
                {
                    var commandLine = GetCommandLine(process);

                    if (commandLine != null && commandLine.Contains("BlockifyLauncher"))
                        return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }

            return false;
        }

        private static string GetCommandLine(Process process)
        {
            try
            {
                var query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}";
                var searcher = new ManagementObjectSearcher(query);
                var queryCollection = searcher.Get();

                foreach (var queryObj in queryCollection)
                    return queryObj["CommandLine"]?.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            return null;
        }

        private Discord.Activity _UpdateDiscordActivity(DiscordStructur _infoDis) =>
            new Discord.Activity
            {
                State = _infoDis.State,
                Details = _infoDis.Details,
                Assets =
                {
                    LargeImage  = _infoDis.Assets.LargeImage,
                    LargeText   = _infoDis.Assets.LargeText,
                    SmallImage  = _infoDis.Assets.SmallImage,
                    SmallText   = _infoDis.Assets.SmallText,
                },
                Instance = _infoDis.Instance,
            };

        private void InitializationJSON() =>
         _discordStr = JsonConvert
                .DeserializeObject<DiscordStructurInitaliz>(
                    File.ReadAllText(_filePath));

        public void UpdateDiscordActivity(string key, string val = null)
        {
            if (key == "play")
            {
                // econom resourse pc.
                _delayUpdate = 1000;

                _activity = _UpdateDiscordActivity(_discordStr.DiscordStruct[key]);
                UpdateActivity();

                _play = true; 
            }
        }

        public DiscordController() : base()
        {
            _delayUpdate = 1000 / 60;

            if (_valid && IsDiscordRunning())
            {
                // TODO : fix path
                //string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _filePath, _fileName); // fix path
                //Debug.WriteLine(local);

                SetDllDirectory(
                    Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        _SDKPath));

                try
                {
                    IntPtr handle = LoadLibrary("discord_game_sdk.dll");
                    if (handle == IntPtr.Zero)
                        throw new DllNotFoundException("Failed to load the library discord_game_sdk.dll");
                }
                catch (DllNotFoundException ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                string discordToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
                Debug.WriteLine(discordToken);
                _discordStr.DISCORDID =
                    long.Parse(Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN"));

                InitializationJSON();

                _activity = _UpdateDiscordActivity(_discordStr.DiscordStruct["main"]);
                _activity.Timestamps.Start = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _discord = new Discord.Discord(
                    _discordStr.DISCORDID,
                    (UInt64)CreateFlags.Default);

                Start();
                UpdateActivity();
            }
        }

        private void UpdateActivity()
        {
            var activity = _discord?.GetActivityManager();
            activity.UpdateActivity(_activity, (result) => {
                //Debug.WriteLine($"debug discord update activity. Code : {result}");
            });
        }

        private async Task StartAsync(string processName)
        {
            try
            {
                while (_valid)
                {
                    if (!IsDiscordRunning())
                    {
                        Stop();
                        throw new Exception("Discord closing...");
                    }

                    if (!IsProcessRunning(processName))
                    {
                        Stop();
                        throw new Exception("Not work process...");
                    }

                    if (_play)
                        if (!IsBlockifyLauncherRunning())
                        {
                            Stop();
                            throw new Exception("The javaw.exe process is closed...");
                        }
                    
                    Debug.WriteLine(IsProcessRunning(processName));

                    _discord?.RunCallbacks();
                    await Task.Delay(_delayUpdate);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("error StartAsync: " + ex.Message);
                Stop();
            }
        }

        private Task _backgroundTask;

        public async void Start()
        {
            _backgroundTask = Task.Run(() => StartAsync("BlockifyLauncher"));
            await _backgroundTask;
        }

        public void Stop()
        {
            _valid = false;
            _discord?.Dispose();
            if (_backgroundTask != null && !_backgroundTask.IsCompleted)
            {
                _backgroundTask.Wait();
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string lpFileName);
    }
}


/*
 
Улучшение безопасност 
 
 */