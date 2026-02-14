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
       
        public DbSet<Match> Matches { get; set; }
        public DbSet<MatchEvent> MatchEvents { get; set; }
        public DbSet<MatchHostDevice> MatchHostDevices { get; set; }
        public DbSet<MatchTeam> MatchTeams { get; set; }
        public DbSet<MatchTeamRole> MatchTeamRoles { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) 
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            IConfigurationRoot configuration = configBuilder.Build();

            string serverName = configuration.GetSection("DatabaseSettings:ServerName").Value;
            string dbName = configuration.GetSection("DatabaseSettings:DatabaseLogicalName").Value;

            // 3. Zbuduj connection string gad
            string connectionString = $"Server={serverName};AttachDbFileName={dbName};Database=ICombatStats;Integrated Security=True;TrustServerCertificate=True;";

            optionsBuilder.UseSqlServer(connectionString, sqlServerOptionsAction: sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure();
            });
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

