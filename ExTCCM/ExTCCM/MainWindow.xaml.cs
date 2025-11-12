using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static iCombatStatsExporter.MainWindow;

// Usunęliśmy 'using' dla NetOffice i Interop

namespace iCombatStatsExporter
{
    public partial class MainWindow : Window
    {
        // --- 1. USTAWIENIA (Bez zmian) ---
        private const string SourceDbPath = @"C:\Program Files (x86)\iCombat\Data\";
        private const string SourceDbFile = "IceDb.mdf";
        private const string SourceLogFile = "IceDb_log.ldf";
        private const string WorkDbPath = @"C:\StatsTemp\";
        private const string WorkDbFile = "IceDb.mdf";
        private const string ServerName = "(LocalDB)\\MSSQLLocalDB";

        // --- 2. GLOBALNE LISTY (Bez zmian) ---
        private List<PlayerStats> _allPlayerStats = new List<PlayerStats>();
        private List<MatchInfo> _loadedMatches = new List<MatchInfo>();
        private string _lastGeneratedPdfPath = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(WorkDbPath);
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // --- 3. MODELE DANYCH (Bez zmian) ---
        public class PlayerStats
        {
            public string MatchId { get; set; }
            public string MatchName { get; set; }
            public DateTime MatchTime { get; set; }
            public string Gracz { get; set; }
            public string Drużyna { get; set; }
            public int Zabojstwa { get; set; }
            public int Smierci { get; set; }
            public double KDRatio
            {
                get
                {
                    if (Smierci > 0) return (double)Zabojstwa / Smierci;
                    if (Zabojstwa > 0) return Zabojstwa;
                    return 0;
                }
            }
        }

        public class MatchInfo
        {
            public string MatchId { get; set; }
            public string MatchName { get; set; }
            public string OriginalMatchName { get; set; }
            public DateTime MatchTime { get; set; }
            public string MatchResult { get; set; }
        }

        // --- 4. LOGIKA PRZYCISKÓW ---

        // Przycisk "POBIERZ" (Bez zmian)
        private async void LoadStatsButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiEnabled(false, "Rozpoczynanie...");
            TeamAGrid.ItemsSource = null;
            TeamBGrid.ItemsSource = null;
            MatchesListBox.ItemsSource = null;
            MatchResultTextBlock.Text = "";
            _allPlayerStats.Clear();
            _loadedMatches.Clear();
            _lastGeneratedPdfPath = string.Empty;

            try
            {
                if (!int.TryParse(RoundsToFetchTextBox.Text, out int roundsToFetch) || roundsToFetch <= 0)
                {
                    SetStatus("BŁĄD: Wprowadź poprawną liczbę (np. 5).");
                    return;
                }

                _allPlayerStats = await LoadStatsAsync(roundsToFetch);

                var matchesFromStats = _allPlayerStats
                    .GroupBy(s => new { s.MatchId, s.MatchName, s.MatchTime })
                    .OrderByDescending(g => g.Key.MatchTime)
                    .ToList();

                int totalMatches = matchesFromStats.Count;

                _loadedMatches = matchesFromStats
                    .Select((matchGroup, index) => {
                        var statsForThisMatch = matchGroup.ToList();
                        var teamScores = statsForThisMatch
                            .GroupBy(s => s.Drużyna)
                            .Select(g => new {
                                TeamName = g.Key,
                                TotalKills = g.Sum(s => s.Zabojstwa)
                            })
                            .OrderByDescending(s => s.TotalKills)
                            .ToList();

                        string matchResultString = "Remis (0-0)";
                        if (teamScores.Count > 0)
                        {
                            var teamA = teamScores[0];
                            var teamB = (teamScores.Count > 1) ? teamScores[1] : new { TeamName = "Brak", TotalKills = 0 };

                            if (teamA.TotalKills > teamB.TotalKills)
                                matchResultString = $"{teamA.TeamName} Wygrała ({teamA.TotalKills}-{teamB.TotalKills})";
                            else if (teamB.TotalKills > teamA.TotalKills)
                                matchResultString = $"{teamB.TeamName} Wygrała ({teamB.TotalKills}-{teamA.TotalKills})";
                            else
                                matchResultString = $"Remis ({teamA.TotalKills}-{teamB.TotalKills})";
                        }

                        int matchNumber = totalMatches - index;
                        string dynamicName = $"Mecz {matchNumber}";

                        return new MatchInfo
                        {
                            MatchId = matchGroup.Key.MatchId,
                            MatchName = dynamicName,
                            OriginalMatchName = matchGroup.Key.MatchName,
                            MatchTime = matchGroup.Key.MatchTime,
                            MatchResult = matchResultString
                        };
                    })
                    .ToList();

                MatchesListBox.ItemsSource = _loadedMatches;

                if (_loadedMatches.Count > 0)
                {
                    MatchesListBox.SelectedIndex = 0;
                }

                SetStatus($"Gotowe! Pomyślnie załadowano {_loadedMatches.Count} meczy.");
            }
            catch (IOException ioEx)
            {
                HandleError($"BŁĄD: Nie można skopiować plików. Upewnij się, że program iCombat jest ZAMKNIĘTY. ({ioEx.Message})", "Błąd Kopiowania");
            }
            catch (Exception ex)
            {
                HandleError($"Krytyczny błąd: {ex.Message}", "Błąd Krytyczny");
            }
            finally
            {
                SetUiEnabled(_allPlayerStats.Count > 0);
            }
        }

