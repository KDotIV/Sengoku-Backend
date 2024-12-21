using Newtonsoft.Json;
using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Orgs
{
    public class CreateTravelCoOpCommand : ICommand
    {
        [JsonProperty("operationName")]
        public required string OperationName { get; set; }
        [JsonProperty("userId")]
        public required int UserId { get; set; }
        [JsonProperty("userName")]
        public required string UserName { get; set; }
        [JsonProperty("fundingGoal")]
        public required double FundingGoal { get; set; }
        [JsonProperty("currentItems")]
        public required List<int> CurrentItems { get; set; }
        [JsonProperty("collabUserIds")]
        public required List<int> CollabUserIds { get; set; }
        [JsonProperty("currentFunding")]
        public double CurrentFunding { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (!string.IsNullOrEmpty(OperationName) &&
                UserId > 0 && !string.IsNullOrEmpty(UserName) &&
                FundingGoal > 0)
                return true;
            else return false;
        }
    }
}
