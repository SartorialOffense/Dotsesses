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

    // ===== Ordinal columns in violin / correlation (ADR-0017, slice 3) =====

    private static ClassAssessment OrdinalFixture()
    {
        // Numeric Q + an Ordinal Mid-Term (✔=1 / ✔✔+=3).
        var students = new List<StudentAssessment>
        {
            new(1,
                new List<Score> { new("Q", null, 80) },
                new List<StudentAttribute> { new("Mid-Term", null, "✔", SortOrder: 1) },
                "muppet1"),
            new(2,
                new List<Score> { new("Q", null, 70) },
                new List<StudentAttribute> { new("Mid-Term", null, "✔✔+", SortOrder: 3) },
                "muppet2"),
        };
        var ca = new ClassAssessment(
            students,
            new List<CutoffCountRange>(),
            new Dictionary<int, MuppetNameInfo>(),
            new Dictionary<string, string>());
        ca.ScoreSelections = new List<ScoreSelection>
        {
            new("Q", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true, Significance: true),
            new("Mid-Term", null, ScoreColumnType.Ordinal, Display: true, Aggregate: false, Correlation: true, Significance: true),
        };
        return ca;
    }

    [Fact]
    public void BuildSeriesData_OrdinalColumn_EmittedWithSortOrderAsValue()
    {
        var ca = OrdinalFixture();

        var result = MainWindowViewModel.BuildSeriesData(ca, s => s.Display);

        Assert.Contains(result, t => t.SeriesName == "Q");
        var ordinal = Assert.Single(result, t => t.SeriesName == "Mid-Term");
        Assert.Equal(1.0, ordinal.Scores["S001"]); // ✔  → SortOrder 1
        Assert.Equal(3.0, ordinal.Scores["S002"]); // ✔✔+ → SortOrder 3
    }

    [Fact]
    public void BuildSeriesData_OrdinalColumn_ExcludedWhenSelectorExcludesIt()
    {
        var ca = OrdinalFixture();
        ca.ScoreSelections = ca.ScoreSelections
            .Select(s => s.Type == ScoreColumnType.Ordinal ? s with { Display = false } : s)
            .ToList();

        var result = MainWindowViewModel.BuildSeriesData(ca, s => s.Display);

        Assert.DoesNotContain(result, t => t.SeriesName == "Mid-Term");
        Assert.Contains(result, t => t.SeriesName == "Q");
    }

    [Fact]
    public void BuildSeriesData_PlainCategorical_NeverEmitted()
    {
        var ca = OrdinalFixture();
        // Demote to plain Categorical (no numeric value) but leave Display on.
        ca.ScoreSelections = ca.ScoreSelections
            .Select(s => s.Type == ScoreColumnType.Ordinal ? s with { Type = ScoreColumnType.Categorical } : s)
            .ToList();

        var result = MainWindowViewModel.BuildSeriesData(ca, s => s.Display);

        Assert.DoesNotContain(result, t => t.SeriesName == "Mid-Term");
    }

    [Fact]
    public void BuildOrdinalLabelMap_MapsStudentSeriesToStrippedLabel()
    {
        var ca = OrdinalFixture();

        var map = MainWindowViewModel.BuildOrdinalLabelMap(ca, s => s.Display);

        Assert.Equal("✔", map[(1, "Mid-Term")]);
        Assert.Equal("✔✔+", map[(2, "Mid-Term")]);
        Assert.DoesNotContain(map.Keys, k => k.SeriesName == "Q");
    }

    [Fact]
    public void BuildOrdinalLabelMap_EmptyWhenNoOrdinalSelected()
    {
        var ca = OrdinalFixture();
        ca.ScoreSelections = ca.ScoreSelections
            .Select(s => s.Type == ScoreColumnType.Ordinal ? s with { Display = false } : s)
            .ToList();

        var map = MainWindowViewModel.BuildOrdinalLabelMap(ca, s => s.Display);

        Assert.Empty(map);
    }
}
