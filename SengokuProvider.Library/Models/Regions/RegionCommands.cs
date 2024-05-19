using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Regions
{
    public class GetRegionCommand : ICommand
    {
        public required Tuple<string, string> QueryParameter { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
}
