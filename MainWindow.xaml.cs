using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace TrainingTracker.DatabaseApp;

public partial class MainWindow : Window
{
    private readonly TrainingDatabase _database;

    public ObservableCollection<TrainingSession> Sessions { get; } = new();
    public List<string> TrainingTypes { get; } =
    [
        "Бег",
        "Силовая",
        "Плавание",
        "Йога/растяжка",
        "Велотренажер",
        "HIIT",
        "Командные виды",
        "Другое"
    ];

    public MainWindow()
    {
        InitializeComponent();
        _database = new TrainingDatabase("training-sessions.db");
        DataContext = this;
        SessionDatePicker.SelectedDate = DateTime.Today;
        LoadSessions();
        UpdateStats();
    }

    private void LoadSessions()
    {
        Sessions.Clear();
        foreach (var session in _database.GetSessions().OrderByDescending(s => s.Date))
        {
            Sessions.Add(session);
        }
    }

    private void OnAddSessionClick(object sender, RoutedEventArgs e)
    {
        if (!SessionDatePicker.SelectedDate.HasValue)
        {
            MessageBox.Show("Укажите дату.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DurationTextBox.Text, out var duration) || duration <= 0)
        {
            MessageBox.Show("Введите длительность в минутах.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = string.IsNullOrWhiteSpace(TypeComboBox.Text) ? "Не указано" : TypeComboBox.Text.Trim();
        var intensity = (int)Math.Round(IntensitySlider.Value);
        var notes = NotesTextBox.Text?.Trim() ?? string.Empty;

        var session = _database.AddSession(
            DateOnly.FromDateTime(SessionDatePicker.SelectedDate.Value),
            type,
            duration,
            intensity,
            notes);

        Sessions.Insert(0, session);
        UpdateStats();
        ClearForm();
    }

    private void OnDeleteSessionClick(object sender, RoutedEventArgs e)
    {
        if (SessionsGrid.SelectedItem is not TrainingSession session)
        {
            MessageBox.Show("Выберите запись.", "Удаление", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show("Удалить выбранную тренировку?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (_database.RemoveSession(session.Id))
        {
            Sessions.Remove(session);
            UpdateStats();
        }
    }

    private void OnRefreshStatsClick(object sender, RoutedEventArgs e) => UpdateStats();

    private void OnClearFormClick(object sender, RoutedEventArgs e) => ClearForm();

    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (Sessions.Count == 0)
        {
            MessageBox.Show("Нет данных для экспорта.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV файлы (*.csv)|*.csv",
            FileName = $"training-report-{DateTime.Today:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var stats = _database.BuildStatistics();
            var builder = new StringBuilder();
            builder.AppendLine("Дата;Тип;Длительность, мин;Интенсивность;Комментарий;ID");

            foreach (var session in _database.GetSessions().OrderBy(s => s.Date))
            {
                builder.AppendLine(string.Join(';', new[]
                {
                    session.DateFormatted,
                    Escape(session.Type),
                    session.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                    session.Intensity.ToString(CultureInfo.InvariantCulture),
                    Escape(session.Notes),
                    session.Id.ToString()
                }));
            }

            builder.AppendLine();
            builder.AppendLine("Метрика;Значение");
            builder.AppendLine($"Всего тренировок;{stats.TotalSessions}");
            builder.AppendLine($"Всего минут;{stats.TotalMinutes}");
            builder.AppendLine($"Средняя длительность;{stats.AverageDurationMinutes:F1}");
            builder.AppendLine($"Средняя интенсивность;{stats.AverageIntensity:F1}");
            builder.AppendLine($"Популярный тип;{stats.MostPopularWorkout ?? "—"}");
            builder.AppendLine($"Тренировок за 7 дней;{stats.LastWeekSessions}");

            File.WriteAllText(dialog.FileName, builder.ToString(), Encoding.UTF8);
            MessageBox.Show("Экспорт завершён.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось сохранить файл: {ex.Message}", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearForm()
    {
        SessionDatePicker.SelectedDate = DateTime.Today;
        TypeComboBox.Text = string.Empty;
        DurationTextBox.Clear();
        IntensitySlider.Value = 5;
        NotesTextBox.Clear();
    }

    private void UpdateStats()
    {
        var stats = _database.BuildStatistics();
        TotalSessionsText.Text = stats.TotalSessions.ToString(CultureInfo.InvariantCulture);
        TotalMinutesText.Text = stats.TotalMinutes.ToString(CultureInfo.InvariantCulture);
        AverageDurationText.Text = $"{stats.AverageDurationMinutes:F1} мин";
        AverageIntensityText.Text = $"{stats.AverageIntensity:F1}/10";
        PopularTypeText.Text = string.IsNullOrWhiteSpace(stats.MostPopularWorkout) ? "—" : stats.MostPopularWorkout;
        WeeklySessionsText.Text = stats.LastWeekSessions.ToString(CultureInfo.InvariantCulture);
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(';') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}

