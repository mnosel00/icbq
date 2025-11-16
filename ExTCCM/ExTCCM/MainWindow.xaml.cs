using ExTCCM.Documents;
using ExTCCM.Models;
using ExTCCM.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using QuestPDF.Fluent;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace iCombatStatsExporter
{
    public partial class MainWindow : Window
    {
        // === GŁÓWNA LOGIKA APLIKACJI ===
        private readonly StatsService _statsService;

        private List<PlayerStats> _allPlayerStats = new List<PlayerStats>();
        private List<MatchInfo> _loadedMatches = new List<MatchInfo>();
        private string _lastGeneratedPdfPath = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            Directory.CreateDirectory(@"C:\StatsTemp\");
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

            // Stwórz instancję naszego serwisu
            _statsService = new StatsService();
        }

        // Przycisk "POBIERZ"
        // W pliku MainWindow.xaml.cs

        // Przycisk "POBIERZ"
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

                SetStatus("Krok 1/2: Łączenie z bazą danych...");
                var rawKills = await _statsService.LoadStatsAsync(roundsToFetch);

                SetStatus("Krok 2/2: Analizowanie meczy...");
                var processedData = _statsService.ProcessRawData(rawKills);

                _loadedMatches = processedData.Matches;
                _allPlayerStats = processedData.PlayerStats;

                MatchesListBox.ItemsSource = _loadedMatches;

                if (_loadedMatches.Count > 0)
                {
                    MatchesListBox.SelectedIndex = 0;
                }

                SetStatus($"Gotowe! Pomyślnie załadowano {_loadedMatches.Count} meczy.");
            }
            catch (SqlException sqlEx)
            {
                // === ZMIANA TUTAJ ===
                // Nowy, poprawny komunikat błędu
                HandleError($"BŁĄD BAZY DANYCH: Nie można się połączyć.\n\n" +
                            $"UPEWNIJ SIĘ, ŻE PROGRAM 'iCombat.Bootloader' JEST URUCHOMIONY.\n\n" +
                            $"(Aplikacja musi być uruchomiona z uprawnieniami Administratora, aby 'zobaczyć' bazę iCombat).",
                    "Błąd Połączenia");
            }
            catch (Exception ex)
            {
                HandleError($"Krytyczny błąd: {ex.Message}", "Błąd Krytyczny");
            }
            finally
            {
                SetUiEnabled(_allPlayerStats.Count > 0);
            }
        }        // Obsługa CheckBoxa
        private void MatchSelectorCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox) return;
            if (checkBox.DataContext is not MatchInfo selectedMatch) return;

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
                    checkBox.IsChecked = true;
                    selectedMatch.IsSelectedForSummary = true;
                    selectedMatch.IncludePersonalStats = true;
                }
            }
            else
            {
                selectedMatch.IsSelectedForSummary = true;
                selectedMatch.IncludePersonalStats = true;
            }
        }

        // Przycisk "SUMUJ WSZYSTKO"
        private void SumAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allPlayerStats == null || _allPlayerStats.Count == 0) return;

            SetStatus("Sumowanie statystyk ze wszystkich pobranych meczy...");

            var matchesForTeamSummary = _loadedMatches.Where(m => m.IsSelectedForSummary).ToList();
            var matchIdsForPersonalStats = _loadedMatches
                .Where(m => m.IsSelectedForSummary || m.IncludePersonalStats)
                .Select(m => m.MatchId)
                .ToList();

            var personalStatsToSum = _allPlayerStats
                .Where(stat => matchIdsForPersonalStats.Contains(stat.MatchId))
                .ToList();

            var summedStats = _statsService.GetSummedStats(personalStatsToSum);

            var teamNames = _allPlayerStats.Select(s => s.Drużyna).Distinct().ToList();
            string teamAName = teamNames.FirstOrDefault(n => n != "Brak Drużyny") ?? "Drużyna A";
            string teamBName = teamNames.FirstOrDefault(n => n != "Brak Drużyny" && n != teamAName) ?? "Drużyna B";

            int teamAKills = summedStats.Where(s => s.Drużyna == teamAName).Sum(s => s.Zabojstwa);
            int teamBKills = summedStats.Where(s => s.Drużyna == teamBName).Sum(s => s.Zabojstwa);
            int teamAWins = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamAName));
            int teamBWins = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith(teamBName));
            int draws = matchesForTeamSummary.Count(m => m.MatchResult.StartsWith("Remis"));

            string line1 = $"Suma: {teamAName} ({teamAWins} wygranych) vs {teamBName} ({teamBWins} wygranych)";
            string line2 = $"(Remisy: {draws})";
            string line3 = $"Całkowite trafienia: {teamAKills} - {teamBKills}";

            SummaryLine1TextBlock.Text = line1;
            SummaryLine1TextBlock.Visibility = Visibility.Visible;
            SummaryLine2TextBlock.Text = line2;
            SummaryLine2TextBlock.Visibility = (draws > 0) ? Visibility.Visible : Visibility.Collapsed;
            SummaryLine3TextBlock.Text = line3;
            SummaryLine3TextBlock.Visibility = Visibility.Visible;

            DisplayStatsInGrids(summedStats, $"Suma - {teamAName}", $"Suma - {teamBName}", excludeBases: true);
            MatchesListBox.SelectedIndex = -1;
        }

        // Akcja Wyboru Meczu
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

            DisplayStatsInGrids(filteredStats, null, null, excludeBases: false);
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

                    var matchesForTeamSummary = _loadedMatches.Where(m => m.IsSelectedForSummary).ToList();
                    var matchIdsForPersonalStats = _loadedMatches
                        .Where(m => m.IsSelectedForSummary || m.IncludePersonalStats)
                        .Select(m => m.MatchId)
                        .ToList();
                    var personalStatsToSum = _allPlayerStats
                        .Where(stat => matchIdsForPersonalStats.Contains(stat.MatchId))
                        .ToList();

                    var summedStats = _statsService.GetSummedStats(personalStatsToSum);

                    var pdfDocument = new PdfReportDocument(matchesForTeamSummary, _allPlayerStats, summedStats);

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

        // Przycisk "WYŚLIJ EMAIL"
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


        // --- 6. FUNKCJE POMOCNICZE (tylko UI) ---

        private void DisplayStatsInGrids(List<PlayerStats> stats, string teamAName = null, string teamBName = null, bool excludeBases = false)
        {
            List<PlayerStats> filteredStats = excludeBases
                ? stats.Where(s => !_statsService.IsBase(s.Gracz)).ToList()
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
    }
}