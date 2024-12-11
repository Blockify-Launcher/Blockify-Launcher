using Discord;
using Microsoft.Identity.Client;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;

namespace BlockifyLauncher.Core.DiscordActivy
{
    public class DiscordController
    {
        private bool _valid = false;

        internal string _filePath { get; } = @"Core\DiscordActivy";
        internal string _fileName { get; } = "discord_config.json";

        private DiscordStructurInitaliz _discordStr;
        private Discord.Discord? _discord;
        private Discord.Activity _activity;

        // param
        private int _delayUpdate = 0;
        private Task backgroundTask; 

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
                    Path.Combine(Directory.GetCurrentDirectory(), _filePath, _fileName));

        /*public void UpdateDiscordActivity(string key)
        {

        }*/

        public DiscordController()
               : base() {
            _delayUpdate = 1000 / 60;

            if (_valid || IsDiscordRunning())
            {
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _filePath, _fileName); // fix path
                Debug.WriteLine(local);
                InitializationJSON();
                _discord = new Discord.Discord(
                    _discordStr.DISCORDID, 
                    (ulong)CreateFlags.Default);
            }
        }

        /*private void UpdateActivity()
        {
            var activity = _discord?.GetActivityManager();
            //activity.UpdateActivity(_activity, (result) => { });
        }*/

        private async Task StartAsync(LobbyManager lobbyManager)
        {
            while (_valid || IsDiscordRunning())
            {
                _discord?.RunCallbacks();
                lobbyManager.FlushNetwork();
                await Task.Delay(_delayUpdate);
            }
        }

        public async void Start(LobbyManager lobbyManager) =>
            await StartAsync(lobbyManager);

        public void Stop()
        {
            _valid = false;
            _discord?.Dispose();
        }
    }
}


/*
 
Улучшение безопасност 
 
 */