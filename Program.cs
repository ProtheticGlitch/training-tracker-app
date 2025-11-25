using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

namespace TrainingTracker.ExperimentApp;

public sealed record TrainingPlan(
    string Name,
    double BaseStimulus,
    double AdaptationRate,
    double FatigueRate,
    double RecoverySpeed,
    double Variation)
{
    public static IReadOnlyList<TrainingPlan> Predefined() =>
        new List<TrainingPlan>
        {
            new("Силовая база", 7.5, 0.9, 1.1, 1.4, 0.12),
            new("Общая выносливость", 6.0, 0.85, 0.9, 1.2, 0.08),
            new("HIIT/кроссфит", 9.0, 1.05, 1.4, 1.0, 0.2),
            new("Техника + кардио", 5.5, 0.7, 0.6, 1.6, 0.1)
        };
}

public sealed class TrainingPlanOption : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public TrainingPlanOption(TrainingPlan plan) => Plan = plan;

    public TrainingPlan Plan { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class TrainingExperiment
{
    private readonly int _weeks;
    private readonly int _sessionsPerWeek;
    private readonly int _runs;

    public TrainingExperiment(int weeks, int sessionsPerWeek, int runs)
    {
        _weeks = weeks;
        _sessionsPerWeek = sessionsPerWeek;
        _runs = runs;
    }

    public ExperimentResult Run(TrainingPlan plan)
    {
        var random = new Random(HashCode.Combine(plan.Name, _weeks, _sessionsPerWeek, _runs));
        var totalDays = _weeks * 7;
        var workoutProbability = _sessionsPerWeek / 7.0;

        var fitnessHistory = new List<double>(_runs * totalDays);
        var burnoutCount = 0;
        var stabilityAccumulator = 0.0;

        for (var run = 0; run < _runs; run++)
        {
            var fitness = 0.0;
            var fatigue = 0.0;
            var burnout = false;

            for (var day = 0; day < totalDays; day++)
            {
                var hasWorkout = random.NextDouble() < workoutProbability;
                if (hasWorkout)
                {
                    var stimulus = plan.BaseStimulus * (1 + random.NextDouble() * plan.Variation - plan.Variation / 2);
                    var adaptationGain = stimulus * plan.AdaptationRate;
                    var fatigueGain = stimulus * plan.FatigueRate;
                    fitness += adaptationGain;
                    fatigue += fatigueGain;
                }

                stabilityAccumulator += fitness > fatigue ? 1 : 0;

                fatigue = Math.Max(0, fatigue - plan.RecoverySpeed);
                fitness = Math.Max(0, fitness * 0.995);

                if (fatigue > fitness * 2.2)
                {
                    burnout = true;
                }

                fitnessHistory.Add(fitness - fatigue * 0.5);
            }

            if (burnout)
            {
                burnoutCount++;
            }
        }

        fitnessHistory.Sort();
        var averageFitness = fitnessHistory.Average();
        var percentile95 = fitnessHistory[(int)(fitnessHistory.Count * 0.95)];
        var stability = stabilityAccumulator / (_runs * totalDays);

        return new ExperimentResult(
            plan.Name,
            averageFitness,
            percentile95,
            burnoutCount / (double)_runs,
            stability);
    }
}

public sealed record ExperimentResult(
    string PlanName,
    double AverageFitness,
    double FitnessPercentile95,
    double BurnoutProbability,
    double Stability);
