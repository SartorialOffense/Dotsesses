namespace Dotsesses.Tests.ViewModels;

using Dotsesses.Models;
using Dotsesses.Tests.Fixtures;
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
        => MainWindowViewModel.CreateForTesting(TestFixtures.IpExamScoresXlsx());

    private static string DisplayName(Score score) =>
        score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;

    [Fact]
    public void FiltersByDisplaySelector()
    {
        var vm = CreateViewModel();
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores.ToList();
        var excluded = firstStudentScores.First();
        var excludedDisplayName = DisplayName(excluded);

        // Normalize to all-Display-on, then flip just the excluded column off,
        // so this exercises the filter independent of the data-driven defaults.
        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => s with { Display = !(s.Name == excluded.Name && s.Index == excluded.Index) })
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

        // Normalize to all-Correlation-on, then flip just the excluded column
        // off, so this exercises the filter independent of the data-driven defaults.
        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => s with { Correlation = !(s.Name == excluded.Name && s.Index == excluded.Index) })
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

        // Normalize to all-Display-on, then flip just the excluded column off,
        // so this exercises the filter independent of the data-driven defaults.
        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => s with { Display = !(s.Name == excluded.Name && s.Index == excluded.Index) })
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
