using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Models
{
    public class MatchInfo
    {
        public string MatchId { get; set; }
        public string MatchName { get; set; } // "Mecz 5" lub "Mecz na Cele 5"
        public string OriginalMatchName { get; set; } // "Custom Time Limit"
        public DateTime MatchTime { get; set; }
        public string MatchResult { get; set; } // "Alpha Wygrała (10-5)"

        public bool IsSelectedForSummary { get; set; } = true;
        public bool IncludePersonalStats { get; set; } = true;
    }
}
