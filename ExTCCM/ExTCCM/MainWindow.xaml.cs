using System;
using System.Collections.Generic;
using System.IO;
using System.Linq; // <-- WAŻNE: Dodaliśmy 'Linq' do sortowania
using System.Threading.Tasks;
using System.Windows;
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

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(WorkDbPath);
        }

        // Klasa PlayerStats (bez zmian)
        public class PlayerStats
        {
            public string Gracz { get; set; }
            public string Drużyna { get; set; }
            public int Zabojstwa { get; set; }
            public int Smierci { get; set; }
        }

        // --- 2. GŁÓWNA LOGIKA APLIKACJI (ZMIANY) ---
        private async void LoadStatsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadStatsButton.IsEnabled = false;
            // Wyczyść obie tabele
            TeamAGrid.ItemsSource = null;
            TeamBGrid.ItemsSource = null;
            TeamAName.Text = "Drużyna A"; // Resetuj nazwy
            TeamBName.Text = "Drużyna B";
            SetStatus("Rozpoczynanie...");

            try
            {
                // Krok 1: Pobierz JEDNĄ listę wszystkich statystyk
                var allStats = await LoadStatsAsync();

                // Krok 2: Podziel statystyki na drużyny
                var teamNames = allStats.Select(s => s.Drużyna).Distinct().ToList();

                if (teamNames.Count > 0)
                {
                    // Przypisz pierwszą drużynę do lewej tabeli
                    string teamAName = teamNames[0];
                    TeamAName.Text = teamAName; // Ustaw etykietę
                    TeamAGrid.ItemsSource = allStats
                        .Where(s => s.Drużyna == teamAName)
                        .OrderByDescending(s => s.Zabojstwa) // Sortuj
                        .ToList();
                }

                if (teamNames.Count > 1)
                {
                    // Przypisz drugą drużynę do prawej tabeli
                    string teamBName = teamNames[1];
                    TeamBName.Text = teamBName; // Ustaw etykietę
                    TeamBGrid.ItemsSource = allStats
                        .Where(s => s.Drużyna == teamBName)
                        .OrderByDescending(s => s.Zabojstwa) // Sortuj
                        .ToList();
                }

                SetStatus($"Gotowe! Pomyślnie załadowano statystyki dla {allStats.Count} graczy.");
            }
            catch (IOException ioEx)
            {
                SetStatus($"BŁĄD: Nie można skopiować plików. Upewnij się, że program iCombat jest ZAMKNIĘTY. ({ioEx.Message})");
                MessageBox.Show("Nie można skopiować plików bazy danych.\n\nUpewnij się, że program iCombat jest całkowicie zamknięty i spróbuj ponownie.", "Błąd Kopiowania", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                SetStatus($"Krytyczny błąd: {ex.Message}");
                MessageBox.Show($"Wystąpił nieoczekiwany błąd:\n\n{ex.Message}", "Błąd Krytyczny", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadStatsButton.IsEnabled = true;
            }
        }

        // Ta funkcja (LoadStatsAsync) jest DOKŁADNIE TAKA SAMA JAK POPRZEDNIO
        // Zwraca jedną listę wszystkich graczy, a my ją dzielimy wyżej
        private async Task<List<PlayerStats>> LoadStatsAsync()
        {
            // --- KROK 1: Kopiowanie plików ---
            SetStatus("Krok 1/4: Kopiowanie plików bazy danych...");
            await Task.Run(() =>
            {
                File.Copy(Path.Combine(SourceDbPath, SourceDbFile), Path.Combine(WorkDbPath, WorkDbFile), true);
                File.Copy(Path.Combine(SourceDbPath, SourceLogFile), Path.Combine(WorkDbPath, SourceLogFile), true);
            });

            // --- KROK 2: Połączenie z bazą i zapytanie SQL ---
            SetStatus("Krok 2/4: Łączenie z bazą danych...");

            var statsList = new List<PlayerStats>();
            string tempDbName = $"iCombatStatsCopy_{Guid.NewGuid():N}";
            string workDbFilePath = Path.Combine(WorkDbPath, WorkDbFile);
            string connectionString = $"Server={ServerName};Database={tempDbName};Integrated Security=True;AttachDbFileName='{workDbFilePath}';";

            // Zapytanie SQL (bez zmian w stosunku do poprzedniej wersji)
            string sqlQuery = @"
                DECLARE @LatestMatchId UNIQUEIDENTIFIER;
                SELECT TOP 1 @LatestMatchId = Id FROM dbo.Matches ORDER BY Created DESC;

                WITH AllPlayerIDs AS (
                    SELECT DISTINCT MatchHostDeviceId AS PlayerId
                    FROM dbo.MatchEvents
                    WHERE MatchId = @LatestMatchId AND MatchHostDeviceId IS NOT NULL
                    UNION
                    SELECT DISTINCT ShooterMatchHostDeviceId1 AS PlayerId
                    FROM dbo.MatchEvents
                    WHERE MatchId = @LatestMatchId AND ShooterMatchHostDeviceId1 IS NOT NULL
                ),
                PlayersInMatch AS (
                    SELECT
                        p.Id AS PlayerId,
                        p.PlayerName,
                        t.Name AS TeamName
                    FROM
                        AllPlayerIDs AS ap
                    JOIN
                        dbo.MatchHostDevices AS p ON ap.PlayerId = p.Id
                    LEFT JOIN 
                        dbo.MatchTeamRoles AS r ON p.MatchTeamRoleId = r.Id
                    LEFT JOIN
                        dbo.MatchTeams AS t ON r.MatchTeamId = t.Id
                ),
                Kills AS (
                    SELECT ShooterMatchHostDeviceId1 AS PlayerId, COUNT(*) AS TotalKills
                    FROM dbo.MatchEvents
                    WHERE MatchId = @LatestMatchId AND Discriminator = 'MatchEventKilled'
                    GROUP BY ShooterMatchHostDeviceId1
                ),
                Deaths AS (
                    SELECT MatchHostDeviceId AS PlayerId, COUNT(*) AS TotalDeaths
                    FROM dbo.MatchEvents
                    WHERE MatchId = @LatestMatchId AND Discriminator = 'MatchEventKilled'
                    GROUP BY MatchHostDeviceId
                )
                SELECT
                    P.PlayerName AS 'Gracz',
                    ISNULL(P.TeamName, 'Brak Drużyny') AS 'Drużyna',
                    ISNULL(K.TotalKills, 0) AS 'Zabojstwa',
                    ISNULL(D.TotalDeaths, 0) AS 'Smierci'
                FROM PlayersInMatch AS P
                LEFT JOIN Kills AS K ON P.PlayerId = K.PlayerId
                LEFT JOIN Deaths AS D ON P.PlayerId = D.PlayerId;
                -- Usunęliśmy sortowanie z SQL, zrobimy to w C#
            ";

            await using (SqlConnection connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync();
                SetStatus("Krok 3/4: Pobieranie statystyk...");

                await using (SqlCommand command = new SqlCommand(sqlQuery, connection))
                {
                    await using (SqlDataReader reader = await command.ExecuteReaderAsync())
                    {
                        // Odczyt (bez zmian)
                        while (await reader.ReadAsync())
                        {
                            statsList.Add(new PlayerStats
                            {
                                Gracz = reader["Gracz"].ToString(),
                                Drużyna = reader["Drużyna"].ToString(),
                                Zabojstwa = (int)reader["Zabojstwa"],
                                Smierci = (int)reader["Smierci"]
                            });
                        }
                    }
                }
            }

            // --- KROK 3: Czyszczenie (Bez zmian) ---
            SetStatus("Krok 4/4: Czyszczenie...");
            await DetachDatabaseAsync(tempDbName);

            return statsList;
        }

        // --- 3. FUNKCJE POMOCNICZE (Bez zmian) ---
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

        private void SetStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = message;
            });
        }
    }
}