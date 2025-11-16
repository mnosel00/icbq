using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Models
{
    public class RawKillEvent
    {
        public string MatchId { get; set; }
        public string MatchName { get; set; }
        public DateTime MatchTime { get; set; }
        public string ShooterId { get; set; }
        public string ShooterName { get; set; }
        public string ShooterTeam { get; set; }
        public string VictimId { get; set; }
        public string VictimName { get; set; }
        public string VictimTeam { get; set; }
    }
}
