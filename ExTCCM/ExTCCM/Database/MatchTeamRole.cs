using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Database
{
    [Table("MatchTeamRoles")]
    public class MatchTeamRole
    {
        [Key]
        public Guid Id { get; set; }
        public Guid? MatchTeamId { get; set; }

        [ForeignKey("MatchTeamId")]
        public virtual MatchTeam MatchTeam { get; set; }
    }
}
