namespace Dotsesses.Tests.ViewModels;

using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Services;
using Dotsesses.UI;
using Dotsesses.Models;
using OxyPlot;

public class MainWindowViewModelTests
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

    [Fact]
    public void Constructor_InitializesPlotModel()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.NotNull(viewModel.DotplotModel);
        Assert.Equal(OxyColors.Transparent, viewModel.DotplotModel.Background);
    }

    [Fact]
    public void Constructor_LoadsSyntheticData()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.NotNull(viewModel.ClassAssessment);
        Assert.True(viewModel.ClassAssessment.Assessments.Count > 0, "Should have at least some students");
    }

    [Fact]
    public void PlotModel_HasAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert - Now has 4 axes: SharedX, StatsY, DotY, CursorY
        Assert.Equal(4, viewModel.DotplotModel.Axes.Count);
        Assert.Contains(viewModel.DotplotModel.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Bottom);
        Assert.Contains(viewModel.DotplotModel.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Left);
    }

    [Fact]
    public void PlotModel_HasScatterSeries()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert - now has 2 series (unselected and selected)
        Assert.Equal(2, viewModel.DotplotModel.Series.Count);
        Assert.IsType<OxyPlot.Series.ScatterSeries>(viewModel.DotplotModel.Series[0]);
        Assert.IsType<OxyPlot.Series.ScatterSeries>(viewModel.DotplotModel.Series[1]);
    }

    [Fact]
    public void ScatterSeries_HasStudents()
    {
        // Act
        var viewModel = CreateViewModel();
        var circleSeries = viewModel.DotplotModel.Series[0] as OxyPlot.Series.ScatterSeries;
        var squareSeries = viewModel.DotplotModel.Series[1] as OxyPlot.Series.ScatterSeries;

        Assert.NotNull(circleSeries);
        Assert.NotNull(squareSeries);
        // Total points across both series should match student count
        var totalPoints = circleSeries.Points.Count + squareSeries.Points.Count;
        Assert.Equal(viewModel.ClassAssessment.Assessments.Count, totalPoints);
    }

    [Fact]
    public void PlotModel_UsesDarkTheme()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert - Uses transparent background now for theme integration
        Assert.Equal(OxyColors.Transparent, viewModel.DotplotModel.Background);
        Assert.Equal(OxyColor.FromRgb(60, 60, 60), viewModel.DotplotModel.PlotAreaBorderColor);
    }

    [Fact]
    public void Constructor_InitializesCursors()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.NotNull(viewModel.Cursors);
        Assert.NotEmpty(viewModel.Cursors);
    }

    [Fact]
    public void Constructor_InitializesComplianceGrid()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.NotNull(viewModel.ComplianceRows);
        Assert.Equal(11, viewModel.ComplianceRows.Count); // All grades A through F (including C-, D+)
    }

    [Fact]
    public void AllGrades_AreEnabledByDefault()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert - All grades (A through F) should be enabled by default
        var fGrade = viewModel.ComplianceRows.FirstOrDefault(r => r.Grade.LetterGrade == LetterGrade.F);
        Assert.NotNull(fGrade);
        Assert.True(fGrade.IsEnabled, "F grade should be enabled by default");

        var fCursor = viewModel.Cursors.FirstOrDefault(c => c.Grade.LetterGrade == LetterGrade.F);
        Assert.NotNull(fCursor);
        Assert.True(fCursor.IsEnabled, "F cursor should be enabled by default");
    }

    // -----------------------------------------------------------------------
    // S04/T02: Default-seeding on load + ApplyScoreSelections recompute orchestrator
    // -----------------------------------------------------------------------

    private static IReadOnlyList<ScoreSelection> BuildSelections(
        IReadOnlyList<Score> scores,
        Func<Score, ScoreSelection>? customize = null)
    {
        return scores
            .Select(s => customize?.Invoke(s) ??
                new ScoreSelection(s.Name, s.Index, Display: true, Aggregate: true, Correlation: true))
            .ToList();
    }

    [Fact]
    public void ApplyScoreSelections_MutatesClassAssessmentScoreSelections()
    {
        // Arrange
        var vm = CreateViewModel();
        var scores = vm.ClassAssessment.Assessments.First().Scores;
        // Build selections with one score deliberately excluded from Aggregate.
        var newSelections = BuildSelections(scores, s =>
            new ScoreSelection(s.Name, s.Index,
                Display: true,
                Aggregate: !string.Equals(s.Name, "MC", StringComparison.OrdinalIgnoreCase),
                Correlation: true));

        // Act
        vm.ApplyScoreSelections(newSelections);

        // Assert — same reference is stored on ClassAssessment
        Assert.Same(newSelections, vm.ClassAssessment.ScoreSelections);
    }

    [Fact]
    public void ApplyScoreSelections_RecalculatesAggregateOnEveryStudent()
    {
        // Arrange
        var vm = CreateViewModel();
        var students = vm.ClassAssessment.Assessments.ToList();
        var scoresOfFirst = students.First().Scores;

        // Pick a score to exclude from Aggregate that is non-zero on at least one student.
        // 'MC' is the first column in the IP exam fixture and is non-zero for typical students.
        const string excludedName = "MC";
        var newSelections = BuildSelections(scoresOfFirst, s =>
            new ScoreSelection(s.Name, s.Index,
                Display: true,
                Aggregate: !string.Equals(s.Name, excludedName, StringComparison.Ordinal),
                Correlation: true));

        var beforeAggregates = students.Select(st => st.AggregateGrade).ToList();

        // Compute expected new aggregate for the first student manually:
        // sum of all scores whose Name != excludedName, then truncate.
        var firstStudent = students.First();
        var expectedFirstAggregate = (int)firstStudent.Scores
            .Where(s => !string.Equals(s.Name, excludedName, StringComparison.Ordinal))
            .Sum(s => s.Value);

        // Act
        vm.ApplyScoreSelections(newSelections);

        // Assert
        var afterAggregates = students.Select(st => st.AggregateGrade).ToList();
        Assert.NotEqual(beforeAggregates, afterAggregates);                  // at least one differs
        Assert.Equal(expectedFirstAggregate, firstStudent.AggregateGrade);   // exact value for one student
    }

    [Fact]
    public void ApplyScoreSelections_SetsHasUnsavedChangesTrue()
    {
        // Arrange
        var vm = CreateViewModel();
        Assert.False(vm.HasUnsavedChanges); // Sanity: a fresh load resets the flag.
        var scores = vm.ClassAssessment.Assessments.First().Scores;
        var newSelections = BuildSelections(scores);

        // Act
        vm.ApplyScoreSelections(newSelections);

        // Assert
        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public void ApplyScoreSelections_TriggersGradeCountRefresh()
    {
        // Arrange
        var vm = CreateViewModel();
        var studentCount = vm.ClassAssessment.Assessments.Count;
        var beforeSum = vm.ComplianceRows.Sum(r => r.CurrentCount);
        var scores = vm.ClassAssessment.Assessments.First().Scores;
        var newSelections = BuildSelections(scores);

        // Act
        vm.ApplyScoreSelections(newSelections);

        // Assert — total count across compliance rows is preserved (== student count),
        // proving grade counts were recomputed without losing any students.
        var afterSum = vm.ComplianceRows.Sum(r => r.CurrentCount);
        Assert.Equal(studentCount, beforeSum);
        Assert.Equal(studentCount, afterSum);
    }

    [Fact]
    public void LoadFromExcelFile_SeedsDefaultSelections()
    {
        // Arrange / Act — CreateViewModel calls LoadFromExcelFile via the T01 factory.
        var vm = CreateViewModel();

        // Assert
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores;
        var selections = vm.ClassAssessment.ScoreSelections;

        Assert.Equal(firstStudentScores.Count, selections.Count);

        foreach (var sel in selections)
        {
            Assert.True(sel.Display, $"Display should default true for {sel.Name}");
            Assert.True(sel.Correlation, $"Correlation should default true for {sel.Name}");

            var isTotal = string.Equals(sel.Name, "Total", StringComparison.OrdinalIgnoreCase);
            if (isTotal)
            {
                Assert.False(sel.Aggregate, "The 'Total' column must default to Aggregate=false.");
            }
            else
            {
                Assert.True(sel.Aggregate, $"Non-Total score {sel.Name} should default to Aggregate=true.");
            }
        }
    }

    [Fact]
    public async Task LoadStateAsync_V1File_SeedsDefaults()
    {
        // Arrange — use the parameterless CreateForTesting overload so the .dots load is the
        // FIRST load on this VM (loading .xlsx then .dots would double-add cursors + compliance
        // rows, which is a separate pre-existing reload bug unrelated to this slice).
        var vm = MainWindowViewModel.CreateForTesting();
        var v1Path = ResolveRepoFile(Path.Combine("Dotsesses", "example", "IP exam scores 2025.dots"));

        // Act — invoke the source-generated AsyncRelayCommand that wraps LoadStateAsync.
        await vm.LoadStateCommand.ExecuteAsync(v1Path);

        // Assert — same shape as the fresh-xlsx case (closes R012).
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores;
        var selections = vm.ClassAssessment.ScoreSelections;

        Assert.Equal(firstStudentScores.Count, selections.Count);

        foreach (var sel in selections)
        {
            Assert.True(sel.Display);
            Assert.True(sel.Correlation);
            var isTotal = string.Equals(sel.Name, "Total", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(!isTotal, sel.Aggregate);
        }
    }

    [Fact]
    public void ApplyScoreSelections_WithEmptySelections_DoesNotCrash()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        var ex = Record.Exception(() => vm.ApplyScoreSelections(Array.Empty<ScoreSelection>()));

        // Assert
        Assert.Null(ex);
        Assert.All(vm.ClassAssessment.Assessments, st => Assert.Equal(0, st.AggregateGrade));
    }

    // -------------------------------------------------------------------
    // T03 — BuildSeriesData helper (filter violin/correlation seriesData by selection)
    // -------------------------------------------------------------------

    private static string DisplayName(Score score) =>
        score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;

    [Fact]
    public void BuildSeriesData_FiltersByDisplaySelector()
    {
        // Arrange — fixture-loaded VM has ScoreSelections seeded by T02 with all Display=true.
        var vm = CreateViewModel();
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores.ToList();
        var excluded = firstStudentScores.First();
        var excludedDisplayName = DisplayName(excluded);

        // Flip Display=false for exactly one score; leave the rest at Display=true.
        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => (s.Name == excluded.Name && s.Index == excluded.Index)
                ? s with { Display = false }
                : s)
            .ToList();
        vm.ClassAssessment.ScoreSelections = newSelections;

        // Act
        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Display);

        // Assert — the excluded series is gone; every other score is still present.
        Assert.DoesNotContain(result, t => t.SeriesName == excludedDisplayName);
        foreach (var score in firstStudentScores.Where(s => !(s.Name == excluded.Name && s.Index == excluded.Index)))
        {
            Assert.Contains(result, t => t.SeriesName == DisplayName(score));
        }
    }

    [Fact]
    public void BuildSeriesData_FiltersByCorrelationSelector()
    {
        // Arrange — exclude a different score via Correlation=false to confirm the helper is selector-agnostic.
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

        // Act
        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Correlation);

        // Assert
        Assert.DoesNotContain(result, t => t.SeriesName == excludedDisplayName);
        foreach (var score in firstStudentScores.Where(s => !(s.Name == excluded.Name && s.Index == excluded.Index)))
        {
            Assert.Contains(result, t => t.SeriesName == DisplayName(score));
        }
    }

    [Fact]
    public void BuildSeriesData_EmptySelectionsList_ReturnsAllScores()
    {
        // Arrange — defensive fallback: when no selections exist, every score on the first student is included.
        var vm = CreateViewModel();
        vm.ClassAssessment.ScoreSelections = Array.Empty<ScoreSelection>();
        var firstStudentScores = vm.ClassAssessment.Assessments.First().Scores.ToList();

        // Act
        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Display);

        // Assert — every score appears exactly once.
        Assert.Equal(firstStudentScores.Count, result.Count);
        foreach (var score in firstStudentScores)
        {
            Assert.Contains(result, t => t.SeriesName == DisplayName(score));
        }
    }

    [Fact]
    public void BuildSeriesData_PreservesScoreValuesForIncludedScores()
    {
        // Arrange — exclude one score and pick a different one to spot-check value preservation.
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

        // Act
        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Display);

        // Assert — every (assessment, sampled-score) pair is present and the value is verbatim.
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
    public void BuildSeriesData_AllScoresExcluded_ReturnsEmptyList()
    {
        // Arrange — every selection has Display=false, so the predicate matches nothing.
        var vm = CreateViewModel();
        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => s with { Display = false })
            .ToList();
        vm.ClassAssessment.ScoreSelections = newSelections;

        // Act
        var result = MainWindowViewModel.BuildSeriesData(vm.ClassAssessment, s => s.Display);

        // Assert
        Assert.Empty(result);
    }
}
