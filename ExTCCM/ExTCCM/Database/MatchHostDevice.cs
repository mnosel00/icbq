using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Database
{
    [Table("MatchHostDevices")]
    public class MatchHostDevice
    {
        [Key]
        public Guid Id { get; set; }
        public string PlayerName { get; set; }
        public Guid? MatchTeamRoleId { get; set; }

        [ForeignKey("MatchTeamRoleId")]
        public virtual MatchTeamRole MatchTeamRole { get; set; }
    }
}
