using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ExTCCM.Models;
using Microsoft.Data.SqlClient;

namespace ExTCCM.Services
{
    public class StatsService
    {
        // --- USTAWIENIA ---
        // Logiczna nazwa bazy z Twojego zrzutu ekranu SSMS
        private const string DatabaseLogicalName = "ICEDB_42c9ed682c7a4d398ddd90a861a5f18a";
        private const string ServerName = "(LocalDB)\\MSSQLLocalDB";

        // --- FUNKCJE PUBLICZNE ---

        public async Task<List<RawKillEvent>> LoadStatsAsync(int roundsToFetch)
        {
            // Cała logika kopiowania i odłączania została usunięta

            var rawKillsList = new List<RawKillEvent>();

            // OSTATECZNY CONNECTION STRING:
            // Łączy się z serwerem i prosi o bazę o konkretnej nazwie logicznej.
            string connectionString = $"Server={ServerName};Database={DatabaseLogicalName};Integrated Security=True;";

            // Zapytanie SQL (bez zmian)
            string sqlQuery = @"
                DECLARE @RoundsToFetch INT = @FetchCount;
                WITH LatestMatches AS (
                    SELECT TOP (@RoundsToFetch) Id, Name, Created FROM dbo.Matches ORDER BY Created DESC
                ), AllEvents AS (
                    SELECT 
                        ev.MatchId,
                        ev.ShooterMatchHostDeviceId1,
                        ev.MatchHostDeviceId,
                        ev.Discriminator
                    FROM dbo.MatchEvents AS ev
                    WHERE ev.MatchId IN (SELECT Id FROM LatestMatches)
                      AND ev.Discriminator = 'MatchEventKilled'
                ),
                PlayerDevices AS (
                    SELECT 
                        dev.Id,
                        dev.PlayerName,
                        t.Name AS TeamName
                    FROM dbo.MatchHostDevices AS dev
                    LEFT JOIN dbo.MatchTeamRoles AS r ON dev.MatchTeamRoleId = r.Id
                    LEFT JOIN dbo.MatchTeams AS t ON r.MatchTeamId = t.Id
                )
                SELECT
                    m.Id AS 'MatchId',
                    m.Name AS 'MatchName',
                    m.Created AS 'MatchTime',
                    
                    shooter.Id AS 'ShooterId',
                    shooter.PlayerName AS 'ShooterName',
                    shooter.TeamName AS 'ShooterTeam',
                    
                    victim.Id AS 'VictimId',
                    victim.PlayerName AS 'VictimName',
                    victim.TeamName AS 'VictimTeam'
                    
                FROM 
                    LatestMatches AS m -- <-- ZMIANA: Zaczynamy od Meczów
                LEFT JOIN -- <-- ZMIANA: Używamy LEFT JOIN
                    AllEvents AS ev ON ev.MatchId = m.Id
                LEFT JOIN 
                    PlayerDevices AS shooter ON ev.ShooterMatchHostDeviceId1 = shooter.Id
                LEFT JOIN 
                    PlayerDevices AS victim ON ev.MatchHostDeviceId = victim.Id;
            ";

            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                // To musi być uruchomione z uprawnieniami Admina, 
                // aby "zobaczyć" bazę podłączoną przez iCombat (który też jest Adminem)
                await connection.OpenAsync();

                await using (SqlCommand command = new SqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@FetchCount", roundsToFetch);
                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            rawKillsList.Add(new RawKillEvent
                            {
                                MatchId = reader["MatchId"].ToString(),
                                MatchName = reader["MatchName"]?.ToString() ?? string.Empty,
                                MatchTime = (DateTime)reader["MatchTime"],
                                ShooterId = reader["ShooterId"]?.ToString(),
                                ShooterName = reader["ShooterName"]?.ToString() ?? "Nieznany",
                                ShooterTeam = reader["ShooterTeam"]?.ToString() ?? "Brak Drużyny",
                                VictimId = reader["VictimId"]?.ToString(),
                                VictimName = reader["VictimName"]?.ToString() ?? "Nieznany",
                                VictimTeam = reader["VictimTeam"]?.ToString() ?? "Brak Drużyny"
                            });
                        }
                    }
                }
            }

            return rawKillsList;
        }

        // Funkcja ProcessRawData (bez zmian)
        public (List<MatchInfo> Matches, List<PlayerStats> PlayerStats) ProcessRawData(List<RawKillEvent> rawKills)
        {
            var allPlayerStats = new List<PlayerStats>();
            var allMatches = new List<MatchInfo>();

            var groupedByMatch = rawKills.GroupBy(k => k.MatchId);
            int totalMatches = groupedByMatch.Count();
            int matchCounter = totalMatches;

            foreach (var matchGroup in groupedByMatch.OrderByDescending(g => g.First().MatchTime))
            {
                var matchEvents = matchGroup.ToList();
                var firstEvent = matchEvents.First();

                var shooters = matchEvents.Select(e => new { Id = e.ShooterId, Name = e.ShooterName, Team = e.ShooterTeam });
                var victims = matchEvents.Select(e => new { Id = e.VictimId, Name = e.VictimName, Team = e.VictimTeam });
                var allPlayersInMatch = shooters.Concat(victims)
                                             .Where(p => p.Id != null)
                                             .GroupBy(p => p.Id)
                                             .Select(g => g.First())
                                             .ToList();

                bool isBaseMatch = allPlayersInMatch.Any(p => IsBase(p.Name));
                string matchDisplayName = isBaseMatch ? $"Mecz na Cele {matchCounter}" : $"Mecz {matchCounter}";
                matchCounter--;

                string teamAName = allPlayersInMatch.Select(p => p.Team).FirstOrDefault(t => t != "Brak Drużyny") ?? "Drużyna A";
                string teamBName = allPlayersInMatch.Select(p => p.Team).FirstOrDefault(t => t != "Brak Drużyny" && t != teamAName) ?? "Drużyna B";

                string matchResultString;

                if (isBaseMatch)
                {
                    int teamAHitsOnBase = matchEvents.Count(e => e.ShooterTeam == teamAName && IsBase(e.VictimName));
                    int teamBHitsOnBase = matchEvents.Count(e => e.ShooterTeam == teamBName && IsBase(e.VictimName));

                    if (teamAHitsOnBase > teamBHitsOnBase)
                        matchResultString = $"{teamAName} Wygrała ({teamAHitsOnBase}-{teamBHitsOnBase} Cele)";
                    else if (teamBHitsOnBase > teamAHitsOnBase)
                        matchResultString = $"{teamBName} Wygrała ({teamBHitsOnBase}-{teamAHitsOnBase} Cele)";
                    else
                        matchResultString = $"Remis ({teamAHitsOnBase}-{teamBHitsOnBase} Cele)";
                }
                else
                {
                    int teamAKills = matchEvents.Count(e => e.ShooterTeam == teamAName && !IsBase(e.VictimName));
                    int teamBKills = matchEvents.Count(e => e.ShooterTeam == teamBName && !IsBase(e.VictimName));

                    if (teamAKills > teamBKills)
                        matchResultString = $"{teamAName} Wygrała ({teamAKills}-{teamBKills})";
                    else if (teamBKills > teamAKills)
                        matchResultString = $"{teamBName} Wygrała ({teamBKills}-{teamAKills})";
                    else
                        matchResultString = $"Remis ({teamAKills}-{teamBKills})";
                }

                allMatches.Add(new MatchInfo
                {
                    MatchId = firstEvent.MatchId,
                    MatchName = matchDisplayName,
                    OriginalMatchName = firstEvent.MatchName,
                    MatchTime = firstEvent.MatchTime,
                    MatchResult = matchResultString
                });

                foreach (var player in allPlayersInMatch)
                {
                    allPlayerStats.Add(new PlayerStats
                    {
                        MatchId = firstEvent.MatchId,
                        MatchName = firstEvent.MatchName,
                        MatchTime = firstEvent.MatchTime,
                        Gracz = player.Name,
                        Drużyna = player.Team,
                        Zabojstwa = matchEvents.Count(e => e.ShooterId == player.Id),
                        Smierci = matchEvents.Count(e => e.VictimId == player.Id)
                    });
                }
            }

            return (allMatches, allPlayerStats);
        }

        // Funkcja GetSummedStats (bez zmian)
        public List<PlayerStats> GetSummedStats(List<PlayerStats> statsToSum)
        {
            if (statsToSum == null || statsToSum.Count == 0)
                return new List<PlayerStats>();

            return statsToSum
                .GroupBy(s => new { s.Gracz, s.Drużyna })
                .Select(group => new PlayerStats
                {
                    Gracz = group.Key.Gracz,
                    Drużyna = group.Key.Drużyna,
                    Zabojstwa = group.Sum(s => s.Zabojstwa),
                    Smierci = group.Sum(s => s.Smierci)
                })
                .ToList();
        }

        // Funkcja IsBase (bez zmian)
        public bool IsBase(string playerName)
        {
            return playerName == "Baza Daleko" || playerName == "Baza Blisko";
        }
    }
}

