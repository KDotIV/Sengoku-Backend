using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Regions
{
    public class GetRegionCommand : ICommand
    {
        public required Tuple<string, string> QueryParameter { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            return QueryParameter != null &&
                   !string.IsNullOrEmpty(QueryParameter.Item1) &&
                   !string.IsNullOrEmpty(QueryParameter.Item2);
        }
    }
    public class InsertRegionCommand : ICommand
    {
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
}
