using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;

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

        // --- 2. GLOBALNA LISTA (Bez zmian) ---
        private List<PlayerStats> _allPlayerStats = new List<PlayerStats>();

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(WorkDbPath);
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

            // ========= NOWA WŁAŚCIWOŚĆ (K/D RATIO) ==================
            // Ta właściwość sama obliczy K/D na podstawie zabójstw i śmierci
            // Nie musimy zmieniać nic więcej w kodzie!
            public double KDRatio
            {
                get
                {
                    // Jeśli gracz ma śmierci, oblicz normalnie
                    if (Smierci > 0)
                    {
                        return (double)Zabojstwa / Smierci;
                    }

                    // Jeśli gracz nie ma śmierci, ale ma zabójstwa (np. 5-0)
                    // Pokażemy jego zabójstwa jako ratio (np. 5.00)
                    if (Zabojstwa > 0)
                    {
                        return Zabojstwa;
                    }

                    // Jeśli 0 zabójstw i 0 śmierci
                    return 0;
                }
            }
            // ========================================================
        }

        public class MatchInfo
        {
            public string MatchId { get; set; }
            public string MatchName { get; set; }
            public string OriginalMatchName { get; set; }
            public DateTime MatchTime { get; set; }
            public string MatchResult { get; set; }
        }

        // --- 4. LOGIKA PRZYCISKÓW (Bez zmian) ---

        // Przycisk "POBIERZ"
        private async void LoadStatsButton_Click(object sender, RoutedEventArgs e)
        {
            SetUiEnabled(false, "Rozpoczynanie...");
            TeamAGrid.ItemsSource = null;
            TeamBGrid.ItemsSource = null;
            MatchesListBox.ItemsSource = null;
            MatchResultTextBlock.Text = "";
            _allPlayerStats.Clear();

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

                var matchesForListBox = matchesFromStats
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
                            {
                                matchResultString = $"{teamA.TeamName} Wygrała ({teamA.TotalKills}-{teamB.TotalKills})";
                            }
                            else if (teamB.TotalKills > teamA.TotalKills)
                            {
                                matchResultString = $"{teamB.TeamName} Wygrała ({teamB.TotalKills}-{teamA.TotalKills})";
                            }
                            else
                            {
                                matchResultString = $"Remis ({teamA.TotalKills}-{teamB.TotalKills})";
                            }
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

                MatchesListBox.ItemsSource = matchesForListBox;

                if (matchesForListBox.Count > 0)
                {
                    MatchesListBox.SelectedIndex = 0;
                }

                SetStatus($"Gotowe! Pomyślnie załadowano {matchesForListBox.Count} meczy.");
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

        // Przycisk "SUMUJ WSZYSTKO"
        private void SumAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allPlayerStats == null || _allPlayerStats.Count == 0) return;

            SetStatus("Sumowanie statystyk ze wszystkich pobranych meczy...");
            MatchResultTextBlock.Text = "Suma Wszystkich Meczy";

            var summedStats = _allPlayerStats
                .GroupBy(s => new { s.Gracz, s.Drużyna })
                .Select(group => new PlayerStats
                {
                    Gracz = group.Key.Gracz,
                    Drużyna = group.Key.Drużyna,
                    Zabojstwa = group.Sum(s => s.Zabojstwa),
                    Smierci = group.Sum(s => s.Smierci)
                    // KDRatio zostanie obliczone automatycznie dla zsumowanych wartości
                })
                .ToList();

            DisplayStatsInGrids(summedStats, "Suma - Drużyna A", "Suma - Drużyna B");
            MatchesListBox.SelectedIndex = -1;
        }

        // Akcja Wyboru Meczu
        private void MatchesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MatchesListBox.SelectedItem == null) return;

            var selectedMatch = (MatchInfo)MatchesListBox.SelectedItem;

            var filteredStats = _allPlayerStats
                .Where(s => s.MatchId == selectedMatch.MatchId)
                .ToList();

            SetStatus($"Wyświetlanie statystyk dla: {selectedMatch.OriginalMatchName} ({selectedMatch.MatchName})");
            MatchResultTextBlock.Text = selectedMatch.MatchResult;
            DisplayStatsInGrids(filteredStats);
        }


        // --- 5. GŁÓWNA FUNKCJA POBIERANIA DANYCH (Bez zmian) ---
        // Nie musimy jej zmieniać, ponieważ KDRatio jest obliczane w klasie PlayerStats
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
                    SELECT TOP (@RoundsToFetch) Id, Name, Created
                    FROM dbo.Matches
                    ORDER BY Created DESC
                ),
                AllPlayerIDs AS (
                    SELECT DISTINCT ev.MatchHostDeviceId AS PlayerId, ev.MatchId
                    FROM dbo.MatchEvents AS ev
                    JOIN LatestMatches AS lm ON ev.MatchId = lm.Id
                    WHERE ev.MatchHostDeviceId IS NOT NULL
                    UNION
                    SELECT DISTINCT ev.ShooterMatchHostDeviceId1 AS PlayerId, ev.MatchId
                    FROM dbo.MatchEvents AS ev
                    JOIN LatestMatches AS lm ON ev.MatchId = lm.Id
                    WHERE ev.ShooterMatchHostDeviceId1 IS NOT NULL
                ),
                PlayersInMatch AS (
                    SELECT
                        p.Id AS PlayerId, p.PlayerName, t.Name AS TeamName, ap.MatchId
                    FROM AllPlayerIDs AS ap
                    JOIN dbo.MatchHostDevices AS p ON ap.PlayerId = p.Id
                    LEFT JOIN dbo.MatchTeamRoles AS r ON p.MatchTeamRoleId = r.Id
                    LEFT JOIN dbo.MatchTeams AS t ON r.MatchTeamId = t.Id
                ),
                Kills AS (
                    SELECT ShooterMatchHostDeviceId1 AS PlayerId, MatchId, COUNT(*) AS TotalKills
                    FROM dbo.MatchEvents
                    WHERE MatchId IN (SELECT Id FROM LatestMatches) AND Discriminator = 'MatchEventKilled'
                    GROUP BY ShooterMatchHostDeviceId1, MatchId
                ),
                Deaths AS (
                    SELECT MatchHostDeviceId AS PlayerId, MatchId, COUNT(*) AS TotalDeaths
                    FROM dbo.MatchEvents
                    WHERE MatchId IN (SELECT Id FROM LatestMatches) AND Discriminator = 'MatchEventKilled'
                    GROUP BY MatchHostDeviceId, MatchId
                )
                SELECT
                    P.PlayerName AS 'Gracz',
                    ISNULL(P.TeamName, 'Brak Drużyny') AS 'Drużyna',
                    ISNULL(K.TotalKills, 0) AS 'Zabojstwa',
                    ISNULL(D.TotalDeaths, 0) AS 'Smierci',
                    P.MatchId,
                    m.Name AS 'MatchName',
                    m.Created AS 'MatchTime'
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
                                // KDRatio nie jest tutaj potrzebne, obliczy się samo
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
                {
                    StatusTextBlock.Text = statusMessage;
                }
                LoadStatsButton.IsEnabled = isEnabled;
                RoundsToFetchTextBox.IsEnabled = isEnabled;
                MatchesListBox.IsEnabled = isEnabled;
                SumAllButton.IsEnabled = isEnabled;
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
}