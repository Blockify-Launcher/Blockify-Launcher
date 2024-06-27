using System.Diagnostics;

namespace BlockifyLauncher.Core
{
    public  class DiscordController
    {
        internal string filePath { get; set; } = "../";
        internal string fileName { get; set; } = "account.json";

        private long _id = 1255825330627805184;

        public Discord.Discord discord;

        public DiscordController() {
            discord = new Discord.Discord(_id, (System.UInt64)Discord.CreateFlags.Default);
        }

        public void Start ()
        {
            try
            {
                var activityManager = discord.GetActivityManager();
                var activity = new Discord.Activity
                {
                    State = "Still Testing",
                    Details = "Bigger Test"
                };
                activityManager.UpdateActivity(activity, (res) =>
                {
                    if (res == Discord.Result.Ok)
                    {
                        Debug.WriteLine("Everything is fine!");
                    }
                });
            } catch (Exception ex) 
            { Debug.WriteLine("Error : " + ex.Message); }
        }

        public void Update () { discord.RunCallbacks(); }
    }
}
