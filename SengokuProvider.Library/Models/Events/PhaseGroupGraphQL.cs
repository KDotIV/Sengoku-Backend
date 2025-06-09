using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Library.Models.Events
{
    public class PhaseGroupGraphQL
    {
        public PhaseGroup PhaseGroup { get; set; }
    }

    public class PhaseGroup
    {
        public long Id { get; set; }
        public string DisplayIdentifier { get; set; }
        public Sets Sets { get; set; }
    }

    public class Sets
    {
        public PageInfo PageInfo { get; set; }
        public List<SetNode> Nodes { get; set; }
    }

    public class SetNode
    {
        public long Id { get; set; }
        public List<Slot> Slots { get; set; }
    }

    public class Slot
    {
        public string Id { get; set; }
        public Entrant Entrant { get; set; }
    }

    public class Entrant
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }

}
