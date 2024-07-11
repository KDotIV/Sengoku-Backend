using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Common
{
    public static class JsonSettings
    {
        public static JsonSerializerSettings DefaultSettings => new JsonSerializerSettings
        {
            Formatting = Formatting.Indented
        };
    }
    public class CommandSerializer : JsonConverter<ICommand>
    {
        public override ICommand? ReadJson(JsonReader reader, Type objectType, ICommand? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject commandContainer = JObject.Load(reader);
            if (commandContainer == null)
                throw new NullReferenceException("Command container is null");

            ICommand? command = null;
            var topicToken = commandContainer["Topic"];
            if (topicToken != null)
            {
                if (topicToken.Type == JTokenType.Integer)
                {
                    command = DeserializeCommand((CommandRegistry)(int)topicToken, commandContainer, serializer);
                }
                else if (topicToken.Type == JTokenType.String && Enum.TryParse<CommandRegistry>(topicToken.ToString(), ignoreCase: true, out var topic))
                {
                    command = DeserializeCommand(topic, commandContainer, serializer);
                }
                else
                {
                    Console.WriteLine($"Invalid or unrecognized topic value: {topicToken}");
                }
            }
            else
            {
                Console.WriteLine("Topic token is missing or null in the received data.");
            }

            return command;
        }
        public override void WriteJson(JsonWriter writer, ICommand? value, JsonSerializer serializer)
        {
            JsonSerializer customSerializer = JsonSerializer.Create(JsonSettings.DefaultSettings);
            customSerializer.Serialize(writer, value);
        }
        private ICommand? DeserializeCommand(CommandRegistry? topic, JObject commandToken, JsonSerializer serializer)
        {
            if (commandToken == null || topic == null)
            {
                Console.WriteLine("Topic or Command cannot be null");
                return null;
            }

            try
            {
                JsonSerializer localSerializer = new JsonSerializer
                {
                    // Copy the settings from the existing serializer but exclude the CommandSerializer
                    ContractResolver = serializer.ContractResolver,
                    DateFormatHandling = serializer.DateFormatHandling,
                    DefaultValueHandling = serializer.DefaultValueHandling,
                    NullValueHandling = serializer.NullValueHandling
                };

                // Add other converters if necessary, but exclude the CommandSerializer
                foreach (var converter in serializer.Converters)
                {
                    if (!(converter is CommandSerializer))
                    {
                        localSerializer.Converters.Add(converter);
                    }
                }

                ICommand? result = topic switch
                {
                    CommandRegistry.OnboardPlayerData => commandToken.ToObject<OnboardLegendsByPlayerCommand>(localSerializer),
                    CommandRegistry.IntakeEventsByTournament => commandToken.ToObject<IntakeEventsByTournamentIdCommand>(localSerializer),
                    CommandRegistry.IntakePlayersByTournament => commandToken.ToObject<IntakePlayersByTournamentCommand>(localSerializer),
                    CommandRegistry.UpdateEvent => throw new NotImplementedException(),
                    CommandRegistry.IntakeEventsByLocation => commandToken.ToObject<IntakeEventsByLocationCommand>(localSerializer),
                    CommandRegistry.IntakeEventsByGames => throw new NotImplementedException(),
                    CommandRegistry.UpdatePlayer => throw new NotImplementedException(),
                    CommandRegistry.UpdateLegend => throw new NotImplementedException(),
                    CommandRegistry.OnboardLegendsByPlayerData => commandToken.ToObject<OnboardLegendsByPlayerCommand>(),
                    CommandRegistry.IntakeLegendsByTournament => throw new NotImplementedException(),
                    _ => throw new NotSupportedException($"Unsupported command topic: {topic}")
                };

                return result;
            }
            catch (JsonSerializationException ex)
            {
                Console.WriteLine($"Error during command deserialization: {ex.Message}");
                return null;
            }
        }
    }
}
