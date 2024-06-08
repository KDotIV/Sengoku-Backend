using SengokuProvider.Library.Models.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Library.Models.Legends
{
    public class GetLegendsByPlayerLinkCommand : ICommand
    {
        public required int PlayerLinkId { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (PlayerLinkId != 0) return true;
            return false;
        }
    }
}
