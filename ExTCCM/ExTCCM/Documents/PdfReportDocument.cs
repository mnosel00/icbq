using ExTCCM.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExTCCM.Documents
{
    public class PdfReportDocument : IDocument
    {

        private readonly List<MatchInfo> _matchesForTeamSummary;
        private readonly List<PlayerStats> _allPlayerStats;
        private readonly List<PlayerStats> _summaryStats;

        public PdfReportDocument(List<MatchInfo> matches, List<PlayerStats> allStats, List<PlayerStats> summaryStats)
        {
            _matchesForTeamSummary = matches.OrderBy(m => m.MatchTime).ToList();
            _allPlayerStats = allStats;
            _summaryStats = summaryStats;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            var matchesToDisplay = _allPlayerStats
                .Select(s => new { s.MatchId, s.MatchName, s.MatchTime })
                .Distinct()
                .OrderBy(m => m.MatchTime)
                .ToList();

            foreach (var match in matchesToDisplay)
            {
                var originalMatchInfo = _matchesForTeamSummary.FirstOrDefault(m => m.MatchId == match.MatchId)
                                     ?? new MatchInfo { MatchId = match.MatchId, MatchName = "Mecz (wykluczony)", MatchResult = "Wykluczony", MatchTime = _allPlayerStats.First(s => s.MatchId == match.MatchId).MatchTime };

                var statsForMatch = _allPlayerStats.Where(s => s.MatchId == match.MatchId).ToList();

                container.Page(page =>
                {
                    page.Margin(30);
                    ComposeMatchPage(page, originalMatchInfo.MatchName, originalMatchInfo.MatchResult, statsForMatch, false);
                });
            }

            container.Page(page =>
            {
                page.Margin(30);
                ComposeSummaryPage(page, _summaryStats);
            });
        }

        private void ComposeMatchPage(PageDescriptor page, string matchName, string matchResult, List<PlayerStats> stats, bool excludeBases)
        {
            page.Header().Element(container => ComposeHeader(container, matchName, matchResult));
            page.Content().Element(container => ComposeStatsGrid(container, stats, false, excludeBases));
            page.Footer().Element(ComposeFooter);
        }

        private void ComposeSummaryPage(PageDescriptor page, List<PlayerStats> summaryStats)
        {
            page.Header().Element(container =>
            {
                var teamNames = _allPlayerStats.Select(s => s.Drużyna).Distinct().ToList();
                string teamAName = teamNames.FirstOrDefault(n => n != "Brak Drużyny") ?? "Drużyna A";
                string teamBName = teamNames.FirstOrDefault(n => n != "Brak Drużyny" && n != teamAName) ?? "Drużyna B";

                int teamAKills = summaryStats.Where(s => s.Drużyna == teamAName).Sum(s => s.Zabojstwa);
                int teamBKills = summaryStats.Where(s => s.Drużyna == teamBName).Sum(s => s.Zabojstwa);

                int teamAWins = _matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamAName));
                int teamBWins = _matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamBName));
                int draws = _matchesForTeamSummary.Count(m => m.MatchResult.StartsWith("Remis"));

                container.Column(col =>
                {
                    col.Item().AlignCenter().Text($"{teamAName} ({teamAWins} wygranych) vs {teamBName} ({teamBWins} wygranych)").Bold().FontSize(18);

                    if (draws > 0)
                        col.Item().AlignCenter().Text($"(Remisy: {draws})").FontSize(14);

                    col.Item().AlignCenter().Text($"Całkowite trafienia: {teamAKills} - {teamBKills}").FontSize(14);
                    col.Item().PaddingVertical(10);
                });
            });

            page.Content().Element(container => ComposeStatsGrid(container, summaryStats, true, true));
            page.Footer().Element(ComposeFooter);
        }

        private void ComposeHeader(IContainer container, string title, string subtitle)
        {
            container.Column(col =>
            {
                col.Item().Text(title).Bold().FontSize(24);
                col.Item().Text(subtitle).FontSize(16).SemiBold();
                col.Item().PaddingVertical(10);
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Row(row =>
            {
                row.Spacing(2);
                row.AutoItem().Text("Strona ");
                row.AutoItem().Text(x => x.CurrentPageNumber());
                row.AutoItem().Text(" z ");
                row.AutoItem().Text(x => x.TotalPages());
            });
        }

        private void ComposeStatsGrid(IContainer container, List<PlayerStats> stats, bool isSummaryPage = false, bool excludeBases = false)
        {
            var filteredStats = excludeBases
                ? stats.Where(s => !IsBase(s.Gracz)).ToList()
                : stats;

            var teamNames = filteredStats.Select(s => s.Drużyna).Distinct().ToList();
            var teamAStats = (teamNames.Count > 0)
                ? filteredStats.Where(s => s.Drużyna == teamNames[0]).OrderByDescending(s => s.Zabojstwa).ToList()
                : new List<PlayerStats>();
            var teamBStats = (teamNames.Count > 1)
                ? filteredStats.Where(s => s.Drużyna == teamNames[1]).OrderByDescending(s => s.Zabojstwa).ToList()
                : new List<PlayerStats>();

            var teamAName = (teamNames.Count > 0) ? teamNames[0] : "Drużyna A";
            if (isSummaryPage) teamAName = $"Suma - {teamAName}";

            var teamBName = (teamNames.Count > 1) ? teamNames[1] : "Drużyna B";
            if (isSummaryPage) teamBName = $"Suma - {teamBName}";

            container.Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().PaddingBottom(5).Text(teamAName).Bold().FontSize(18);
                    ComposeStatsTable(col.Item(), teamAStats);
                });

                row.ConstantItem(20);

                row.RelativeItem().Column(col =>
                {
                    col.Item().PaddingBottom(5).Text(teamBName).Bold().FontSize(18);
                    ComposeStatsTable(col.Item(), teamBStats);
                });
            });
        }

        private bool IsBase(string playerName)
        {
            return playerName == "Baza Daleko" || playerName == "Baza Blisko";
        }

        private void ComposeStatsTable(IContainer container, List<PlayerStats> stats)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(2).Text("Gracz").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().Text("Trafienia").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().Text("Śmierci").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().Text("Skuteczność").Bold();
                });

                foreach (var player in stats)
                {
                    table.Cell().Border(1).Padding(2).Text(player.Gracz);
                    table.Cell().Border(1).Padding(2).AlignCenter().Text(player.Zabojstwa.ToString());
                    table.Cell().Border(1).Padding(2).AlignCenter().Text(player.Smierci.ToString());
                    table.Cell().Border(1).Padding(2).AlignCenter().Text(player.KDRatio.ToString("F2"));
                }
            });
        }
    }
}
