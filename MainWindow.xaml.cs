using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace TrainingTracker.ExperimentApp;

public partial class MainWindow : Window
{
    public ObservableCollection<TrainingPlanOption> PlanOptions { get; } = new();
    public ObservableCollection<ExperimentResult> Results { get; } = new();
    private SummarySnapshot? _summary;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ExportButton.IsEnabled = false;
        SummaryTextBlock.Text = "Пока нет данных. Запустите эксперимент.";

        foreach (var plan in TrainingPlan.Predefined())
        {
            PlanOptions.Add(new TrainingPlanOption(plan));
        }
    }

    private async void OnRunExperimentClick(object sender, RoutedEventArgs e)
    {
        if (!TryParseInputs(out var weeks, out var sessions, out var runs))
        {
            return;
        }

        var activePlans = PlanOptions.Where(p => p.IsSelected).Select(p => p.Plan).ToList();
        if (activePlans.Count == 0)
        {
            MessageBox.Show("Оставьте хотя бы один план.", "Эксперимент", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ToggleControls(false);
        StatusTextBlock.Text = "Выполняем вычисления...";

        try
        {
            var results = await Task.Run(() =>
            {
                var experiment = new TrainingExperiment(weeks, sessions, runs);
                return activePlans.Select(experiment.Run)
                    .OrderByDescending(r => r.AverageFitness)
                    .ToList();
            });

            Results.Clear();
            foreach (var result in results)
            {
                Results.Add(result);
            }

            UpdateSummary(results);
            StatusTextBlock.Text = $"Готово. Сценариев: {results.Count}.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "Ошибка.";
            MessageBox.Show($"Не удалось выполнить эксперимент: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ToggleControls(true);
        }
    }

    private void OnExportResultsClick(object sender, RoutedEventArgs e)
    {
        if (Results.Count == 0 || _summary is null)
        {
            MessageBox.Show("Сначала выполните эксперимент.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV файлы (*.csv)|*.csv",
            FileName = $"experiment-report-{DateTime.Today:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("План;Средняя форма;95% форма;Вероятность выгорания (%);Стабильность");
            foreach (var result in Results)
            {
                builder.AppendLine(string.Join(';', new[]
                {
                    CsvEscape(result.PlanName),
                    result.AverageFitness.ToString("F1", CultureInfo.InvariantCulture),
                    result.FitnessPercentile95.ToString("F1", CultureInfo.InvariantCulture),
                    (result.BurnoutProbability * 100).ToString("F1", CultureInfo.InvariantCulture),
                    result.Stability.ToString("F2", CultureInfo.InvariantCulture)
                }));
            }

            builder.AppendLine();
            builder.AppendLine("Итоги;Значение");
            builder.AppendLine($"Лучшая средняя форма;{CsvEscape($"{_summary.BestOverall.PlanName} ({_summary.BestOverall.AverageFitness:F1})")}");
            builder.AppendLine($"Самый стабильный план;{CsvEscape($"{_summary.MostStable.PlanName} ({_summary.MostStable.Stability:F2})")}");
            builder.AppendLine($"Самый безопасный план;{CsvEscape($"{_summary.Safest.PlanName} ({_summary.Safest.BurnoutProbability * 100:F1}% выгорания)")}");

            File.WriteAllText(dialog.FileName, builder.ToString(), Encoding.UTF8);
            MessageBox.Show("Экспорт завершён.", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось сохранить файл: {ex.Message}", "Экспорт", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryParseInputs(out int weeks, out int sessions, out int runs)
    {
        weeks = sessions = runs = 0;
        if (!int.TryParse(WeeksTextBox.Text, out weeks) || weeks < 2 || weeks > 52)
        {
            MessageBox.Show("Количество недель: 2–52.", "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(SessionsTextBox.Text, out sessions) || sessions < 1 || sessions > 14)
        {
            MessageBox.Show("Тренировок в неделю: 1–14.", "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(RunsTextBox.Text, out runs) || runs < 50 || runs > 5000)
        {
            MessageBox.Show("Прогонов: 50–5000.", "Параметры", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void ToggleControls(bool isEnabled)
    {
        WeeksTextBox.IsEnabled = isEnabled;
        SessionsTextBox.IsEnabled = isEnabled;
        RunsTextBox.IsEnabled = isEnabled;
        PlanList.IsEnabled = isEnabled;
        RunButton.IsEnabled = isEnabled;
        ExportButton.IsEnabled = isEnabled && _summary is not null;
        ResultsGrid.IsEnabled = isEnabled;
    }

    private void UpdateSummary(IReadOnlyList<ExperimentResult> results)
    {
        if (results.Count == 0)
        {
            _summary = null;
            SummaryTextBlock.Text = "Нет данных для отображения.";
            ExportButton.IsEnabled = false;
            return;
        }

        var bestOverall = results.MaxBy(r => r.AverageFitness)!;
        var mostStable = results.MaxBy(r => r.Stability)!;
        var safest = results.MinBy(r => r.BurnoutProbability)!;
        _summary = new SummarySnapshot(bestOverall, mostStable, safest);

        SummaryTextBlock.Text =
            $"Лучшая средняя форма — {bestOverall.PlanName} ({bestOverall.AverageFitness:F1}).\n" +
            $"Самая высокая стабильность — {mostStable.PlanName} ({mostStable.Stability:F2}).\n" +
            $"Минимальный риск выгорания — {safest.PlanName} ({safest.BurnoutProbability * 100:F1}%).";
        ExportButton.IsEnabled = true;
    }

    private static string CsvEscape(string value) =>
        value.Contains(';') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private sealed record SummarySnapshot(
        ExperimentResult BestOverall,
        ExperimentResult MostStable,
        ExperimentResult Safest);
}

