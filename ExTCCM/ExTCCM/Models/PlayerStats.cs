using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Models
{
    public class PlayerStats
    {
        public string MatchId { get; set; }
        public string MatchName { get; set; }
        public DateTime MatchTime { get; set; }
        public string Gracz { get; set; }
        public string Drużyna { get; set; }
        public int Zabojstwa { get; set; }
        public int Smierci { get; set; }
        public double KDRatio
        {
            get
            {
                if (Smierci > 0) return (double)Zabojstwa / Smierci;
                if (Zabojstwa > 0) return Zabojstwa;
                return 0;
            }
        }
    }
}
