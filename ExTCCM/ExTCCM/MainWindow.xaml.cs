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

        // --- 3. MODELE DANYCH (ZAKTUALIZOWANE) ---
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

        // ZAKTUALIZOWANA KLASA MatchInfo
        public class MatchInfo
        {
            public string MatchId { get; set; }
            public string MatchName { get; set; }
            public string OriginalMatchName { get; set; }
            public DateTime MatchTime { get; set; }
            public string MatchResult { get; set; }

            // NOWE WŁAŚCIWOŚCI DO KONTROLOWANIA SUMY
            public bool IsSelectedForSummary { get; set; } = true; // Domyślnie zaznaczone
            public bool IncludePersonalStats { get; set; } = true; // Domyślnie wliczaj staty
        }

        // --- 4. LOGIKA PRZYCISKÓW ---

        // Przycisk "POBIERZ" (Bez zmian)
        private async void LoadStatsButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiEnabled(false, "Rozpoczynanie...");
            TeamAGrid.ItemsSource = null;
            TeamBGrid.ItemsSource = null;
            MatchesListBox.ItemsSource = null;
            SummaryLine1TextBlock.Text = "Wybierz mecz z listy";
            SummaryLine2TextBlock.Visibility = Visibility.Collapsed;
            SummaryLine3TextBlock.Visibility = Visibility.Collapsed;
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
                            // IsSelectedForSummary i IncludePersonalStats są domyślnie 'true'
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

        // ========================================================
        // ====== NOWA FUNKCJA: Obsługa CheckBoxa ======
        // ========================================================
        private void MatchSelectorCheckBox_Click(object sender, RoutedEventArgs e)
        {
            // Pobierz CheckBox, który został kliknięty
            if (sender is not CheckBox checkBox) return;

            // Znajdź obiekt MatchInfo powiązany z tym CheckBoxem
            if (checkBox.DataContext is not MatchInfo selectedMatch) return;

            // Logika uruchamia się TYLKO, gdy ODZNACZAMY checkbox
            if (checkBox.IsChecked == false)
            {
                // Wyświetl okno dialogowe z pytaniem
                MessageBoxResult result = MessageBox.Show(
                    $"Czy statystyki osobiste (Trafienia/Śmierci) z meczu '{selectedMatch.MatchName}' mają być wliczone do ogólnej sumy?",
                    "Wybierz opcję",
                    MessageBoxButton.YesNoCancel, // Daje Tak, Nie, Anuluj
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    // "Tak" - wliczaj statystyki osobiste, ale nie wynik drużynowy
                    selectedMatch.IsSelectedForSummary = false; // Pozostaw odznaczone
                    selectedMatch.IncludePersonalStats = true;
                }
                else if (result == MessageBoxResult.No)
                {
                    // "Nie" - nie wliczaj ani statystyk osobistych, ani drużynowych
                    selectedMatch.IsSelectedForSummary = false; // Pozostaw odznaczone
                    selectedMatch.IncludePersonalStats = false;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // "Anuluj" - przywróć CheckBox do stanu zaznaczonego
                    checkBox.IsChecked = true;
                    selectedMatch.IsSelectedForSummary = true;
                    selectedMatch.IncludePersonalStats = true;
                }
            }
            else
            {
                // Jeśli ZAZNACZAMY z powrotem, po prostu zresetuj wszystko do 'true'
                selectedMatch.IsSelectedForSummary = true;
                selectedMatch.IncludePersonalStats = true;
            }
        }

        // ========================================================
        // ====== ZAKTUALIZOWANA FUNKCJA: Przycisk "SUMUJ WSZYSTKO" ======
        // ========================================================
        private void SumAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allPlayerStats == null || _allPlayerStats.Count == 0) return;

            SetStatus("Sumowanie statystyk ze wszystkich pobranych meczy...");

            // === POCZĄTEK ZMIAN ===

            // Krok 1: Filtruj mecze, które wliczamy do WYNIKÓW DRUŻYNOWYCH
            // Bierzemy tylko te, które mają zaznaczony CheckBox
            var matchesForTeamSummary = _loadedMatches
                .Where(m => m.IsSelectedForSummary)
                .ToList();

            // Krok 2: Filtruj mecze, które wliczamy do STATYSTYK OSOBISTYCH
            // Bierzemy te zaznaczone LUB te odznaczone z opcją "Tak"
            var matchIdsForPersonalStats = _loadedMatches
                .Where(m => m.IsSelectedForSummary || m.IncludePersonalStats)
                .Select(m => m.MatchId)
                .ToList();

            // Filtruj główną listę statystyk
            var personalStatsToSum = _allPlayerStats
                .Where(stat => matchIdsForPersonalStats.Contains(stat.MatchId))
                .ToList();

            // === KONIEC FILTROWANIA ===

            // Krok 3: Oblicz statystyki osobiste (używając przefiltrowanej listy)
            var summedStats = GetSummedStats(personalStatsToSum); // Przekaż przefiltrowaną listę

            // Krok 4: Znajdź nazwy drużyn (używając pełnej listy, na wszelki wypadek)
            var teamNames = _allPlayerStats.Select(s => s.Drużyna).Distinct().ToList();
            string teamAName = (teamNames.Count > 0) ? teamNames[0] : "Drużyna A";
            string teamBName = (teamNames.Count > 1) ? teamNames[1] : "Drużyna B";

            // Krok 5: Oblicz sumy trafień (używając zsumowanych statystyk osobistych)
            int teamAKills = summedStats.Where(s => s.Drużyna == teamAName).Sum(s => s.Zabojstwa);
            int teamBKills = summedStats.Where(s => s.Drużyna == teamBName).Sum(s => s.Zabojstwa);

            // Krok 6: Oblicz wygrane (używając przefiltrowanej listy meczy)
            int teamAWins = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamAName));
            int teamBWins = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamBName));
            int draws = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith("Remis"));

            // Krok 7: Zbuduj nagłówki (bez zmian)
            string line1 = $"Suma: {teamAName} ({teamAWins} wygranych) vs {teamBName} ({teamBWins} wygranych)";
            string line2 = $"(Remisy: {draws})";
            string line3 = $"Całkowite trafienia: {teamAKills} - {teamBKills}";

            SummaryLine1TextBlock.Text = line1;
            SummaryLine1TextBlock.Visibility = Visibility.Visible;

            SummaryLine2TextBlock.Text = line2;
            SummaryLine2TextBlock.Visibility = (draws > 0) ? Visibility.Visible : Visibility.Collapsed;

            SummaryLine3TextBlock.Text = line3;
            SummaryLine3TextBlock.Visibility = Visibility.Visible;

            DisplayStatsInGrids(summedStats, $"Suma - {teamAName}", $"Suma - {teamBName}");
            MatchesListBox.SelectedIndex = -1;
        }

        // Akcja Wyboru Meczu (Bez zmian)
        private void MatchesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MatchesListBox.SelectedItem == null) return;
            var selectedMatch = (MatchInfo)MatchesListBox.SelectedItem;
            var filteredStats = _allPlayerStats.Where(s => s.MatchId == selectedMatch.MatchId).ToList();
            SetStatus($"Wyświetlanie statystyk dla: {selectedMatch.OriginalMatchName} ({selectedMatch.MatchName})");
            SummaryLine1TextBlock.Text = selectedMatch.MatchResult;
            SummaryLine1TextBlock.Visibility = Visibility.Visible;
            SummaryLine2TextBlock.Visibility = Visibility.Collapsed;
            SummaryLine3TextBlock.Visibility = Visibility.Collapsed;
            DisplayStatsInGrids(filteredStats);
        }

        // Przycisk "GENERUJ PDF"
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

                    // ===========================================
                    // ====== ZMIANA DLA PDF ======
                    // Musimy także przefiltrować dane wysyłane do PDF

                    // 1. Filtruj mecze do podsumowania drużynowego
                    var matchesForTeamSummary = _loadedMatches.Where(m => m.IsSelectedForSummary).ToList();

                    // 2. Filtruj statystyki osobiste
                    var matchIdsForPersonalStats = _loadedMatches
                        .Where(m => m.IsSelectedForSummary || m.IncludePersonalStats)
                        .Select(m => m.MatchId)
                        .ToList();

                    var personalStatsToSum = _allPlayerStats
                        .Where(stat => matchIdsForPersonalStats.Contains(stat.MatchId))
                        .ToList();

                    // 3. Oblicz zsumowane statystyki osobiste
                    var summedStats = GetSummedStats(personalStatsToSum);

                    // 4. Utwórz PDF z przefiltrowanymi danymi
                    var pdfDocument = new PdfReportDocument(matchesForTeamSummary, personalStatsToSum, summedStats);
                    // ===========================================

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

        // Przycisk "WYŚLIJ EMAIL" (Bez zmian)
        private void SendEmailButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastGeneratedPdfPath) || !File.Exists(_lastGeneratedPdfPath))
            {
                MessageBox.Show("Najpierw musisz wygenerować raport PDF za pomocą przycisku '1. Generuj Raport PDF'.", "Brak Pliku", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string email = EmailTextBox.Text;
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                MessageBox.Show("Wprowadź poprawny adres e-mail odbiorcy.", "Brak Adresu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                SetStatus("Otwieranie klienta poczty...");
                string subject = System.Web.HttpUtility.UrlEncode("Raport Statystyk iCombat");
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"mailto:{email}?subject={subject}",
                    UseShellExecute = true
                });
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

        // --- 6. FUNKCJE POMOCNICZE ---

        // ===========================================
        // ====== ZAKTUALIZOWANA FUNKCJA GetSummedStats ======
        // ===========================================
        // Teraz przyjmuje listę jako parametr
        private List<PlayerStats> GetSummedStats(List<PlayerStats> statsToSum)
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
    // ====== KLASA PDF (ZMIANA W KONSTRUKTORZE I ComposeSummaryPage) ======
    // =========================================================================
    public class PdfReportDocument : IDocument
    {
        private readonly List<MatchInfo> _matchesForTeamSummary; // <-- Zmieniona lista
        private readonly List<PlayerStats> _statsForPersonalSummary; // <-- Zmieniona lista
        private readonly List<PlayerStats> _summaryStats;

        // ZAKTUALIZOWANY KONSTRUKTOR
        public PdfReportDocument(List<MatchInfo> matches, List<PlayerStats> allStats, List<PlayerStats> summaryStats)
        {
            // Przekazujemy przefiltrowane listy
            _matchesForTeamSummary = matches.OrderBy(m => m.MatchTime).ToList();
            _statsForPersonalSummary = allStats;
            _summaryStats = summaryStats;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            // Iteruj po meczach, które mają być wliczane do statystyk osobistych
            // (aby nie pominąć meczu z odznaczoną drużyną, ale wliczonymi statami)
            var matchesToDisplay = _statsForPersonalSummary
                .Select(s => new { s.MatchId, s.MatchName, s.MatchTime })
                .Distinct()
                .OrderBy(m => m.MatchTime)
                .ToList();

            foreach (var match in matchesToDisplay)
            {
                // Znajdź oryginalny obiekt MatchInfo (dla wyniku)
                var originalMatchInfo = _matchesForTeamSummary.FirstOrDefault(m => m.MatchId == match.MatchId);
                string matchResult = originalMatchInfo?.MatchResult ?? "Wynik wykluczony z sumy";
                string matchName = originalMatchInfo?.MatchName ?? $"Mecz ({match.MatchId.Substring(0, 4)}...)";

                var statsForMatch = _statsForPersonalSummary.Where(s => s.MatchId == match.MatchId).ToList();

                container.Page(page =>
                {
                    page.Margin(30);
                    ComposeMatchPage(page, matchName, matchResult, statsForMatch);
                });
            }

            container.Page(page =>
            {
                page.Margin(30);
                ComposeSummaryPage(page, _summaryStats); // Przekaż zsumowane statystyki
            });
        }

        // Zaktualizowano parametry
        private void ComposeMatchPage(PageDescriptor page, string matchName, string matchResult, List<PlayerStats> stats)
        {
            page.Header().Element(container => ComposeHeader(container, matchName, matchResult));
            page.Content().Element(container => ComposeStatsGrid(container, stats, false));
            page.Footer().Element(ComposeFooter);
        }

        // ZAKTUALIZOWANA FUNKCJA PDF
        private void ComposeSummaryPage(PageDescriptor page, List<PlayerStats> summaryStats)
        {
            // --- Nagłówek ---
            page.Header().Element(container =>
            {
                // Musimy obliczyć te dane na nowo na podstawie przefiltrowanych list
                var teamNames = _statsForPersonalSummary.Select(s => s.Drużyna).Distinct().ToList();
                string teamAName = (teamNames.Count > 0) ? teamNames[0] : "Drużyna A";
                string teamBName = (teamNames.Count > 1) ? teamNames[1] : "Drużyna B";

                // Użyj 'summaryStats' (już zsumowane statystyki osobiste)
                int teamAKills = summaryStats.Where(s => s.Drużyna == teamAName).Sum(s => s.Zabojstwa);
                int teamBKills = summaryStats.Where(s => s.Drużyna == teamBName).Sum(s => s.Zabojstwa);

                // Użyj '_matchesForTeamSummary' (przekazanej przefiltrowanej listy meczy)
                int teamAWins = _matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamAName));
                int teamBWins = _matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamBName));
                int draws = _matchesForTeamSummary.Count(m => m.MatchResult.StartsWith("Remis"));

                container.Column(col =>
                {
                    col.Item().AlignCenter().Text($"{teamAName} ({teamAWins} wygranych) vs {teamBName} ({teamBWins} wygranych)").Bold().FontSize(18);

                    if (draws > 0)
                    {
                        col.Item().AlignCenter().Text($"(Remisy: {draws})").FontSize(14);
                    }

                    col.Item().AlignCenter().Text($"Całkowite trafienia: {teamAKills} - {teamBKills}").FontSize(14);
                    col.Item().PaddingVertical(10);
                });
            });

            page.Content().Element(container => ComposeStatsGrid(container, summaryStats, true));
            page.Footer().Element(ComposeFooter);
        }

        // Nagłówek dla pojedynczego meczu (bez zmian)
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

        private void ComposeStatsGrid(IContainer container, List<PlayerStats> stats, bool isSummaryPage = false)
        {
            var teamNames = stats.Select(s => s.Drużyna).Distinct().ToList();
            var teamAStats = (teamNames.Count > 0)
                ? stats.Where(s => s.Drużyna == teamNames[0]).OrderByDescending(s => s.Zabojstwa).ToList()
                : new List<PlayerStats>();
            var teamBStats = (teamNames.Count > 1)
                ? stats.Where(s => s.Drużyna == teamNames[1]).OrderByDescending(s => s.Zabojstwa).ToList()
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

        // Nagłówki tabel (bez zmian)
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