using Newtonsoft.Json;
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
            var jsonObject = JObject.Load(reader);
            var topic = jsonObject["Topic"]?.ToObject<string>();

            ICommand? command = topic switch
            {
                nameof(LegendCommandRegistry.OnboardLegendsByPlayerData) => jsonObject.ToObject<OnboardLegendsByPlayerCommand>(),
                nameof(EventCommandRegistry.IntakeEventsByTournament) => jsonObject.ToObject<IntakeEventsByTournamentIdCommand>(),
                _ => throw new NotSupportedException($"Command topic '{topic}' is not supported")
            };

            return command;
        }

        public override void WriteJson(JsonWriter writer, ICommand? value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }
}
