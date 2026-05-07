namespace Dotsesses.Tests.UI;

using System.Linq;
using Dotsesses.Models;
using Dotsesses.Tests.Fixtures;
using Dotsesses.UI;
using Xunit;

/// <summary>
/// Slice 4 / issue #10: ComplianceGridViewModel owns the compliance
/// grid and subscribes to GradingSession.
/// </summary>
public class ComplianceGridViewModelTests
{
    private static ComplianceGridViewModel BuildVm(out GradingSession session)
    {
        session = TestFixtures.SessionForGrading();
        var classAssessment = TestFixtures.ClassAssessmentForGrading();
        return new ComplianceGridViewModel(classAssessment, session);
    }

    [Fact]
    public void Constructor_BuildsOneRowPerDefaultCurveGrade()
    {
        // The fixture's DefaultCurve has 4 entries: A, B, C, F (catch-all).
        var vm = BuildVm(out _);

        Assert.Equal(4, vm.Rows.Count);
        Assert.Equal(
            new[] { LetterGrade.A, LetterGrade.B, LetterGrade.C, LetterGrade.F },
            vm.Rows.Select(r => r.Grade.LetterGrade).ToArray());
    }

    [Fact]
    public void Constructor_PopulatesTargetRangeFromDefaultCurve()
    {
        var vm = BuildVm(out _);

        Assert.Equal((10, 10), (vm.Rows[0].LowerTarget, vm.Rows[0].UpperTarget));
        Assert.Equal((20, 20), (vm.Rows[1].LowerTarget, vm.Rows[1].UpperTarget));
        Assert.Equal((20, 20), (vm.Rows[2].LowerTarget, vm.Rows[2].UpperTarget));
        Assert.Equal((0, 0), (vm.Rows[3].LowerTarget, vm.Rows[3].UpperTarget));
    }

    [Fact]
    public void Constructor_PopulatesInitialCount_FromSession()
    {
        var vm = BuildVm(out var session);

        // Each row's count must match session.CurrentState.Counts on construction.
        foreach (var row in vm.Rows)
        {
            var expected = session.CurrentState.Counts
                .First(c => c.Grade.Equals(row.Grade)).Count;
            Assert.Equal(expected, row.CurrentCount);
        }
    }

    [Fact]
    public void Constructor_PopulatesIsEnabled_FromSession()
    {
        // Fresh session: every slot enabled, catch-all enabled.
        var vm = BuildVm(out _);

        Assert.All(vm.Rows, r => Assert.True(r.IsEnabled));
    }

    [Fact]
    public void MoveCutoff_OnSession_UpdatesCurrentCount()
    {
        var vm = BuildVm(out var session);
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);

        // Top fixture student is 540; push A past it (within the +5
        // upper drag bound) so the A bin empties.
        session.MoveCutoff(slotA.Grade, 545, originator: this);

        var aRow = vm.Rows.First(r => r.Grade.LetterGrade == LetterGrade.A);
        Assert.Equal(0, aRow.CurrentCount);
    }

    [Fact]
    public void DisableGrade_OnSession_FlipsRowIsEnabledFalse()
    {
        var vm = BuildVm(out var session);
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);

        session.DisableGrade(slotB.Grade);

        var bRow = vm.Rows.First(r => r.Grade.LetterGrade == LetterGrade.B);
        Assert.False(bRow.IsEnabled);
    }

    [Fact]
    public void Toggle_RowToDisabled_CallsSessionDisableGrade()
    {
        var vm = BuildVm(out var session);
        var bRow = vm.Rows.First(r => r.Grade.LetterGrade == LetterGrade.B);

        Assert.True(bRow.IsEnabled);
        bRow.IsEnabled = false; // simulate user toggling the checkbox

        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        Assert.False(slotB.IsEnabled);
    }

    [Fact]
    public void Toggle_RowToEnabled_CallsSessionEnableGrade()
    {
        var vm = BuildVm(out var session);
        var bRow = vm.Rows.First(r => r.Grade.LetterGrade == LetterGrade.B);

        // Disable first so we can re-enable.
        session.DisableGrade(bRow.Grade);
        Assert.False(bRow.IsEnabled);

        bRow.IsEnabled = true;

        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        Assert.True(slotB.IsEnabled);
    }

    [Fact]
    public void Toggle_OnCatchAllRow_DoesNotThrow_AndDoesNotMutateSession()
    {
        // The catch-all is structurally always enabled (ADR-0011) and has
        // no draggable slot — toggling its row must be inert, not throw
        // ArgumentException out of session.DisableGrade.
        var vm = BuildVm(out var session);
        var fRow = vm.Rows.First(r => r.Grade.LetterGrade == LetterGrade.F);

        var ex = Record.Exception(() => fRow.IsEnabled = false);

        Assert.Null(ex);
        // Catch-all stays in EnabledGrades regardless of the row flag.
        Assert.Contains(session.CurrentState.EnabledGrades, g => g.LetterGrade == LetterGrade.F);
    }

    [Fact]
    public void SyncFromSession_DoesNotReentrantlyToggle()
    {
        // session.LastChange fires → SyncFromSession sets row.IsEnabled →
        // row's onEnabledChanged callback runs → must NOT call session
        // back, otherwise we get a feedback loop. Drive a session change
        // and assert no extra LastChange events fire from the sync.
        var vm = BuildVm(out var session);
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);

        int extraEmissions = 0;
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GradingSession.LastChange)) extraEmissions++;
        };

        session.MoveCutoff(slotA.Grade, slotA.Score - 5, originator: this);

        // Exactly one LastChange (the MoveCutoff) — no echo from the sync.
        Assert.Equal(1, extraEmissions);
    }
}
