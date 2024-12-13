using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Orgs
{
    public class CreateTravelCoOpCommand : ICommand
    {
        public required string OperationName { get; set; }
        public required int UserId { get; set; }
        public required string UserName { get; set; }
        public required double FundingGoal { get; set; }
        public required List<CoOpItem> CurrentItems { get; set; }
        public required List<int> CollabUserIds { get; set; }
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
