using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Library.Models.Orgs
{
    public class TravelCoOpResult
    {
        public required string OperationName {  get; set; }
        public required int UserId { get; set; }
        public required string UserName { get; set; }
        public required double FundingGoal { get; set; }
        public required List<int> CoOpItems { get; set; } = new List<int>();
        public required List<int> CollabUserIds { get; set; }
        public required DateTime LastUpdated { get; set; }
        public double CurrentFunding {  get; set; }
    }
}
