namespace Dotsesses.Tests.ViewModels;

using Dotsesses.Models;
using Dotsesses.UI;

/// <summary>
/// Covers <see cref="MainWindowViewModel.BuildSeriesData"/> — a static
/// helper that filters a ClassAssessment's scores by ScoreSelection
/// flag (Display, Correlation). Lives outside MainWindowViewModelTests
/// because the helper is selector-level and has no coupling to MWVM
/// orchestration.
/// </summary>
public class MainWindowViewModelBuildSeriesDataTests
{
    private static MainWindowViewModel CreateViewModel()
    {
        return MainWindowViewModel.CreateForTesting(
            ResolveRepoFile(Path.Combine("Dotsesses", "example", "IP exam scores 2025.xlsx")));
    }

    private static string ResolveRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dotsesses.sln")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new InvalidOperationException("Could not locate Dotsesses.sln walking up from " + AppContext.BaseDirectory);
        }
        return Path.Combine(dir.FullName, relativePath);
    }

    private static string DisplayName(Score score) =>
        score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;

    [Fact]
    public void FiltersByDisplaySelector()
    {
        var vm = CreateViewModel();
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores.ToList();
        var excluded = firstStudentScores.First();
        var excludedDisplayName = DisplayName(excluded);

        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => (s.Name == excluded.Name && s.Index == excluded.Index)
                ? s with { Display = false }
                : s)
            .ToList();
        vm.ClassAssessment.ScoreSelections = newSelections;

        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Display);

        Assert.DoesNotContain(result, t => t.SeriesName == excludedDisplayName);
        foreach (var score in firstStudentScores.Where(s => !(s.Name == excluded.Name && s.Index == excluded.Index)))
        {
            Assert.Contains(result, t => t.SeriesName == DisplayName(score));
        }
    }

    [Fact]
    public void FiltersByCorrelationSelector()
    {
        var vm = CreateViewModel();
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores.ToList();
        var excluded = firstStudentScores.Last();
        var excludedDisplayName = DisplayName(excluded);

        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => (s.Name == excluded.Name && s.Index == excluded.Index)
                ? s with { Correlation = false }
                : s)
            .ToList();
        vm.ClassAssessment.ScoreSelections = newSelections;

        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Correlation);

        Assert.DoesNotContain(result, t => t.SeriesName == excludedDisplayName);
        foreach (var score in firstStudentScores.Where(s => !(s.Name == excluded.Name && s.Index == excluded.Index)))
        {
            Assert.Contains(result, t => t.SeriesName == DisplayName(score));
        }
    }

    [Fact]
    public void EmptySelectionsList_ReturnsAllScores()
    {
        // Defensive fallback: when no selections exist, every score on the
        // first student is included — preserves pre-S04 behavior.
        var vm = CreateViewModel();
        vm.ClassAssessment.ScoreSelections = Array.Empty<ScoreSelection>();
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores.ToList();

        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Display);

        Assert.Equal(firstStudentScores.Count, result.Count);
        foreach (var score in firstStudentScores)
        {
            Assert.Contains(result, t => t.SeriesName == DisplayName(score));
        }
    }

    [Fact]
    public void PreservesScoreValuesForIncludedScores()
    {
        var vm = CreateViewModel();
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores.ToList();
        Assert.True(firstStudentScores.Count >= 2, "Fixture must have at least 2 scores for this test.");

        var excluded = firstStudentScores[0];
        var sampled = firstStudentScores[1];

        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => (s.Name == excluded.Name && s.Index == excluded.Index)
                ? s with { Display = false }
                : s)
            .ToList();
        vm.ClassAssessment.ScoreSelections = newSelections;

        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Display);

        var sampledTuple = result.Single(t => t.SeriesName == DisplayName(sampled));
        foreach (var assessment in vm.ClassAssessment.Assessments)
        {
            var expected = assessment.Scores.FirstOrDefault(s => s.Name == sampled.Name && s.Index == sampled.Index);
            if (expected != null)
            {
                var key = $"S{assessment.Id:D3}";
                Assert.True(sampledTuple.Scores.ContainsKey(key), $"Missing student key {key} for {sampled.Name}");
                Assert.Equal(expected.Value, sampledTuple.Scores[key]);
            }
        }
    }

    [Fact]
    public void AllScoresExcluded_ReturnsEmptyList()
    {
        var vm = CreateViewModel();
        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => s with { Display = false })
            .ToList();
        vm.ClassAssessment.ScoreSelections = newSelections;

        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Display);

        Assert.Empty(result);
    }
}
