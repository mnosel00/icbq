using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Database.Context
{
    public class StatsDbContext : DbContext
    {
        // Usunęliśmy stałe 'DatabaseLogicalName' i 'ServerName'

        // Definiujemy tabele (bez zmian)
        public DbSet<Match> Matches { get; set; }
        public DbSet<MatchEvent> MatchEvents { get; set; }
        public DbSet<MatchHostDevice> MatchHostDevices { get; set; }
        public DbSet<MatchTeam> MatchTeams { get; set; }
        public DbSet<MatchTeamRole> MatchTeamRoles { get; set; }

        // ===== CAŁA TA FUNKCJA JEST NOWA =====
        // Mówimy EF Core, jak ma się połączyć
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // 1. Zbuduj obiekt konfiguracji
            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) // Szukaj pliku tam, gdzie jest .exe
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            IConfigurationRoot configuration = configBuilder.Build();

            // 2. Odczytaj wartości z pliku JSON
            string serverName = configuration.GetSection("DatabaseSettings:ServerName").Value;
            string dbName = configuration.GetSection("DatabaseSettings:DatabaseLogicalName").Value;

            // 3. Zbuduj connection string
            string connectionString = $"Server={serverName};Database={dbName};Integrated Security=True;TrustServerCertificate=True;";

            optionsBuilder.UseSqlServer(connectionString);
        }

        // Funkcja OnModelCreating jest bez zmian
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<MatchEvent>()
                .HasOne(e => e.Shooter)
                .WithMany()
                .HasForeignKey(e => e.ShooterId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MatchEvent>()
                .HasOne(e => e.Victim)
                .WithMany()
                .HasForeignKey(e => e.VictimId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

