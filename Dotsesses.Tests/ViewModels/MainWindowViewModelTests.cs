namespace Dotsesses.Tests.ViewModels;

using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Calculators;
using Dotsesses.Services;
using Dotsesses.Tests.Fixtures;
using Dotsesses.UI;
using Dotsesses.Models;
using OxyPlot;

public class MainWindowViewModelTests
{
    private static MainWindowViewModel CreateViewModel()
        => MainWindowViewModel.CreateForTesting(TestFixtures.IpExamScoresXlsx());

    [Fact]
    public void Constructor_BuildsDotplotModelWithExpectedShape()
    {
        // The dotplot has 4 axes (SharedX, StatsY, DotY, CursorY) and 2
        // ScatterSeries (unselected + selected). Total points across both
        // series equals the student count. Background is transparent for
        // theme integration; the plot-area border is the dark-theme grey.
        var vm = CreateViewModel();

        Assert.NotNull(vm.DotplotModel);
        Assert.Equal(OxyColors.Transparent, vm.DotplotModel.Background);
        Assert.Equal(OxyColor.FromRgb(60, 60, 60), vm.DotplotModel.PlotAreaBorderColor);

        Assert.Equal(4, vm.DotplotModel.Axes.Count);
        Assert.Contains(vm.DotplotModel.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Bottom);
        Assert.Contains(vm.DotplotModel.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Left);

        Assert.Equal(2, vm.DotplotModel.Series.Count);
        var s0 = Assert.IsType<OxyPlot.Series.ScatterSeries>(vm.DotplotModel.Series[0]);
        var s1 = Assert.IsType<OxyPlot.Series.ScatterSeries>(vm.DotplotModel.Series[1]);
        Assert.Equal(vm.ClassAssessment.Assessments.Count, s0.Points.Count + s1.Points.Count);

        Assert.NotEmpty(vm.ClassAssessment.Assessments);
    }

    [Fact]
    public void Constructor_BuildsComplianceGrid_AllGradesEnabledByDefault()
    {
        var vm = CreateViewModel();

        Assert.NotNull(vm.ComplianceGrid);
        Assert.Equal(11, vm.ComplianceGrid.Rows.Count); // A through F (including C-, D+, D)

        // Catch-all (F) is structurally always enabled and not present
        // in Slots; assert via session.CurrentState.EnabledGrades.
        var fRow = vm.ComplianceGrid.Rows.FirstOrDefault(r => r.Grade.LetterGrade == LetterGrade.F);
        Assert.NotNull(fRow);
        Assert.True(fRow.IsEnabled);
        Assert.Contains(vm.GradingSession.CurrentState.EnabledGrades, g => g.LetterGrade == LetterGrade.F);
    }

    private static IReadOnlyList<ScoreSelection> BuildSelections(
        IReadOnlyList<Score> scores,
        Func<Score, ScoreSelection>? customize = null)
    {
        return scores
            .Select(s => customize?.Invoke(s) ??
                new ScoreSelection(s.Name, s.Index, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true))
            .ToList();
    }

