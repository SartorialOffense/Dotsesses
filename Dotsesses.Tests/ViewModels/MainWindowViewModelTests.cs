namespace Dotsesses.Tests.ViewModels;

using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Calculators;
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
    public async Task LoadStateAsync_V1File_ThrowsForUnsupportedVersion()
    {
        // V1 .dots files are no longer supported (ADR-0009). The rejection
        // happens in StateService.LoadAsync and bubbles up to the caller.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MainWindowVMTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var v1Path = Path.Combine(tempDir, "v1.dots");
            await File.WriteAllTextAsync(
                v1Path,
                """{"version": 1, "savedAt": "2024-01-01T00:00:00Z", "students": [], "cursors": []}""");

            var vm = MainWindowViewModel.CreateForTesting();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => vm.LoadStateCommand.ExecuteAsync(v1Path));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadFromExcelFile_ConstructsGradingSession()
    {
        // Slice 2: each Excel load constructs a fresh GradingSession alongside
        // the ClassAssessment. The session is the canonical owner of the live
        // grading state going forward.
        var vm = CreateViewModel();

        Assert.NotNull(vm.GradingSession);
        Assert.NotEmpty(vm.GradingSession.Slots);
        Assert.NotEmpty(vm.GradingSession.CurrentState.Cutoffs);
    }

    [Fact]
    public void LoadFromExcelFile_TwoLoads_ProduceFreshSession()
    {
        // Each load creates a new session — no state from the prior file leaks.
        var vm = CreateViewModel();
        var firstSession = vm.GradingSession;

        // Re-load the same Excel; the session should be a new instance.
        vm.LoadFromExcelFile(
            ResolveRepoFile(Path.Combine("Dotsesses", "example", "IP exam scores 2025.xlsx")));

        Assert.NotSame(firstSession, vm.GradingSession);
    }

    [Fact]
    public void GradingSession_MoveCutoff_MirrorsIntoLegacyCursorsCollection()
    {
        // Slice 3: drag goes through GradingSession.MoveCutoff. The legacy
        // Cursors collection mirror-syncs from session.LastChange so existing
        // OxyPlot rendering and Compliance recalc paths keep working until
        // the cleanup slice (issue #14) deletes _cursors entirely.
        var vm = CreateViewModel();
        var slotA = vm.GradingSession.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var initial = slotA.Score;
        var newScore = initial - 5;

        vm.GradingSession.MoveCutoff(slotA.Grade, newScore, originator: this);

        var legacyA = vm.Cursors.First(c => c.Grade.LetterGrade == LetterGrade.A);
        Assert.Equal(newScore, slotA.Score);
        Assert.Equal(newScore, legacyA.Score);
    }

    [Fact]
    public async Task LoadStateAsync_V2File_HydratesGradingSession()
    {
        // VM-driven round-trip: save a session with a non-default cutoff,
        // load on a fresh VM, verify the session reflects the saved state.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MainWindowVMTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var saveVm = CreateViewModel();
            var slotA = saveVm.GradingSession.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
            var newAScore = slotA.Score - 5;
            saveVm.GradingSession.MoveCutoff(slotA.Grade, newAScore, originator: this);

            var savedPath = Path.Combine(tempDir, "saved.dots");
            await saveVm.SaveStateCommand.ExecuteAsync(savedPath);

            var loadVm = MainWindowViewModel.CreateForTesting();
            await loadVm.LoadStateCommand.ExecuteAsync(savedPath);

            Assert.Equal(
                newAScore,
                loadVm.GradingSession.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A).Score);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ApplyScoreSelections_RebuildsDotplotPointsForNewAggregates()
    {
        // Regression test for the M001 S04 UAT Step 2 failure: toggling Aggregate off for
        // a non-Total score recomputed AggregateGrade (visible in the details panel) but the
        // dotplot didn't visually update. Verify that DotplotModel.Series points reflect the
        // new AggregateGrade values after ApplyScoreSelections.
        var vm = CreateViewModel();

        // Snapshot dot x-coordinates before the change.
        var seriesBefore = vm.DotplotModel.Series.OfType<OxyPlot.Series.ScatterSeries>().ToList();
        var pointsBefore = seriesBefore.SelectMany(s => s.Points).Select(p => p.X).OrderBy(x => x).ToList();

        // Build a selection set that excludes one non-Total score from Aggregate.
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var nonTotalScore = firstStudent.Scores.First(s => !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Value > 0);
        var modified = vm.ClassAssessment.ScoreSelections
            .Select(s =>
                s.Name == nonTotalScore.Name && s.Index == nonTotalScore.Index
                    ? s with { Aggregate = false }
                    : s)
            .ToList();

        // Act
        vm.ApplyScoreSelections(modified);

        // Assert — the per-student aggregate values reflected in the dotplot have changed.
        var seriesAfter = vm.DotplotModel.Series.OfType<OxyPlot.Series.ScatterSeries>().ToList();
        var pointsAfter = seriesAfter.SelectMany(s => s.Points).Select(p => p.X).OrderBy(x => x).ToList();

        Assert.NotEqual(pointsBefore, pointsAfter);

        // And the new x-positions match the actual AggregateGrade values of the students.
        var aggregateValues = vm.ClassAssessment.Assessments.Select(a => (double)a.AggregateGrade).OrderBy(x => x).ToList();
        var plottedX = pointsAfter.OrderBy(x => x).ToList();
        Assert.Equal(aggregateValues, plottedX);
    }

    [Fact]
    public async Task SaveStateAsync_LoadStateAsync_RestoresScoreSelections()
    {
        // Arrange — load fixture, mutate selections via ApplyScoreSelections, save, then load
        // into a fresh VM and assert the selections survived the round trip. Regression test
        // for the M001 S04 UAT Step 4 failure: LoadStateAsync was constructing a fresh
        // ClassAssessment with empty ScoreSelections and immediately seeding defaults, dropping
        // the persisted selections on the floor.
        var vm = CreateViewModel();
        var firstScore = vm.ClassAssessment.Assessments.First().Scores.First();
        var firstScoreName = firstScore.Name;
        var firstScoreIndex = firstScore.Index;

        var modified = vm.ClassAssessment.ScoreSelections
            .Select(s =>
                s.Name == firstScoreName && s.Index == firstScoreIndex
                    ? s with { Display = false, Correlation = false }
                    : s)
            .ToList();
        vm.ApplyScoreSelections(modified);

        var tempPath = Path.Combine(Path.GetTempPath(), $"dotsesses-roundtrip-{Guid.NewGuid():N}.dots");
        try
        {
            await vm.SaveStateCommand.ExecuteAsync(tempPath);

            var loaded = MainWindowViewModel.CreateForTesting();
            await loaded.LoadStateCommand.ExecuteAsync(tempPath);

            // Assert — the modified score's Display/Correlation are still false after reload.
            var restored = loaded.ClassAssessment.ScoreSelections
                .First(s => s.Name == firstScoreName && s.Index == firstScoreIndex);
            Assert.False(restored.Display);
            Assert.False(restored.Correlation);

            // Other rows still default-on (specifically, second score should still be Display=true).
            var secondScore = loaded.ClassAssessment.Assessments.First().Scores.Skip(1).First();
            var secondRestored = loaded.ClassAssessment.ScoreSelections
                .First(s => s.Name == secondScore.Name && s.Index == secondScore.Index);
            Assert.True(secondRestored.Display);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
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
    // M002/S05/T03 — narrow-aggregate-range defensive fallback
    //
    // SC1 Case 7 of the M002 milestone walkthrough surfaced a crash: when the user
    // reduces the Aggregate selection to a single non-Total component with a small
    // value range (e.g. 'Q2-TM', max 4.5), every student's AggregateGrade collapses
    // into a 0–~5 range. SeedCursorsFromDefaults then runs DefaultCurveGenerator +
    // InitialCutoffCalculator against that narrow range, the result interleaves
    // with the second-pass no-range catch-all positions, and the next
    // RecalculateGradeCounts feeds non-monotonic cutoffs into GradeAssigner..ctor
    // which throws "Cutoffs are out of order" and crashes the process.
    //
    // Same bug family as MEM028's empty-aggregate guard. The narrow-single-component
    // case is the uncovered edge. Fix landed in MainWindowViewModel.
    // SeedCursorsFromDefaults: detect non-monotonicity post-seed and fall back to
    // CursorPlacementCalculator.ResetToEvenSpacingMonotonic over [minScore,maxScore].
    // -------------------------------------------------------------------

    [Fact]
    public void ApplyScoreSelections_SingleNarrowAggregateComponent_DoesNotCrash()
    {
        // Arrange — load fixture; pin to "MC" specifically (matches the SC1 Case 7 user repro).
        // The IP exam fixture exposes (per fixture probe used during T03):
        //   MC=10, Q1ab=40, Q1c-e=6, Q2a-c=18, Q2-TM=4.5, Q2-box=10, Short=16,
        //   Class=110.5, Total=215. The user-reported crash was with 'MC' as the
        //   sole non-Total Aggregate (range collapsed to 0-10).
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var narrowComponent = firstStudent.Scores.First(s =>
            string.Equals(s.Name, "MC", StringComparison.Ordinal));
        Assert.True(narrowComponent.Value <= 10,
            $"Expected MC ≤10; fixture gave MC={narrowComponent.Value}");

        // Build selections where ONLY the narrow component is Aggregate=true.
        var narrowOnlySelections = firstStudent.Scores
            .Select(s => new ScoreSelection(
                s.Name,
                s.Index,
                Display: true,
                Aggregate: string.Equals(s.Name, narrowComponent.Name, StringComparison.Ordinal)
                    && !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase),
                Correlation: true))
            .ToList();

        // Act — Apply the narrow-aggregate-only selections. This is the SC1 Case 7 repro.
        // Pre-fix this throws InvalidOperationException("Cutoffs are out of order ...");
        // post-fix the defensive fallback kicks in and Apply succeeds.
        var ex = Record.Exception(() => vm.ApplyScoreSelections(narrowOnlySelections));

        // Assert — no crash. ClassAssessment.Current populated. Cursors monotonic by Grade.Order.
        Assert.Null(ex);
        Assert.NotNull(vm.ClassAssessment.Current);

        var cursorsByOrder = vm.Cursors.OrderBy(c => c.Grade.Order).ToList();
        for (int i = 0; i < cursorsByOrder.Count - 1; i++)
        {
            Assert.True(
                cursorsByOrder[i].Score >= cursorsByOrder[i + 1].Score,
                $"Cursor for {cursorsByOrder[i].Grade.DisplayName} (score {cursorsByOrder[i].Score}) " +
                $"must be ≥ cursor for {cursorsByOrder[i + 1].Grade.DisplayName} " +
                $"(score {cursorsByOrder[i + 1].Score}) after narrow-aggregate Apply.");
        }
    }

    // -------------------------------------------------------------------
    // M002/S05/T04 — aggregate-change Apply refreshes downstream drag bounds
    //
    // The user reported during SC1 Case 7 that "when the ranges are recalculated, the
    // cursors don't have their validation ranges updated so they can't be moved or
    // they do strange things". Investigation confirmed two cursor-drag paths:
    //
    //   - Dotplot drag (MainWindowViewModel.OnDotplotMouseMove) reads
    //     ClassAssessment.Assessments.Min/Max(a => a.AggregateGrade) directly at drag
    //     time → always fresh, no caching. Not affected by aggregate-change staleness.
    //   - Violin/cursor-column drag (ViolinPlotControl.axaml.cs) calls
    //     ViolinPlotViewModel.NormalizedToScore which reads cached MinScore/MaxScore
    //     from the VM. Those are written ONLY by WireCursorsToViolinPlot at the end
    //     of SeedCursorsFromDefaults — already called from the aggregate-changed
    //     branch of ApplyScoreSelections (line ~1300).
    //
    // Pre-T03 the GradeAssigner crash at RecalculateGradeCounts could fire AFTER the
    // wire-through (so MinScore/MaxScore *were* refreshed) but the process died before
    // the user could drag, so the user's "stale bounds" report was the symptom of the
    // crash flow rather than a separate cache-staleness bug. Now that T03 prevents
    // the crash, the existing wire-through path is provably exercised.
    //
    // These tests pin the contract at the readable seam: ClassAssessment.Assessments
    // must reflect the new aggregate range immediately after ApplyScoreSelections. Any
    // future change that breaks the freshness of WireCursorsToViolinPlot's input would
    // fail here. Direct ViolinPlotViewModel.MinScore/MaxScore assertions are not
    // possible at the unit level because CreateForTesting does not wire that VM (no
    // Avalonia harness in this project per MEM030); the contract is asserted via
    // the source values WireCursorsToViolinPlot reads.
    // -------------------------------------------------------------------

    [Fact]
    public void ApplyScoreSelections_AggregateChange_NewAggregateRangeIsObservable()
    {
        // Arrange — load fixture (full Aggregate selection) and capture original range.
        var vm = CreateViewModel();
        var originalMin = vm.ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var originalMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        Assert.True(originalMax > originalMin,
            "Sanity: fixture must have a non-degenerate aggregate range on initial load.");

        // Reduce Aggregate to MC only — narrow component so the new range is clearly different.
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var narrowOnlySelections = firstStudent.Scores
            .Select(s => new ScoreSelection(
                s.Name,
                s.Index,
                Display: true,
                Aggregate: string.Equals(s.Name, "MC", StringComparison.Ordinal)
                    && !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase),
                Correlation: true))
            .ToList();

        // Act
        vm.ApplyScoreSelections(narrowOnlySelections);

        // Assert — the source values WireCursorsToViolinPlot reads are now narrow-range.
        // After MC-only Aggregate, class min/max equals class min/max of the MC column
        // (per FixtureProbe used during T04: MC class min=4, max=16).
        var newMin = vm.ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var newMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        Assert.True(newMax < 50,
            $"After narrowing Aggregate to MC only, class max should be much less than the " +
            $"original full-aggregate max; got {newMax} vs original max={originalMax}.");
        Assert.True(newMin != originalMin || newMax != originalMax,
            $"Aggregate-change Apply must shift the observable range. " +
            $"original=({originalMin}, {originalMax}); new=({newMin}, {newMax}).");
    }

    [Fact]
    public void ApplyScoreSelections_DisplayOnlyChange_AggregateRangeUnchanged()
    {
        // Arrange — capture original aggregate range, then build a Display-only-changed selection.
        var vm = CreateViewModel();
        var originalMin = vm.ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var originalMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var displayOnlyChanged = vm.ClassAssessment.ScoreSelections
            .Select(sel => string.Equals(sel.Name, "MC", StringComparison.Ordinal)
                ? new ScoreSelection(sel.Name, sel.Index, Display: !sel.Display, sel.Aggregate, sel.Correlation)
                : sel)
            .ToList();

        // Act
        vm.ApplyScoreSelections(displayOnlyChanged);

        // Assert — Display-only change must NOT shift the aggregate range.
        // (R035: cursors don't reset; aggregate values are untouched.)
        var newMin = vm.ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var newMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        Assert.Equal(originalMin, newMin);
        Assert.Equal(originalMax, newMax);
    }

    [Fact]
    public void ApplyScoreSelections_SingleNarrowAggregateComponent_CursorsSpanNewRange()
    {
        // Arrange — pin to MC as the sole Aggregate component (same scenario as the crash test).
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var narrowComponent = firstStudent.Scores.First(s =>
            string.Equals(s.Name, "MC", StringComparison.Ordinal));
        var narrowOnlySelections = firstStudent.Scores
            .Select(s => new ScoreSelection(
                s.Name,
                s.Index,
                Display: true,
                Aggregate: string.Equals(s.Name, narrowComponent.Name, StringComparison.Ordinal)
                    && !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase),
                Correlation: true))
            .ToList();

        // Act
        vm.ApplyScoreSelections(narrowOnlySelections);

        // Assert — when the fallback fires, best grade lands at the new max and worst at the
        // new min. This pins the user-visible side of the fallback so cursors are at least
        // visible across the new (narrow) aggregate range rather than stuck at stale
        // wide-range positions from the original load.
        var newMin = vm.ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var newMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        Assert.All(vm.Cursors, c =>
        {
            Assert.True(c.Score >= newMin && c.Score <= newMax,
                $"Cursor for {c.Grade.DisplayName} score={c.Score} must lie within new aggregate " +
                $"range [{newMin}, {newMax}] after the narrow-aggregate fallback fires.");
        });
    }

    // -------------------------------------------------------------------
    // M002/S02/T02 — Cursor pinning semantics on ApplyScoreSelections
    //
    // When the AGGREGATE selection set changes, ApplyScoreSelections must re-seed
    // the cursors from the default curve at the new aggregate range (per MEM035).
    // Display-only and Correlation-only changes must NOT touch cursor positions.
    // -------------------------------------------------------------------

    [Fact]
    public void ApplyScoreSelections_AggregateChange_ResetsCursorsToDefaults()
    {
        // Arrange — load fixture; pick a non-Total score currently in the Aggregate set.
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var nonTotalScore = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Value > 0);

        // Hand-set the B cursor to a deliberately off-default Score (mimics a user drag).
        // Decrement by 1 so we stay within the valid B+ ≥ B ≥ B- ordering invariant
        // (MEM023 — OnCursorPropertyChanged fires synchronously on assignment, and a value
        // that breaks ordering throws "Cutoffs are out of order"). One unit off-default is
        // enough to detect a reset because SeedCursorsFromDefaults computes via the
        // InitialCutoffCalculator + barbell projection, which is deterministic for a fixed
        // (assessments, midpointCurve) pair — so the post-Apply cursor value lands on the
        // recomputed cutoff, not on the hand-set value.
        var bCursor = vm.Cursors.First(c => c.Grade.LetterGrade == LetterGrade.B);
        var handSetScore = bCursor.Score - 1;
        bCursor.Score = handSetScore;
        Assert.Equal(handSetScore, vm.Cursors.First(c => c.Grade.LetterGrade == LetterGrade.B).Score);

        // Build a selection list that toggles Aggregate=false on the chosen non-Total score.
        var modified = vm.ClassAssessment.ScoreSelections
            .Select(s =>
                s.Name == nonTotalScore.Name && s.Index == nonTotalScore.Index
                    ? s with { Aggregate = false }
                    : s)
            .ToList();

        // Act
        vm.ApplyScoreSelections(modified);

        // Assert — cursor moved off the hand-set value...
        var bCursorAfter = vm.Cursors.First(c => c.Grade.LetterGrade == LetterGrade.B);
        Assert.NotEqual(handSetScore, bCursorAfter.Score);

        // ...and the new value matches what a fresh InitialCutoffCalculator would compute
        // against the post-Apply Assessments. This mirrors the production projection in
        // MainWindowViewModel.SeedCursorsFromDefaults: defaultCurve via GenerateRanges,
        // midpointCurve filtered to entries with non-zero bounds, projected to CutoffCount(Midpoint).
        var defaultCurve = new DefaultCurveGenerator().GenerateRanges(vm.ClassAssessment.Assessments.Count);
        var midpointCurve = defaultCurve
            .Where(r => r.LowerBound > 0 || r.UpperBound > 0)
            .Select(r => new CutoffCount(r.Grade, r.Midpoint))
            .ToList();
        var expectedCutoffs = new InitialCutoffCalculator()
            .Calculate(vm.ClassAssessment.Assessments, midpointCurve);
        var expectedB = expectedCutoffs.First(c => c.Grade.LetterGrade == LetterGrade.B).Score;
        Assert.Equal(expectedB, bCursorAfter.Score);
    }

    [Fact]
    public void ApplyScoreSelections_DisplayOnlyChange_DoesNotResetCursors()
    {
        // Arrange — load fixture, hand-set two cursor Score values to off-default values
        // (mimics a user dragging cursors), then snapshot every cursor's Score.
        var vm = CreateViewModel();
        var bCursor = vm.Cursors.First(c => c.Grade.LetterGrade == LetterGrade.B);
        var cCursor = vm.Cursors.First(c => c.Grade.LetterGrade == LetterGrade.C);
        bCursor.Score = bCursor.Score + 7; // deliberate off-default
        cCursor.Score = cCursor.Score - 3; // deliberate off-default
        var snapshot = vm.Cursors.ToDictionary(c => c.Grade, c => c.Score);

        // Build a selection list that flips ONLY a Display flag (Aggregate, Correlation untouched).
        // Pick a non-Total score so we don't accidentally drop the Total from Aggregate.
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var target = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Value > 0);
        var modified = vm.ClassAssessment.ScoreSelections
            .Select(s =>
                s.Name == target.Name && s.Index == target.Index
                    ? s with { Display = false }
                    : s)
            .ToList();

        // Act
        vm.ApplyScoreSelections(modified);

        // Assert — every cursor's Score is identical to the snapshot.
        foreach (var cursor in vm.Cursors)
        {
            Assert.True(snapshot.ContainsKey(cursor.Grade),
                $"Cursor for {cursor.Grade.DisplayName} appeared after a Display-only change");
            Assert.Equal(snapshot[cursor.Grade], cursor.Score);
        }
        Assert.Equal(snapshot.Count, vm.Cursors.Count);
    }

    [Fact]
    public void ApplyScoreSelections_CorrelationOnlyChange_DoesNotResetCursors()
    {
        // Arrange — same shape as Display-only but flipping Correlation instead.
        var vm = CreateViewModel();
        var bCursor = vm.Cursors.First(c => c.Grade.LetterGrade == LetterGrade.B);
        var cCursor = vm.Cursors.First(c => c.Grade.LetterGrade == LetterGrade.C);
        bCursor.Score = bCursor.Score + 11;
        cCursor.Score = cCursor.Score - 5;
        var snapshot = vm.Cursors.ToDictionary(c => c.Grade, c => c.Score);

        var firstStudent = vm.ClassAssessment.Assessments.First();
        var target = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Value > 0);
        var modified = vm.ClassAssessment.ScoreSelections
            .Select(s =>
                s.Name == target.Name && s.Index == target.Index
                    ? s with { Correlation = false }
                    : s)
            .ToList();

        // Act
        vm.ApplyScoreSelections(modified);

        // Assert — no cursor moved.
        foreach (var cursor in vm.Cursors)
        {
            Assert.True(snapshot.ContainsKey(cursor.Grade),
                $"Cursor for {cursor.Grade.DisplayName} appeared after a Correlation-only change");
            Assert.Equal(snapshot[cursor.Grade], cursor.Score);
        }
        Assert.Equal(snapshot.Count, vm.Cursors.Count);
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

    [Fact]
    public void OnHoveredStudentIdChanged_FilteredScores_PassedToCard()
    {
        // Arrange — flip Display=false on the first score in the seeded selections so
        // BuildDisplayScores excludes it from the drill-down card.
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        Assert.True(firstStudent.Scores.Count >= 2, "Fixture must have ≥2 scores for this test.");

        var excluded = firstStudent.Scores[0];
        var newSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => (s.Name == excluded.Name && s.Index == excluded.Index)
                ? s with { Display = false }
                : s)
            .ToList();
        vm.ClassAssessment.ScoreSelections = newSelections;

        // Act — assigning HoveredStudentId triggers OnHoveredStudentIdChanged which
        // must construct a StudentCardViewModel with the filtered DisplayScores.
        vm.HoveredStudentId = firstStudent.Id;

        // Assert
        Assert.NotNull(vm.HoveredStudent);
        Assert.Equal(firstStudent.Scores.Count - 1, vm.HoveredStudent!.DisplayScores.Count);
        Assert.DoesNotContain(vm.HoveredStudent.DisplayScores,
            s => s.Name == excluded.Name && s.Index == excluded.Index);
    }
}