        // Przycisk "SUMUJ WSZYSTKO" (Bez zmian)
        private void SumAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allPlayerStats == null || _allPlayerStats.Count == 0) return;
            SetStatus("Sumowanie statystyk ze wszystkich pobranych meczy...");
            MatchResultTextBlock.Text = "Suma Wszystkich Meczy";
            var summedStats = GetSummedStats();
            DisplayStatsInGrids(summedStats, "Suma - Drużyna A", "Suma - Drużyna B");
            MatchesListBox.SelectedIndex = -1;
        }

        // Akcja Wyboru Meczu (Bez zmian)
        private void MatchesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MatchesListBox.SelectedItem == null) return;
            var selectedMatch = (MatchInfo)MatchesListBox.SelectedItem;
            var filteredStats = _allPlayerStats.Where(s => s.MatchId == selectedMatch.MatchId).ToList();
            SetStatus($"Wyświetlanie statystyk dla: {selectedMatch.OriginalMatchName} ({selectedMatch.MatchName})");
            MatchResultTextBlock.Text = selectedMatch.MatchResult;
            DisplayStatsInGrids(filteredStats);
        }

        // Przycisk "GENERUJ PDF" (Bez zmian)
        private void GeneratePdfButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allPlayerStats == null || _allPlayerStats.Count == 0)
            {
                MessageBox.Show("Brak danych do wygenerowania raportu. Najpierw kliknij 'Pobierz'.", "Brak Danych", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetUiEnabled(false, "Generowanie PDF...");

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "PDF Document (*.pdf)|*.pdf",
                Title = "Zapisz raport PDF",
                FileName = $"Raport_iCombat_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;

                try
                {
                    SetStatus("Tworzenie dokumentu PDF...");
                    var summedStats = GetSummedStats();
                    var pdfDocument = new PdfReportDocument(_loadedMatches, _allPlayerStats, summedStats);
                    pdfDocument.GeneratePdf(filePath);

                    _lastGeneratedPdfPath = filePath;
                    SetStatus($"PDF Zapisany! Lokalizacja: {filePath}");

                    MessageBox.Show($"PDF został pomyślnie wygenerowany i zapisany!\n\nLokalizacja:\n{filePath}", "Raport Gotowy!", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    HandleError($"Nie udało się wygenerować PDF: {ex.Message}", "Błąd PDF");
                }
                finally
                {
                    SetUiEnabled(true);
                }
            }
            else
            {
                SetUiEnabled(true, "Anulowano generowanie PDF.");
            }
        }

        // ========================================================
        // ====== POPRAWIONA LOGIKA: PRZYCISK "WYŚLIJ EMAIL" ======
        // ========================================================
        private void SendEmailButton_Click(object sender, RoutedEventArgs e)
        {
            // Krok 1: Sprawdź, czy PDF został wygenerowany (nadal ważne)
            if (string.IsNullOrEmpty(_lastGeneratedPdfPath) || !File.Exists(_lastGeneratedPdfPath))
            {
                MessageBox.Show("Najpierw musisz wygenerować raport PDF za pomocą przycisku '1. Generuj Raport PDF'.", "Brak Pliku", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Krok 2: Sprawdź email
            string email = EmailTextBox.Text;
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                MessageBox.Show("Wprowadź poprawny adres e-mail odbiorcy.", "Brak Adresu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SetStatus("Otwieranie klienta poczty...");

                // Krok 3: Stwórz BARDZO PROSTY link 'mailto:'
                // Usuwamy 'body' całkowicie, aby uniknąć błędu długości.
                string subject = System.Web.HttpUtility.UrlEncode("Raport Statystyk iCombat");

                // Uruchomienie Process.Start w bezpieczny sposób
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"mailto:{email}?subject={subject}",
                    UseShellExecute = true // To jest kluczowe, aby użyć domyślnej aplikacji
                });

                // Informacja dla użytkownika
                MessageBox.Show(
                    "Otworzono domyślny program pocztowy.\n\n" +
                    "Proszę, napisz treść wiadomości i RĘCZNIE załącz plik PDF:\n\n" +
                    $"{_lastGeneratedPdfPath}",
                    "Gotowe do wysłania",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                // Ten błąd nadal może się pojawić, jeśli absolutnie żaden
                // program pocztowy nie jest skonfigurowany w Windows.
                HandleError($"Nie można otworzyć domyślnego programu pocztowego.\n\nBłąd: {ex.Message}", "Błąd E-mail");
            }
            finally
            {
                SetUiEnabled(true);
            }
        }


        // --- 5. GŁÓWNA FUNKCJA POBIERANIA DANYCH (Bez zmian) ---
        private async Task<List<PlayerStats>> LoadStatsAsync(int roundsToFetch)
        {
            SetStatus("Krok 1/4: Kopiowanie plików bazy danych...");
            await Task.Run(() =>
            {
                File.Copy(Path.Combine(SourceDbPath, SourceDbFile), Path.Combine(WorkDbPath, WorkDbFile), true);
                File.Copy(Path.Combine(SourceDbPath, SourceLogFile), Path.Combine(WorkDbPath, SourceLogFile), true);
            });

            SetStatus("Krok 2/4: Łączenie z bazą danych...");
            var statsList = new List<PlayerStats>();
            string tempDbName = $"iCombatStatsCopy_{Guid.NewGuid():N}";
            string workDbFilePath = Path.Combine(WorkDbPath, WorkDbFile);
            string connectionString = $"Server={ServerName};Database={tempDbName};Integrated Security=True;AttachDbFileName='{workDbFilePath}';";

            string sqlQuery = @"
                DECLARE @RoundsToFetch INT = @FetchCount;
                WITH LatestMatches AS (
                    SELECT TOP (@RoundsToFetch) Id, Name, Created FROM dbo.Matches ORDER BY Created DESC
                ), AllPlayerIDs AS (
                    SELECT DISTINCT ev.MatchHostDeviceId AS PlayerId, ev.MatchId FROM dbo.MatchEvents AS ev JOIN LatestMatches AS lm ON ev.MatchId = lm.Id WHERE ev.MatchHostDeviceId IS NOT NULL
                    UNION
                    SELECT DISTINCT ev.ShooterMatchHostDeviceId1 AS PlayerId, ev.MatchId FROM dbo.MatchEvents AS ev JOIN LatestMatches AS lm ON ev.MatchId = lm.Id WHERE ev.ShooterMatchHostDeviceId1 IS NOT NULL
                ), PlayersInMatch AS (
                    SELECT p.Id AS PlayerId, p.PlayerName, t.Name AS TeamName, ap.MatchId FROM AllPlayerIDs AS ap
                    JOIN dbo.MatchHostDevices AS p ON ap.PlayerId = p.Id
                    LEFT JOIN dbo.MatchTeamRoles AS r ON p.MatchTeamRoleId = r.Id
                    LEFT JOIN dbo.MatchTeams AS t ON r.MatchTeamId = t.Id
                ), Kills AS (
                    SELECT ShooterMatchHostDeviceId1 AS PlayerId, MatchId, COUNT(*) AS TotalKills FROM dbo.MatchEvents
                    WHERE MatchId IN (SELECT Id FROM LatestMatches) AND Discriminator = 'MatchEventKilled' GROUP BY ShooterMatchHostDeviceId1, MatchId
                ), Deaths AS (
                    SELECT MatchHostDeviceId AS PlayerId, MatchId, COUNT(*) AS TotalDeaths FROM dbo.MatchEvents
                    WHERE MatchId IN (SELECT Id FROM LatestMatches) AND Discriminator = 'MatchEventKilled' GROUP BY MatchHostDeviceId, MatchId
                )
                SELECT P.PlayerName AS 'Gracz', ISNULL(P.TeamName, 'Brak Drużyny') AS 'Drużyna',
                    ISNULL(K.TotalKills, 0) AS 'Zabojstwa', ISNULL(D.TotalDeaths, 0) AS 'Smierci',
                    P.MatchId, m.Name AS 'MatchName', m.Created AS 'MatchTime'
                FROM PlayersInMatch AS P
                JOIN LatestMatches AS m ON P.MatchId = m.Id
                LEFT JOIN Kills AS K ON P.PlayerId = K.PlayerId AND P.MatchId = K.MatchId
                LEFT JOIN Deaths AS D ON P.PlayerId = D.PlayerId AND P.MatchId = D.MatchId;
            ";

            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                SetStatus("Krok 3/4: Pobieranie statystyk...");
                await using (SqlCommand command = new SqlCommand(sqlQuery, connection))
                {
                    command.Parameters.AddWithValue("@FetchCount", roundsToFetch);
                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            statsList.Add(new PlayerStats
                            {
                                MatchId = reader["MatchId"].ToString(),
                                MatchName = reader["MatchName"]?.ToString() ?? string.Empty,
                                MatchTime = (DateTime)reader["MatchTime"],
                                Gracz = reader["Gracz"].ToString(),
                                Drużyna = reader["Drużyna"].ToString(),
                                Zabojstwa = (int)reader["Zabojstwa"],
                                Smierci = (int)reader["Smierci"]
                            });
                        }
                    }
                }
            }
            SetStatus("Krok 4/4: Czyszczenie...");
            await DetachDatabaseAsync(tempDbName);
            return statsList;
        }

        // --- 6. FUNKCJE POMOCNICZE (Bez zmian) ---
        private List<PlayerStats> GetSummedStats()
        {
            if (_allPlayerStats == null || _allPlayerStats.Count == 0)
                return new List<PlayerStats>();

            return _allPlayerStats
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

        private void DisplayStatsInGrids(List<PlayerStats> stats, string teamAName = null, string teamBName = null)
        {
            var teamNames = stats.Select(s => s.Drużyna).Distinct().ToList();

            TeamAName.Text = teamAName ?? (teamNames.Count > 0 ? teamNames[0] : "Drużyna A");
            TeamAGrid.ItemsSource = null;
            if (teamNames.Count > 0)
            {
                TeamAGrid.ItemsSource = stats
                    .Where(s => s.Drużyna == teamNames[0])
                    .OrderByDescending(s => s.Zabojstwa)
                    .ToList();
            }

            TeamBName.Text = teamBName ?? (teamNames.Count > 1 ? teamNames[1] : "Drużyna B");
            TeamBGrid.ItemsSource = null;
            if (teamNames.Count > 1)
            {
                TeamBGrid.ItemsSource = stats
                    .Where(s => s.Drużyna == teamNames[1])
                    .OrderByDescending(s => s.Zabojstwa)
                    .ToList();
            }
        }

        private void SetStatus(string message)
        {
            Dispatcher.Invoke(() => { StatusTextBlock.Text = message; });
        }

        private void HandleError(string statusMessage, string messageBoxTitle)
        {
            SetStatus(statusMessage);
            MessageBox.Show(statusMessage, messageBoxTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void SetUiEnabled(bool isEnabled, string statusMessage = "")
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(statusMessage))
                    StatusTextBlock.Text = statusMessage;

                LoadStatsButton.IsEnabled = isEnabled;
                RoundsToFetchTextBox.IsEnabled = isEnabled;
                MatchesListBox.IsEnabled = isEnabled;
                SumAllButton.IsEnabled = isEnabled;
                EmailTextBox.IsEnabled = isEnabled;
                GeneratePdfButton.IsEnabled = isEnabled;
                SendEmailButton.IsEnabled = isEnabled;
            });
        }

        private async Task DetachDatabaseAsync(string dbName)
        {
            try
            {
                string masterConnString = $"Server={ServerName};Database=master;Integrated Security=True;";
                await using (SqlConnection masterConnection = new SqlConnection(masterConnString))
                {
                    await masterConnection.OpenAsync();
                    string detachQuery = $"EXEC sp_detach_db '{dbName}', 'true'";
                    await using (SqlCommand detachCommand = new SqlCommand(detachQuery, masterConnection))
                    {
                        await detachCommand.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Ostrzeżenie: Nie można było automatycznie odłączyć bazy {dbName}. ({ex.Message})");
            }
        }
    }


    // =========================================================================
    // ====== KLASA PDF (Bez zmian w stosunku do poprzedniej wersji) ======
    // =========================================================================
    public class PdfReportDocument : IDocument
    {
        private readonly List<MatchInfo> _matches;
        private readonly List<PlayerStats> _allStats;
        private readonly List<PlayerStats> _summaryStats;

        public PdfReportDocument(List<MatchInfo> matches, List<PlayerStats> allStats, List<PlayerStats> summaryStats)
        {
            _matches = matches.OrderBy(m => m.MatchTime).ToList();
            _allStats = allStats;
            _summaryStats = summaryStats;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            foreach (var match in _matches)
            {
                var statsForMatch = _allStats.Where(s => s.MatchId == match.MatchId).ToList();

                container.Page(page =>
                {
                    page.Margin(30);
                    ComposeMatchPage(page, match, statsForMatch);
                });
            }

            container.Page(page =>
            {
                page.Margin(30);
                ComposeSummaryPage(page, _summaryStats);
            });
        }

        private void ComposeMatchPage(PageDescriptor page, MatchInfo match, List<PlayerStats> stats)
        {
            page.Header().Element(container => ComposeHeader(container, match.MatchName, match.MatchResult));
            page.Content().Element(container => ComposeStatsGrid(container, stats));
            page.Footer().Element(ComposeFooter);
        }

        private void ComposeSummaryPage(PageDescriptor page, List<PlayerStats> summaryStats)
        {
            page.Header().Element(container => ComposeHeader(container, "Suma Wszystkich Meczy", "Całkowite Statystyki"));
            page.Content().Element(container => ComposeStatsGrid(container, summaryStats));
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

        private void ComposeStatsGrid(IContainer container, List<PlayerStats> stats)
        {
            var teamNames = stats.Select(s => s.Drużyna).Distinct().ToList();
            var teamAStats = (teamNames.Count > 0)
                ? stats.Where(s => s.Drużyna == teamNames[0]).OrderByDescending(s => s.Zabojstwa).ToList()
                : new List<PlayerStats>();
            var teamBStats = (teamNames.Count > 1)
                ? stats.Where(s => s.Drużyna == teamNames[1]).OrderByDescending(s => s.Zabojstwa).ToList()
                : new List<PlayerStats>();

            var teamAName = (teamNames.Count > 0) ? teamNames[0] : "Drużyna A";
            var teamBName = (teamNames.Count > 1) ? teamNames[1] : "Drużyna B";

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

        private void ComposeStatsTable(IContainer container, List<PlayerStats> stats)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(50);
                });

                table.Header(header =>
                {
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(2).Text("Gracz").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().Text("Zabójstwa").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().Text("Śmierci").Bold();
                    header.Cell().Border(1).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().Text("K/D").Bold();
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