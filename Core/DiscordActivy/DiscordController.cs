using Discord;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace BlockifyLauncher.Core.DiscordActivy
{
    public struct DiscordStructurId
    {
        [JsonProperty("DISCORDID")]
        public long Id;
    }

    public class DiscordController
    {
        private bool _valid = true;

        internal string filePath { get; set; } = "Core";
        internal string fileName { get; set; } = "discord_config.json";

        private DiscordStructurId _discordInformation;

        private Discord.Discord? discord;
        private Discord.Activity activity;

        private int _delayUpdate = 0;

        public DiscordController()
        {
            //DiscordInititalization();
            _delayUpdate = 1000 / 60;

            if (_valid && IsDiscordRunning())
            {
                discord = new Discord.Discord(1255825330627805184, (ulong)CreateFlags.Default);
                var lobbyManager = discord.GetLobbyManager();
                
                Intitalization();   // create format information.

                Start(lobbyManager);
                UpdateActivity();
            }
        }

        public static bool IsDiscordRunning()
        {
            Process[] processes = Process.GetProcessesByName("Discord");
            return processes.Length > 0;
        }

        /* Information block launcher for discord. */
        private void Intitalization()
        {
            activity = new Discord.Activity
            {
                State = "Main launcher",
                Details = "This minecraft launcher.",
                Timestamps =
                {
                    Start = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                Assets =
                {
                    LargeImage = "avatarblockylauncher",
                    LargeText = "BlockyLauncher",
                },
                Instance = true
            };
        }

        /* Information block launcher for start game. */
        public void UpdateStartGame(string versionName)
        {
            // econom resource pc.
            _delayUpdate = 1000;

            activity.State = "Play minecraft.";
            activity.Assets.SmallText = $"minecraft {versionName}";
            activity.Assets.SmallImage = "minecraftactivy";
            UpdateActivity();
        }

        public void UpdateActivity()
        {
            var activityManager = discord?.GetActivityManager();
            activityManager.UpdateActivity(activity, (result) =>
            {
                // func result
            });
        }

        public void stop()
        {
            _valid = false;
            discord.Dispose();
        }

        public async Task StartAsync(LobbyManager lobbyManager)
        {
            UpdateActivity();

            while (_valid)
            {
                discord?.RunCallbacks();
                lobbyManager.FlushNetwork();

                await Task.Delay(_delayUpdate); // Delay 1.63 milisecond
            }
        }

        private Task backgroundTask;

        public async void Start(LobbyManager lobbyManager)
        {
            await StartAsync(lobbyManager);
        }
    }
}
