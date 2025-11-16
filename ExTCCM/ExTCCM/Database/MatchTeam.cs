using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Database
{
    [Table("MatchTeams")]
    public class MatchTeam
    {
        [Key]
        public Guid Id { get; set; }
        public string Name { get; set; }
    }
}
