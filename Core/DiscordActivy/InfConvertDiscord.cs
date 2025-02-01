using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlockifyLauncher.Core.DiscordActivy
{
    public class InfConvertDiscord : JsonConverter<IDictionary<string, DiscordStructur>>
    {
        public override IDictionary<string, DiscordStructur> ReadJson(
            JsonReader reader,
            Type objectType,
            IDictionary<string, DiscordStructur> existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            JArray array = JArray.Load(reader);
            var result = new Dictionary<string, DiscordStructur>();

            foreach (var item in array)
            {
                var key = item["key"].ToString();
                var value = item["value"].ToObject<DiscordStructur>(serializer);
                result[key] = value;
            }
            return result;
        }

        public override void WriteJson(
            JsonWriter writer,
            IDictionary<string, DiscordStructur> Val,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
