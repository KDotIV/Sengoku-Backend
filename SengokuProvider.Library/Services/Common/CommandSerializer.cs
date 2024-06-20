using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Services.Common
{
    public class CommandSerializer : JsonConverter<ICommand>
    {
        public override ICommand? ReadJson(JsonReader reader, Type objectType, ICommand? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new StringEnumConverter { AllowIntegerValues = true } }
            };
            JObject jsonObject = JObject.Load(reader);
            var topic = jsonObject["Topic"]?.ToObject<int>(JsonSerializer.Create(settings));

            ICommand? command = topic switch
            {
                (int)LegendCommandRegistry.OnboardLegendsByPlayerData => jsonObject["Command"]?.ToObject<OnboardLegendsByPlayerCommand>(JsonSerializer.Create(settings)),
                (int)EventCommandRegistry.IntakeEventsByTournament => jsonObject["Command"]?.ToObject<IntakeEventsByTournamentIdCommand>(JsonSerializer.Create(settings)),
                _ => throw new NotSupportedException($"Command topic '{topic}' is not supported")
            };

            return command;
        }

        public override void WriteJson(JsonWriter writer, ICommand? value, JsonSerializer serializer)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                Converters = new List<JsonConverter> { new StringEnumConverter { AllowIntegerValues = true } }
            };
            JsonSerializer customSerializer = JsonSerializer.Create(settings);
            customSerializer.Serialize(writer, value);
        }
    }
}
