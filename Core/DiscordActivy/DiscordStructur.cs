using Newtonsoft.Json;

namespace BlockifyLauncher.Core.DiscordActivy
{
    /* Struct connection */
    public struct DiscordStructurInitaliz
    {
        [JsonProperty("DISCORDID")]
        public long DISCORDID;

        [JsonProperty("INFO")]
        [JsonConverter(typeof(InfConvertDiscord))]
        public IDictionary<string, DiscordStructur>
            DiscordStruct;
    }

    /* Struct information */
    public struct DiscordStructur
    {
        [JsonProperty("State")]
        public string State;
        [JsonProperty("Details")]
        public string Details;

        public struct AssetsDiscordStructur
        {
            [JsonProperty("LargeImage")]
            public string LargeImage;
            [JsonProperty("LargeText")]
            public string LargeText;
            [JsonProperty("SmallImage")]
            public string SmallImage;
            [JsonProperty("SmallText")]
            public string SmallText;
        }

        [JsonProperty("Assets")]
        public AssetsDiscordStructur Assets;

        public struct PartyDiscordStructur
        {
            [JsonProperty("Id")]
            public string Id;

            public struct SizeParty
            {
                [JsonProperty("CurrentSize")]
                public int CurrentSize;
                [JsonProperty("MaxSize")]
                public int MaxSize;
            }

            [JsonProperty("Size")]
            public SizeParty Size;
        }

        [JsonProperty("Party")]
        public PartyDiscordStructur Party;

        public struct SecretsDiscordStructur
        {
            [JsonProperty("Match")]
            public string Match;
            [JsonProperty("Join")]
            public string Join;
            [JsonProperty("Spectate")]
            public string Spectate;
        }

        [JsonProperty("Secrets")]
        public SecretsDiscordStructur Secrets;

        [JsonProperty("Instance")]
        public bool Instance;
    }
}
