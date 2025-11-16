using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Database
{
    [Table("MatchEvents")]
    public class MatchEvent
    {
        public Guid Id { get; set; } 
        public string Discriminator { get; set; }
        public Guid MatchId { get; set; }

        [Column("ShooterMatchHostDeviceId1")]
        public Guid? ShooterId { get; set; }

        [Column("MatchHostDeviceId")]
        public Guid? VictimId { get; set; }

        [ForeignKey("MatchId")]
        public virtual Match Match { get; set; }

        [ForeignKey("ShooterId")]
        public virtual MatchHostDevice Shooter { get; set; }

        [ForeignKey("VictimId")]
        public virtual MatchHostDevice Victim { get; set; }
    }
}
