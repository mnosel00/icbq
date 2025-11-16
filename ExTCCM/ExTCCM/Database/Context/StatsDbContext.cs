using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Database.Context
{
    public class StatsDbContext : DbContext
    {
        // Nazwa logiczna bazy z SSMS
        private const string DatabaseLogicalName = "ICEDB_42c9ed682c7a4d398ddd90a861a5f18a";
        private const string ServerName = "(LocalDB)\\MSSQLLocalDB";

        // Definiujemy tabele, do których chcemy mieć dostęp
        public DbSet<Match> Matches { get; set; }
        public DbSet<MatchEvent> MatchEvents { get; set; }
        public DbSet<MatchHostDevice> MatchHostDevices { get; set; }
        public DbSet<MatchTeam> MatchTeams { get; set; }
        public DbSet<MatchTeamRole> MatchTeamRoles { get; set; }

        // Mówimy EF Core, jak ma się połączyć
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Nasz "magiczny" connection string, który działa, gdy iCombat jest uruchomiony
            string connectionString = $"Server={ServerName};Database={DatabaseLogicalName};Integrated Security=True;TrustServerCertificate=True;";
            optionsBuilder.UseSqlServer(connectionString);
        }

        // Mówimy EF Core, że niektóre relacje są opcjonalne (aby uniknąć błędów)
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<MatchEvent>()
                .HasOne(e => e.Shooter)
                .WithMany()
                .HasForeignKey(e => e.ShooterId)
                .OnDelete(DeleteBehavior.Restrict); // Nie usuwaj gracza, gdy zdarzenie jest usuwane

            modelBuilder.Entity<MatchEvent>()
                .HasOne(e => e.Victim)
                .WithMany()
                .HasForeignKey(e => e.VictimId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