    /// <summary>
    /// Builds a selection list where only the score named <paramref name="aggregateName"/>
    /// is in the Aggregate set. Used by the M002/S05 narrow-aggregate scenarios:
    /// pinning to "MC" collapses the per-student range to 0–10 and triggers the
    /// session's narrow-aggregate fallback.
    /// </summary>
    private static IReadOnlyList<ScoreSelection> NarrowAggregateOn(
        IReadOnlyCollection<Score> scores, string aggregateName)
    {
        return scores
            .Select(s => new ScoreSelection(
                s.Name, s.Index,
                ScoreColumnType.Numeric,
                Display: true,
                Aggregate: string.Equals(s.Name, aggregateName, StringComparison.Ordinal)
                    && !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase),
                Correlation: true))
            .ToList();
    }

    [Fact]
    public void ApplyScoreSelections_MutatesClassAssessmentScoreSelections()
    {
        var vm = CreateViewModel();
        var newSelections = BuildSelections(vm.ClassAssessment.Assessments.First().Scores, s =>
            new ScoreSelection(s.Name, s.Index, ScoreColumnType.Numeric, Display: true,
                Aggregate: !string.Equals(s.Name, "MC", StringComparison.OrdinalIgnoreCase),
                Correlation: true));

        vm.ApplyScoreSelections(newSelections);

        Assert.Same(newSelections, vm.ClassAssessment.ScoreSelections);
    }

    [Fact]
    public void ApplyScoreSelections_RecalculatesAggregateOnEveryStudent()
    {
        // Excluding MC must change at least one student's AggregateGrade
        // and produce the manually-computed sum on the first student.
        var vm = CreateViewModel();
        var students = vm.ClassAssessment.Assessments.ToList();
        const string excluded = "MC";
        var newSelections = BuildSelections(students.First().Scores, s =>
            new ScoreSelection(s.Name, s.Index, ScoreColumnType.Numeric, Display: true,
                Aggregate: !string.Equals(s.Name, excluded, StringComparison.Ordinal),
                Correlation: true));

        var beforeAggregates = students.Select(st => st.AggregateGrade).ToList();
        var expectedFirst = (int)students.First().Scores
            .Where(s => !string.Equals(s.Name, excluded, StringComparison.Ordinal))
            .Sum(s => s.Value);

        vm.ApplyScoreSelections(newSelections);

        Assert.NotEqual(beforeAggregates, students.Select(st => st.AggregateGrade).ToList());
        Assert.Equal(expectedFirst, students.First().AggregateGrade);
    }

    [Fact]
    public void ApplyScoreSelections_SetsHasUnsavedChangesTrue()
    {
        var vm = CreateViewModel();
        Assert.False(vm.HasUnsavedChanges);

        vm.ApplyScoreSelections(BuildSelections(vm.ClassAssessment.Assessments.First().Scores));

        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public void ApplyScoreSelections_TriggersGradeCountRefresh()
    {
        // Total count across compliance rows equals student count both
        // before and after — proving grade counts were recomputed without
        // losing students.
        var vm = CreateViewModel();
        var studentCount = vm.ClassAssessment.Assessments.Count;
        var beforeSum = vm.ComplianceGrid!.Rows.Sum(r => r.CurrentCount);

        vm.ApplyScoreSelections(BuildSelections(vm.ClassAssessment.Assessments.First().Scores));

        Assert.Equal(studentCount, beforeSum);
        Assert.Equal(studentCount, vm.ComplianceGrid!.Rows.Sum(r => r.CurrentCount));
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
    public void LoadFromExcelFile_ConstructsGradingSession_AllSlotsEnabled()
    {
        // Each load builds a fresh session with every slot enabled —
        // including non-mandatory grades (C-, D+, D in the production
        // curve) which the constructor seeds at fallback-band positions.
        // Issue #14 follow-up regression coverage.
        var vm = CreateViewModel();

        Assert.NotNull(vm.GradingSession);
        Assert.NotEmpty(vm.GradingSession.Slots);
        Assert.NotEmpty(vm.GradingSession.CurrentState.Cutoffs);
        Assert.All(new[] { LetterGrade.CMinus, LetterGrade.DPlus, LetterGrade.D }, letter =>
        {
            var slot = vm.GradingSession.Slots.FirstOrDefault(s => s.Grade.LetterGrade == letter);
            Assert.NotNull(slot);
            Assert.True(slot.IsEnabled, $"Slot for {letter} (no mandatory range) must be enabled.");
        });
    }

    [Fact]
    public void LoadFromExcelFile_TwoLoads_ProduceFreshSession()
    {
        var vm = CreateViewModel();
        var firstSession = vm.GradingSession;

        vm.LoadFromExcelFile(TestFixtures.IpExamScoresXlsx());

        Assert.NotSame(firstSession, vm.GradingSession);
    }

    [Fact]
    public async Task LoadStateAsync_V2File_HydratesGradingSession()
    {
        // Round-trip: save a session with a non-default cutoff, load
        // into a fresh VM, verify the session reflects the saved state.
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

            Assert.Equal(newAScore,
                loadVm.GradingSession.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A).Score);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Fact]
    public void ApplyScoreSelections_RebuildsDotplotPointsForNewAggregates()
    {
        // M001 S04 UAT Step 2 regression: dotplot Series points must
        // reflect the new AggregateGrade values after Apply (previously
        // the per-student aggregate updated but the plot didn't).
        var vm = CreateViewModel();
        var pointsBefore = vm.DotplotModel.Series.OfType<OxyPlot.Series.ScatterSeries>()
            .SelectMany(s => s.Points).Select(p => p.X).OrderBy(x => x).ToList();

        var firstStudent = vm.ClassAssessment.Assessments.First();
        var nonTotal = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Value > 0);
        var modified = vm.ClassAssessment.ScoreSelections
            .Select(s => s.Name == nonTotal.Name && s.Index == nonTotal.Index
                ? s with { Aggregate = false } : s)
            .ToList();

        vm.ApplyScoreSelections(modified);

        var pointsAfter = vm.DotplotModel.Series.OfType<OxyPlot.Series.ScatterSeries>()
            .SelectMany(s => s.Points).Select(p => p.X).OrderBy(x => x).ToList();
        Assert.NotEqual(pointsBefore, pointsAfter);
        Assert.Equal(
            vm.ClassAssessment.Assessments.Select(a => (double)a.AggregateGrade).OrderBy(x => x).ToList(),
            pointsAfter);
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
        var vm = CreateViewModel();
        var ex = Record.Exception(() => vm.ApplyScoreSelections(Array.Empty<ScoreSelection>()));

        Assert.Null(ex);
        Assert.All(vm.ClassAssessment.Assessments, st => Assert.Equal(0, st.AggregateGrade));
    }

    [Fact]
    public void ApplyScoreSelections_SingleNarrowAggregateComponent_DoesNotCrash()
    {
        // M002/S05/T03 SC1 Case 7 repro: pin to "MC" as sole non-Total
        // Aggregate so the per-student range collapses to 0–10. The
        // session's narrow-aggregate fallback must keep slots monotonic.
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var mc = firstStudent.Scores.First(s => string.Equals(s.Name, "MC", StringComparison.Ordinal));
        Assert.True(mc.Value <= 10, $"Expected MC ≤10; fixture gave MC={mc.Value}");

        var ex = Record.Exception(() => vm.ApplyScoreSelections(NarrowAggregateOn(firstStudent.Scores, "MC")));
        Assert.Null(ex);

        var slotsByOrder = vm.GradingSession.Slots.OrderBy(s => s.Grade.Order).ToList();
        for (int i = 0; i < slotsByOrder.Count - 1; i++)
        {
            Assert.True(slotsByOrder[i].Score >= slotsByOrder[i + 1].Score,
                $"{slotsByOrder[i].Grade.DisplayName}={slotsByOrder[i].Score} must be ≥ " +
                $"{slotsByOrder[i + 1].Grade.DisplayName}={slotsByOrder[i + 1].Score}.");
        }
    }

    [Fact]
    public void ApplyScoreSelections_AggregateChange_NewAggregateRangeIsObservable()
    {
        // After narrowing Aggregate to MC only, the source values that
        // WireViolinPlot reads must reflect the new (narrow) range.
        var vm = CreateViewModel();
        var originalMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);

        vm.ApplyScoreSelections(NarrowAggregateOn(vm.ClassAssessment.Assessments.First().Scores, "MC"));

        var newMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        Assert.True(newMax < 50,
            $"After MC-only Aggregate, max should be ≪ original max; got {newMax} vs {originalMax}.");
    }

    [Fact]
    public void ApplyScoreSelections_DisplayOnlyChange_AggregateRangeUnchanged()
    {
        var vm = CreateViewModel();
        var originalMin = vm.ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var originalMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);

        var displayOnlyChanged = vm.ClassAssessment.ScoreSelections
            .Select(sel => string.Equals(sel.Name, "MC", StringComparison.Ordinal)
                ? sel with { Display = !sel.Display }
                : sel)
            .ToList();

        vm.ApplyScoreSelections(displayOnlyChanged);

        Assert.Equal(originalMin, vm.ClassAssessment.Assessments.Min(a => a.AggregateGrade));
        Assert.Equal(originalMax, vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade));
    }

    [Fact]
    public void ApplyScoreSelections_SingleNarrowAggregateComponent_CursorsSpanNewRange()
    {
        // When the fallback fires, slots span [newMin, newMax] — proves
        // user-visible cursors aren't stuck at stale wide-range positions.
        var vm = CreateViewModel();
        vm.ApplyScoreSelections(NarrowAggregateOn(vm.ClassAssessment.Assessments.First().Scores, "MC"));

        var newMin = vm.ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var newMax = vm.ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        Assert.All(vm.GradingSession.Slots, s =>
            Assert.True(s.Score >= newMin && s.Score <= newMax,
                $"Slot {s.Grade.DisplayName}={s.Score} must lie in [{newMin},{newMax}]."));
    }

    // Cursor-pinning semantics (MEM035): an aggregate-set change re-seeds
    // cursors via session.ReseedFromDefaults; Display-only and
    // Correlation-only changes leave cursors untouched.

    [Fact]
    public void ApplyScoreSelections_AggregateChange_ResetsCursorsToDefaults()
    {
        // Hand-set the B slot off-default by 1 so we can detect a reset
        // without violating B+ ≥ B ≥ B- ordering. The post-Apply value
        // will land on the deterministic recomputed cutoff.
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var nonTotal = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Value > 0);
        var bSlot = vm.GradingSession.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var handSet = bSlot.Score - 1;
        vm.GradingSession.MoveCutoff(bSlot.Grade, handSet, originator: this);

        var modified = vm.ClassAssessment.ScoreSelections
            .Select(s => s.Name == nonTotal.Name && s.Index == nonTotal.Index
                ? s with { Aggregate = false } : s)
            .ToList();
        vm.ApplyScoreSelections(modified);

        var bAfter = vm.GradingSession.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        Assert.NotEqual(handSet, bAfter.Score);

        var midpointCurve = new DefaultCurveGenerator()
            .GenerateRanges(vm.ClassAssessment.Assessments.Count)
            .Where(r => r.LowerBound > 0 || r.UpperBound > 0)
            .Select(r => new CutoffCount(r.Grade, r.Midpoint))
            .ToList();
        var expectedB = new InitialCutoffCalculator()
            .Calculate(vm.ClassAssessment.Assessments, midpointCurve)
            .First(c => c.Grade.LetterGrade == LetterGrade.B).Score;
        Assert.Equal(expectedB, bAfter.Score);
    }

    [Theory]
    [InlineData(false, true)]   // Display-only
    [InlineData(true, false)]   // Correlation-only
    public void ApplyScoreSelections_NonAggregateFlagChange_DoesNotResetCursors(
        bool changeDisplay, bool changeCorrelation)
    {
        var vm = CreateViewModel();
        var bSlot = vm.GradingSession.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var cSlot = vm.GradingSession.Slots.First(s => s.Grade.LetterGrade == LetterGrade.C);
        vm.GradingSession.MoveCutoff(bSlot.Grade, bSlot.Score + 1, originator: this);
        vm.GradingSession.MoveCutoff(cSlot.Grade, cSlot.Score - 1, originator: this);
        var snapshot = vm.GradingSession.Slots.ToDictionary(s => s.Grade, s => s.Score);

        var firstStudent = vm.ClassAssessment.Assessments.First();
        var target = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Value > 0);
        var modified = vm.ClassAssessment.ScoreSelections
            .Select(s => s.Name == target.Name && s.Index == target.Index
                ? s with
                {
                    Display = changeDisplay ? !s.Display : s.Display,
                    Correlation = changeCorrelation ? !s.Correlation : s.Correlation,
                } : s)
            .ToList();

        vm.ApplyScoreSelections(modified);

        Assert.Equal(snapshot.Count, vm.GradingSession.Slots.Count);
        foreach (var slot in vm.GradingSession.Slots)
        {
            Assert.Equal(snapshot[slot.Grade], slot.Score);
        }
    }

    [Fact]
    public void OnHoveredStudentIdChanged_FilteredScores_PassedToCard()
    {
        // Display=false on a score must exclude it from the drill-down card.
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        Assert.True(firstStudent.Scores.Count >= 2);
        var excluded = firstStudent.Scores[0];

        vm.ClassAssessment.ScoreSelections = vm.ClassAssessment.ScoreSelections
            .Select(s => (s.Name == excluded.Name && s.Index == excluded.Index)
                ? s with { Display = false } : s)
            .ToList();
        vm.HoveredStudentId = firstStudent.Id;

        Assert.NotNull(vm.HoveredStudent);
        Assert.Equal(firstStudent.Scores.Count - 1, vm.HoveredStudent!.DisplayScores.Count);
        Assert.DoesNotContain(vm.HoveredStudent.DisplayScores,
            s => s.Name == excluded.Name && s.Index == excluded.Index);
    }

    // ===== Slice 2 — Type transitions on Apply =====

    private static IReadOnlyList<ScoreSelection> SelectionsWithTypeFlip(
        IReadOnlyList<ScoreSelection> existing, string flippedName, ScoreColumnType newType)
    {
        return existing
            .Select(s => string.Equals(s.Name, flippedName, StringComparison.Ordinal)
                ? s with { Type = newType, Aggregate = newType == ScoreColumnType.Numeric ? s.Aggregate : false }
                : s)
            .ToList();
    }

    [Fact]
    public void ApplyScoreSelections_NumericToCategorical_MovesDataToAttributes()
    {
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var flipName = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase)).Name;
        var originalValue = firstStudent.Scores.First(s => s.Name == flipName).Value;

        var newSelections = SelectionsWithTypeFlip(
            vm.ClassAssessment.ScoreSelections, flipName, ScoreColumnType.Categorical);

        vm.ApplyScoreSelections(newSelections);

        Assert.DoesNotContain(firstStudent.Scores, s => s.Name == flipName);
        var attr = firstStudent.Attributes.Single(a => a.Name == flipName);
        // "R" round-trip format gives invariant-culture text — must parse back to the same value.
        Assert.Equal(originalValue,
            double.Parse(attr.Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ApplyScoreSelections_NumericToCategorical_PreservesScoreComment()
    {
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var flipName = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase)).Name;
        firstStudent.Scores.First(s => s.Name == flipName).Comment = "preserved";

        var newSelections = SelectionsWithTypeFlip(
            vm.ClassAssessment.ScoreSelections, flipName, ScoreColumnType.Categorical);

        vm.ApplyScoreSelections(newSelections);

        var attr = firstStudent.Attributes.Single(a => a.Name == flipName);
        Assert.Equal("preserved", attr.Comment);
    }

    [Fact]
    public void ApplyScoreSelections_NumericToCategorical_RemovesFromAggregateSum()
    {
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var flipName = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase)).Name;
        var aggregateBefore = firstStudent.AggregateGrade;
        var flippedValue = firstStudent.Scores.First(s => s.Name == flipName).Value;

        var newSelections = SelectionsWithTypeFlip(
            vm.ClassAssessment.ScoreSelections, flipName, ScoreColumnType.Categorical);

        vm.ApplyScoreSelections(newSelections);

        // The flipped column dropped out of the aggregate; the new aggregate is the
        // old aggregate minus the flipped column's contribution.
        Assert.Equal(aggregateBefore - (int)flippedValue, firstStudent.AggregateGrade);
    }

    [Fact]
    public void ApplyScoreSelections_CategoricalToNumeric_MovesDataToScores()
    {
        // Set up: flip a column to Categorical, then flip it back.
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var flipName = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase)).Name;
        var originalValue = firstStudent.Scores.First(s => s.Name == flipName).Value;

        vm.ApplyScoreSelections(SelectionsWithTypeFlip(
            vm.ClassAssessment.ScoreSelections, flipName, ScoreColumnType.Categorical));
        Assert.DoesNotContain(firstStudent.Scores, s => s.Name == flipName);

        // Now flip back.
        vm.ApplyScoreSelections(SelectionsWithTypeFlip(
            vm.ClassAssessment.ScoreSelections, flipName, ScoreColumnType.Numeric));

        var restoredScore = firstStudent.Scores.Single(s => s.Name == flipName);
        Assert.Equal(originalValue, restoredScore.Value);
        Assert.DoesNotContain(firstStudent.Attributes, a => a.Name == flipName);
    }

    [Fact]
    public void ApplyScoreSelections_CategoricalToNumeric_PreservesAttributeComment()
    {
        var vm = CreateViewModel();
        var firstStudent = vm.ClassAssessment.Assessments.First();
        var flipName = firstStudent.Scores.First(s =>
            !string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase)).Name;
        firstStudent.Scores.First(s => s.Name == flipName).Comment = "round-trip";

        vm.ApplyScoreSelections(SelectionsWithTypeFlip(
            vm.ClassAssessment.ScoreSelections, flipName, ScoreColumnType.Categorical));
        vm.ApplyScoreSelections(SelectionsWithTypeFlip(
            vm.ClassAssessment.ScoreSelections, flipName, ScoreColumnType.Numeric));

        var restored = firstStudent.Scores.Single(s => s.Name == flipName);
        Assert.Equal("round-trip", restored.Comment);
    }
}
