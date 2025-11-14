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

        // --- 3. MODELE DANYCH (ZAKTUALIZOWANE) ---

        // Przechowuje surowy log trafień pobrany z bazy
        public class RawKillEvent
        {
            public string MatchId { get; set; }
            public string MatchName { get; set; }
            public DateTime MatchTime { get; set; }
            public string ShooterId { get; set; }
            public string ShooterName { get; set; }
            public string ShooterTeam { get; set; }
            public string VictimId { get; set; }
            public string VictimName { get; set; }
            public string VictimTeam { get; set; }
        }

        // Przechowuje podsumowane statystyki dla JEDNEGO gracza w JEDNYM meczu
        public class PlayerStats
        {
            public string MatchId { get; set; }
            public string MatchName { get; set; } // <-- DODAJ TĘ LINIĘ
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

        // Przechowuje informacje o JEDNYM meczu (dla listy po lewej)
        public class MatchInfo
        {
            public string MatchId { get; set; }
            public string MatchName { get; set; } // "Mecz 5" lub "Mecz na Cele 5"
            public string OriginalMatchName { get; set; } // "Custom Time Limit"
            public DateTime MatchTime { get; set; }
            public string MatchResult { get; set; } // "Alpha Wygrała (10-5)"

            // Właściwości dla CheckBoxa i logiki sumowania
            public bool IsSelectedForSummary { get; set; } = true;
            public bool IncludePersonalStats { get; set; } = true;
        }


        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(WorkDbPath);
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // --- 4. LOGIKA PRZYCISKÓW ---

        // Przycisk "POBIERZ" (ZAKTUALIZOWANY)
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

                // Krok 1: Pobierz SUROWY log trafień (nowa funkcja LoadStatsAsync)
                SetStatus("Krok 2/4: Pobieranie surowych danych...");
                var rawKills = await LoadStatsAsync(roundsToFetch);

                // Krok 2: Przetwórz surowe dane (nowa funkcja ProcessRawData)
                SetStatus("Krok 3/4: Analizowanie meczy...");
                var processedData = ProcessRawData(rawKills);

                _loadedMatches = processedData.Matches;
                _allPlayerStats = processedData.PlayerStats;

                // Krok 3: Wyświetl listę meczy
                MatchesListBox.ItemsSource = _loadedMatches;

                if (_loadedMatches.Count > 0)
                {
                    MatchesListBox.SelectedIndex = 0; // To automatycznie wywoła MatchesListBox_SelectionChanged
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

        // NOWA FUNKCJA: Obsługa CheckBoxa
        private void MatchSelectorCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox) return;
            if (checkBox.DataContext is not MatchInfo selectedMatch) return;

            // Logika uruchamia się TYLKO, gdy ODZNACZAMY
            if (checkBox.IsChecked == false)
            {
                MessageBoxResult result = MessageBox.Show(
                    $"Czy statystyki osobiste (Trafienia/Śmierci) z meczu '{selectedMatch.MatchName}' mają być wliczone do ogólnej sumy?",
                    "Wybierz opcję",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    selectedMatch.IsSelectedForSummary = false;
                    selectedMatch.IncludePersonalStats = true;
                }
                else if (result == MessageBoxResult.No)
                {
                    selectedMatch.IsSelectedForSummary = false;
                    selectedMatch.IncludePersonalStats = false;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // Anuluj odznaczenie
                    checkBox.IsChecked = true;
                    selectedMatch.IsSelectedForSummary = true;
                    selectedMatch.IncludePersonalStats = true;
                }
            }
            else
            {
                // Zaznaczanie z powrotem
                selectedMatch.IsSelectedForSummary = true;
                selectedMatch.IncludePersonalStats = true;
            }
        }

        // ZAKTUALIZOWANA FUNKCJA: Przycisk "SUMUJ WSZYSTKO"
        private void SumAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allPlayerStats == null || _allPlayerStats.Count == 0) return;

            SetStatus("Sumowanie statystyk ze wszystkich pobranych meczy...");

            // Krok 1: Filtruj mecze do WYNIKÓW DRUŻYNOWYCH
            var matchesForTeamSummary = _loadedMatches
                .Where(m => m.IsSelectedForSummary)
                .ToList();

            // Krok 2: Filtruj ID meczy do STATYSTYK OSOBISTYCH
            var matchIdsForPersonalStats = _loadedMatches
                .Where(m => m.IsSelectedForSummary || m.IncludePersonalStats)
                .Select(m => m.MatchId)
                .ToList();

            // Filtruj główną listę statystyk
            var personalStatsToSum = _allPlayerStats
                .Where(stat => matchIdsForPersonalStats.Contains(stat.MatchId))
                .ToList();

            // Krok 3: Oblicz zsumowane statystyki osobiste
            var summedStats = GetSummedStats(personalStatsToSum);

            // Krok 4: Znajdź nazwy drużyn
            var teamNames = _allPlayerStats.Select(s => s.Drużyna).Distinct().ToList();
            string teamAName = teamNames.FirstOrDefault(n => n != "Brak Drużyny") ?? "Drużyna A";
            string teamBName = teamNames.FirstOrDefault(n => n != "Brak Drużyny" && n != teamAName) ?? "Drużyna B";

            // Krok 5: Oblicz sumy trafień (z już przefiltrowanej i zsumowanej listy)
            int teamAKills = summedStats.Where(s => s.Drużyna == teamAName).Sum(s => s.Zabojstwa);
            int teamBKills = summedStats.Where(s => s.Drużyna == teamBName).Sum(s => s.Zabojstwa);

            // Krok 6: Oblicz wygrane (używając przefiltrowanej listy meczy)
            int teamAWins = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamAName));
            int teamBWins = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamBName));
            int draws = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith("Remis"));

            // Krok 7: Zbuduj nagłówki
            string line1 = $"Suma: {teamAName} ({teamAWins} wygranych) vs {teamBName} ({teamBWins} wygranych)";
            string line2 = $"(Remisy: {draws})";
            string line3 = $"Całkowite trafienia: {teamAKills} - {teamBKills}";

            SummaryLine1TextBlock.Text = line1;
            SummaryLine1TextBlock.Visibility = Visibility.Visible;
            SummaryLine2TextBlock.Text = line2;
            SummaryLine2TextBlock.Visibility = (draws > 0) ? Visibility.Visible : Visibility.Collapsed;
            SummaryLine3TextBlock.Text = line3;
            SummaryLine3TextBlock.Visibility = Visibility.Visible;

            // Krok 8: Wyświetl tabele, UKRYWAJĄC BAZY
            DisplayStatsInGrids(summedStats, $"Suma - {teamAName}", $"Suma - {teamBName}", excludeBases: true);
            MatchesListBox.SelectedIndex = -1;
        }

        // ZAKTUALIZOWANA FUNKCJA: Akcja Wyboru Meczu
        private void MatchesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MatchesListBox.SelectedItem == null) return;

            var selectedMatch = (MatchInfo)MatchesListBox.SelectedItem;

            // Filtruj statystyki tylko dla tego meczu
            var filteredStats = _allPlayerStats
                .Where(s => s.MatchId == selectedMatch.MatchId)
                .ToList();

            SetStatus($"Wyświetlanie statystyk dla: {selectedMatch.OriginalMatchName} ({selectedMatch.MatchName})");

            // Ustaw nagłówek
            SummaryLine1TextBlock.Text = selectedMatch.MatchResult;
            SummaryLine1TextBlock.Visibility = Visibility.Visible;
            SummaryLine2TextBlock.Visibility = Visibility.Collapsed;
            SummaryLine3TextBlock.Visibility = Visibility.Collapsed;

            // Wyświetl tabele, POKAZUJĄC BAZY
            DisplayStatsInGrids(filteredStats, null, null, excludeBases: false);
        }

        // ZAKTUALIZOWANA FUNKCJA: Przycisk "GENERUJ PDF"
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

                    // Musimy także przefiltrować dane wysyłane do PDF
                    var matchesForTeamSummary = _loadedMatches.Where(m => m.IsSelectedForSummary).ToList();

                    var matchIdsForPersonalStats = _loadedMatches
                        .Where(m => m.IsSelectedForSummary || m.IncludePersonalStats)
                        .Select(m => m.MatchId)
                        .ToList();

                    var personalStatsToSum = _allPlayerStats
                        .Where(stat => matchIdsForPersonalStats.Contains(stat.MatchId))
                        .ToList();

                    var summedStats = GetSummedStats(personalStatsToSum);

                    // Utwórz PDF z przefiltrowanymi danymi
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


        // --- 5. NOWE GŁÓWNE FUNKCJE LOGIKI ---

        // ZAKTUALIZOWANA: Pobiera teraz SUROWY log trafień
        private async Task<List<RawKillEvent>> LoadStatsAsync(int roundsToFetch)
        {
            SetStatus("Krok 1/4: Kopiowanie plików bazy danych...");
            await Task.Run(() =>
            {
                File.Copy(Path.Combine(SourceDbPath, SourceDbFile), Path.Combine(WorkDbPath, WorkDbFile), true);
                File.Copy(Path.Combine(SourceDbPath, SourceLogFile), Path.Combine(WorkDbPath, SourceLogFile), true);
            });

            SetStatus("Krok 2/4: Łączenie z bazą danych...");
            var rawKillsList = new List<RawKillEvent>();
            string tempDbName = $"iCombatStatsCopy_{Guid.NewGuid():N}";
            string workDbFilePath = Path.Combine(WorkDbPath, WorkDbFile);
            string connectionString = $"Server={ServerName};Database={tempDbName};Integrated Security=True;AttachDbFileName='{workDbFilePath}';";

            // NOWE ZAPYTANIE: Pobiera log trafień, a nie podsumowanie
            string sqlQuery = @"
                DECLARE @RoundsToFetch INT = @FetchCount;

                WITH LatestMatches AS (
                    SELECT TOP (@RoundsToFetch) Id, Name, Created
                    FROM dbo.Matches
                    ORDER BY Created DESC
                )
                SELECT
                    m.Id AS 'MatchId',
                    m.Name AS 'MatchName',
                    m.Created AS 'MatchTime',
                    
                    shooter.Id AS 'ShooterId',
                    shooter.PlayerName AS 'ShooterName',
                    shooter_team.Name AS 'ShooterTeam',
                    
                    victim.Id AS 'VictimId',
                    victim.PlayerName AS 'VictimName',
                    victim_team.Name AS 'VictimTeam'
                    
                FROM 
                    dbo.MatchEvents AS ev
                JOIN 
                    LatestMatches AS m ON ev.MatchId = m.Id
                LEFT JOIN 
                    dbo.MatchHostDevices AS shooter ON ev.ShooterMatchHostDeviceId1 = shooter.Id
                LEFT JOIN 
                    dbo.MatchTeamRoles AS shooter_role ON shooter.MatchTeamRoleId = shooter_role.Id
                LEFT JOIN 
                    dbo.MatchTeams AS shooter_team ON shooter_role.MatchTeamId = shooter_team.Id
                LEFT JOIN 
                    dbo.MatchHostDevices AS victim ON ev.MatchHostDeviceId = victim.Id
                LEFT JOIN 
                    dbo.MatchTeamRoles AS victim_role ON victim.MatchTeamRoleId = victim_role.Id
                LEFT JOIN 
                    dbo.MatchTeams AS victim_team ON victim_role.MatchTeamId = victim_team.Id
                WHERE
                    ev.Discriminator = 'MatchEventKilled';
            ";

            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                SetStatus("Krok 3/4: Pobieranie logu trafień...");
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
            SetStatus("Krok 4/4: Czyszczenie...");
            await DetachDatabaseAsync(tempDbName);
            return rawKillsList;
        }

        // NOWA FUNKCJA: Przetwarza surowe dane w listy, których potrzebuje UI
        private (List<MatchInfo> Matches, List<PlayerStats> PlayerStats) ProcessRawData(List<RawKillEvent> rawKills)
        {
            var allPlayerStats = new List<PlayerStats>();
            var allMatches = new List<MatchInfo>();

            // 1. Znajdź wszystkie unikalne ID meczy
            var groupedByMatch = rawKills.GroupBy(k => k.MatchId);

            int totalMatches = groupedByMatch.Count();
            int matchCounter = totalMatches;

            // Sortuj mecze po dacie (najnowsze pierwsze)
            foreach (var matchGroup in groupedByMatch.OrderByDescending(g => g.First().MatchTime))
            {
                var matchEvents = matchGroup.ToList();
                var firstEvent = matchEvents.First();

                // 2. Znajdź wszystkich unikalnych graczy w tym meczu
                var shooters = matchEvents.Select(e => new { Id = e.ShooterId, Name = e.ShooterName, Team = e.ShooterTeam });
                var victims = matchEvents.Select(e => new { Id = e.VictimId, Name = e.VictimName, Team = e.VictimTeam });
                var allPlayersInMatch = shooters.Concat(victims)
                                             .Where(p => p.Id != null)
                                             .GroupBy(p => p.Id)
                                             .Select(g => g.First())
                                             .ToList();

                // 3. Sprawdź, czy to "Mecz na Cele"
                bool isBaseMatch = allPlayersInMatch.Any(p => IsBase(p.Name));
                string matchDisplayName = isBaseMatch ? $"Mecz na Cele {matchCounter}" : $"Mecz {matchCounter}";
                matchCounter--;

                string teamAName = allPlayersInMatch.Select(p => p.Team).FirstOrDefault(t => t != "Brak Drużyny") ?? "Drużyna A";
                string teamBName = allPlayersInMatch.Select(p => p.Team).FirstOrDefault(t => t != "Brak Drużyny" && t != teamAName) ?? "Drużyna B";

                string matchResultString;

                if (isBaseMatch)
                {
                    // 4a. Logika "Meczu na Cele"
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
                    // 4b. Logika standardowego meczu
                    int teamAKills = matchEvents.Count(e => e.ShooterTeam == teamAName);
                    int teamBKills = matchEvents.Count(e => e.ShooterTeam == teamBName);

                    if (teamAKills > teamBKills)
                        matchResultString = $"{teamAName} Wygrała ({teamAKills}-{teamBKills})";
                    else if (teamBKills > teamAKills)
                        matchResultString = $"{teamBName} Wygrała ({teamBKills}-{teamAKills})";
                    else
                        matchResultString = $"Remis ({teamAKills}-{teamBKills})";
                }

                // 5. Stwórz wpis dla listy meczy
                allMatches.Add(new MatchInfo
                {
                    MatchId = firstEvent.MatchId,
                    MatchName = matchDisplayName,
                    OriginalMatchName = firstEvent.MatchName,
                    MatchTime = firstEvent.MatchTime,
                    MatchResult = matchResultString
                });

                // 6. Oblicz statystyki osobiste dla tego meczu
                foreach (var player in allPlayersInMatch)
                {
                    allPlayerStats.Add(new PlayerStats
                    {
                        MatchId = firstEvent.MatchId,
                        MatchName = firstEvent.MatchName, // <-- DODAJ TĘ LINIĘ
                        MatchTime = firstEvent.MatchTime, // <-- DODAJ TĘ LINIĘ
                        Gracz = player.Name,
                        Drużyna = player.Team,
                        Zabojstwa = matchEvents.Count(e => e.ShooterId == player.Id),
                        Smierci = matchEvents.Count(e => e.VictimId == player.Id)
                    });
                }
            }

            return (allMatches, allPlayerStats);
        }

        // --- 6. FUNKCJE POMOCNICZE ---

        // ZAKTUALIZOWANA: Teraz przyjmuje listę
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

        // NOWA FUNKCJA: Sprawdza, czy gracz to Baza
        private bool IsBase(string playerName)
        {
            return playerName == "Baza Daleko" || playerName == "Baza Blisko";
        }

        // ZAKTUALIZOWANA: Potrafi ukrywać bazy
        private void DisplayStatsInGrids(List<PlayerStats> stats, string teamAName = null, string teamBName = null, bool excludeBases = false)
        {
            // Filtruj listę, jeśli trzeba
            List<PlayerStats> filteredStats = excludeBases
                ? stats.Where(s => !IsBase(s.Gracz)).ToList()
                : stats;

            var teamNames = filteredStats.Select(s => s.Drużyna).Distinct().ToList();

            TeamAName.Text = teamAName ?? (teamNames.Count > 0 ? teamNames[0] : "Drużyna A");
            TeamAGrid.ItemsSource = null;
            if (teamNames.Count > 0)
            {
                TeamAGrid.ItemsSource = filteredStats
                    .Where(s => s.Drużyna == teamNames[0])
                    .OrderByDescending(s => s.Zabojstwa)
                    .ToList();
            }

            TeamBName.Text = teamBName ?? (teamNames.Count > 1 ? teamNames[1] : "Drużyna B");
            TeamBGrid.ItemsSource = null;
            if (teamNames.Count > 1)
            {
                TeamBGrid.ItemsSource = filteredStats
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
    // ====== KLASA PDF (ZAKTUALIZOWANA DLA NOWEJ LOGIKI) ======
    // =========================================================================
    public class PdfReportDocument : IDocument
    {
        private readonly List<MatchInfo> _matchesForTeamSummary;
        private readonly List<PlayerStats> _allPlayerStats; // Teraz przekazujemy pełną listę
        private readonly List<PlayerStats> _summaryStats; // To są już zsumowane statystyki osobiste

        public PdfReportDocument(List<MatchInfo> matches, List<PlayerStats> allStats, List<PlayerStats> summaryStats)
        {
            // Przekazujemy przefiltrowane listy
            _matchesForTeamSummary = matches.OrderBy(m => m.MatchTime).ToList();
            _allPlayerStats = allStats; // Pełna lista statystyk
            _summaryStats = summaryStats; // Wstępnie przefiltrowana i zsumowana lista
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            // Będziemy iterować po liście meczy (która zawiera już logikę "Mecz na Cele")
            // Ale musimy też wyświetlić mecze, które są odznaczone, ale mają 'IncludePersonalStats'

            var matchesToDisplay = _allPlayerStats
                .Select(s => s.MatchId)
                .Distinct()
                .ToList();

            var sortedMatchInfos = matchesToDisplay
                .Select(id => _matchesForTeamSummary.FirstOrDefault(m => m.MatchId == id)
                             // Jeśli mecz został całkowicie odfiltrowany, stwórz tymczasowy
                             ?? new MatchInfo { MatchId = id, MatchName = "Mecz (wykluczony)", MatchResult = "Wykluczony", MatchTime = _allPlayerStats.First(s => s.MatchId == id).MatchTime })
                .OrderBy(m => m.MatchTime)
                .ToList();


            foreach (var match in sortedMatchInfos)
            {
                var statsForMatch = _allPlayerStats.Where(s => s.MatchId == match.MatchId).ToList();

                container.Page(page =>
                {
                    page.Margin(30);
                    // Pokaż Bazy na stronach pojedynczych meczy
                    ComposeMatchPage(page, match.MatchName, match.MatchResult, statsForMatch, false);
                });
            }

            container.Page(page =>
            {
                page.Margin(30);
                // Ukryj Bazy na stronie podsumowania
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
            // --- Nagłówek ---
            page.Header().Element(container =>
            {
                // Obliczamy te dane na nowo na podstawie przefiltrowanych list
                var teamNames = _allPlayerStats.Select(s => s.Drużyna).Distinct().ToList();
                string teamAName = teamNames.FirstOrDefault(n => n != "Brak Drużyny") ?? "Drużyna A";
                string teamBName = teamNames.FirstOrDefault(n => n != "Brak Drużyny" && n != teamAName) ?? "Drużyna B";

                // Użyj 'summaryStats' (już zsumowane statystyki osobiste, BEZ BAZ)
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
                        col.Item().AlignCenter().Text($"(Remisy: {draws})").FontSize(14);

                    col.Item().AlignCenter().Text($"Całkowite trafienia: {teamAKills} - {teamBKills}").FontSize(14);
                    col.Item().PaddingVertical(10);
                });
            });

            // Przekaż 'excludeBases: true', aby ukryć bazy na stronie podsumowania
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

        // ZAKTUALIZOWANA: Dodano 'excludeBases'
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

        // ZAKTUALIZOWANA: Dodano helper IsBase
        private bool IsBase(string playerName)
        {
            return playerName == "Baza Daleko" || playerName == "Baza Blisko";
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