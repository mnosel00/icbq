using ExTCCM.Database.Context;
using ExTCCM.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ExTCCM.Services
{
    public class StatsService
    {
       
        public async Task<List<RawKillEvent>> LoadStatsAsync(int roundsToFetch)
        {
            
            await using var context = new StatsDbContext();

            
            var rawKillsList = await context.Matches
                .OrderByDescending(m => m.Created) 
                .Take(roundsToFetch) 
                .SelectMany(
                    m => context.MatchEvents
                        .Where(ev => ev.MatchId == m.Id && ev.Discriminator == "MatchEventKilled")
                        .DefaultIfEmpty(), 
                    (m, ev) => new { m, ev } 
                )
                .Select(x => new RawKillEvent
                {
                    MatchId = x.m.Id.ToString(),
                    MatchName = x.m.Name,
                    MatchTime = x.m.Created,

                    
                    ShooterId = x.ev.Shooter.Id.ToString(),
                    ShooterName = x.ev.Shooter.PlayerName ?? "Nieznany",
                    ShooterTeam = x.ev.Shooter.MatchTeamRole.MatchTeam.Name ?? "Brak Drużyny",

                    VictimId = x.ev.Victim.Id.ToString(),
                    VictimName = x.ev.Victim.PlayerName ?? "Nieznany",
                    VictimTeam = x.ev.Victim.MatchTeamRole.MatchTeam.Name ?? "Brak Drużyny"
                })
                .ToListAsync(); 

            return rawKillsList;
        }

       
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

       
        public bool IsBase(string playerName)
        {
            return playerName == "Baza Daleko" || playerName == "Baza Blisko";
        }
    }
}

