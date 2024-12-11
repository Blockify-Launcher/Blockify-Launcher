using Discord;
using Microsoft.Identity.Client;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Navigation;

namespace BlockifyLauncher.Core.DiscordActivy
{
    public class DiscordController
    {
        private bool _valid = true;

        internal string _filePath { get; } = @"C:\Users\Palma\source\repos\BlockifyLauncher\Blockify-Launcher\Core\DiscordActivy\discord_config.json";
        internal string _fileName { get; } = "discord_config.json";
        internal string _SDKPath  {
            get => Environment.Is64BitProcess ?
                @"SDK\Discord\x86_64\" : @"SDK\Discord\x86\";
        }

        private DiscordStructurInitaliz _discordStr;
        private Discord.Discord? _discord;
        private Discord.Activity _activity;

        // param
        private int _delayUpdate = 0;

        private static bool IsDiscordRunning() =>
            (Process.GetProcessesByName("Discord")).Length > 0;

        private Discord.Activity _UpdateDiscordActivity(DiscordStructur _infoDis) =>
            new Discord.Activity
            {
                State       = _infoDis.State,
                Details     = _infoDis.Details,
                Timestamps  =
                {
                    Start = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                Assets      =
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

        /*public void UpdateDiscordActivity(string key)
        {

        }*/

        public DiscordController() : base() {
            _delayUpdate = 1000 / 60;

            if (_valid || IsDiscordRunning())
            {
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
                        throw new DllNotFoundException("Не удалось загрузить библиотеку discord_game_sdk.dll");
                }
                catch (DllNotFoundException ex)
                {
                    Debug.WriteLine(ex.Message);
                }

                InitializationJSON();

                _activity   = _UpdateDiscordActivity(_discordStr.DiscordStruct["main"]);
                _discord    = new Discord.Discord(
                    _discordStr.DISCORDID, 
                    (UInt64)CreateFlags.Default);

                var lobbyManager = _discord.GetLobbyManager();
                Start(lobbyManager);
                UpdateActivity();
            }
        }

        private void UpdateActivity()
        {
            var activity = _discord?.GetActivityManager();
            activity.UpdateActivity(_activity, (result) => {});
        }

        private async Task StartAsync(LobbyManager lobbyManager)
        {
            while (_valid || IsDiscordRunning())
            {
                _discord?.RunCallbacks();
                lobbyManager.FlushNetwork();
                await Task.Delay(_delayUpdate);
            }
        }

        private Task _backgroundTask;

        public async void Start(LobbyManager lobbyManager)
        {
            //_backgroundTask = 
                StartAsync(lobbyManager);
            //_backgroundTask.Start();
        }

        public void Stop()
        {
            _valid = false;
            _discord?.Dispose();
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