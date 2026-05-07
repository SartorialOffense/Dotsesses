namespace Dotsesses.Tests.UI;

using Dotsesses.Models;
using Dotsesses.Tests.Fixtures;
using Dotsesses.UI;

public class GradingSessionTests
{
    [Fact]
    public void MoveCutoff_WithValidScore_CommitsAndBroadcasts()
    {
        // Arrange
        var session = TestFixtures.SessionForGrading();
        var originator = new object();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var midwayBetweenAandB = (slotA.Score + slotB.Score) / 2;

        // Act
        var result = session.MoveCutoff(slotB.Grade, midwayBetweenAandB, originator);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Failure);
        Assert.Equal(
            midwayBetweenAandB,
            session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B).Score);
        Assert.True(session.LastChange.IsFrom(originator));
    }

    [Fact]
    public void MoveCutoff_ToSameScoreAsAdjacentSlot_FailsWithWouldOverlap()
    {
        // Arrange
        var session = TestFixtures.SessionForGrading();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var stateBefore = session.LastChange.State;

        // Act — move A down to B's exact score (would collide)
        var result = session.MoveCutoff(slotA.Grade, slotB.Score, new object());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(CutoffMoveFailure.WouldOverlap, result.Failure);
        Assert.Same(stateBefore, session.LastChange.State);
        Assert.Equal(450, slotA.Score);  // unchanged
    }

    [Fact]
    public void MoveCutoff_PastAdjacentSlot_FailsWithOrderingViolation()
    {
        // Arrange
        var session = TestFixtures.SessionForGrading();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var stateBefore = session.LastChange.State;

        // Act — move A below B's score (A.Order < B.Order means A.Score must stay > B.Score)
        var result = session.MoveCutoff(slotA.Grade, slotB.Score - 50, new object());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(CutoffMoveFailure.OrderingViolation, result.Failure);
        Assert.Same(stateBefore, session.LastChange.State);
    }

    [Fact]
    public void MoveCutoff_AboveMaxAggregatePlusMargin_FailsWithOutOfRange()
    {
        // Fixture: AggregateGrades 50..540 → max + 5 = 545.
        var session = TestFixtures.SessionForGrading();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var stateBefore = session.LastChange.State;

        var result = session.MoveCutoff(slotA.Grade, 600, new object());

        Assert.False(result.Success);
        Assert.Equal(CutoffMoveFailure.OutOfRange, result.Failure);
        Assert.Same(stateBefore, session.LastChange.State);
    }

    [Fact]
    public void MoveCutoff_AtUpperBoundExactly_IsAccepted_OnePastFails()
    {
        // Fixture: max=540 → max+5=545 (inclusive). 546 is OutOfRange.
        var session = TestFixtures.SessionForGrading();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);

        var atBound = session.MoveCutoff(slotA.Grade, 545, new object());
        Assert.True(atBound.Success);

        var pastBound = session.MoveCutoff(slotA.Grade, 546, new object());
        Assert.False(pastBound.Success);
        Assert.Equal(CutoffMoveFailure.OutOfRange, pastBound.Failure);
    }

    [Fact]
    public void MoveCutoff_HasNoLowerOutOfRangeBound_NeighborSpacingIsTheOnlyFloor()
    {
        // Issue #18 follow-up: the lower OutOfRange bound is gone so
        // untargeted slots whose initial seed sits in the fallback band
        // below `min - 8` can be dragged back up, AND a targeted slot
        // can be dragged below `min - 8` whenever its lower neighbor
        // permits. The catch-all (or whichever worse neighbor is
        // enabled) is now the only floor, via OrderingViolation /
        // WouldOverlap.
        var session = TestFixtures.SessionForGrading();
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        session.DisableGrade(TestFixtures.GradeC);

        // Look up the catch-all's actual score from session state — its
        // exact value depends on the fixture's data envelope and the
        // legacy-matched fallback formula in GradingSession.
        var catchAllScore = session.CurrentState.Cutoffs
            .Single(c => c.Grade.LetterGrade == LetterGrade.F)
            .Score;

        // One above the catch-all: succeeds (neighbor spacing satisfied).
        // This score is well below the prior OutOfRange floor (which
        // was `min − 8 = 42` in the fixture), so passing here proves the
        // floor is gone.
        var lowMove = session.MoveCutoff(slotB.Grade, catchAllScore + 1, new object());
        Assert.True(lowMove.Success);
        Assert.Equal(catchAllScore + 1, slotB.Score);

        // Same score as catch-all → WouldOverlap (not OutOfRange).
        var atCatchAll = session.MoveCutoff(slotB.Grade, catchAllScore, new object());
        Assert.False(atCatchAll.Success);
        Assert.Equal(CutoffMoveFailure.WouldOverlap, atCatchAll.Failure);

        // Below the catch-all → OrderingViolation (not OutOfRange).
        var belowCatchAll = session.MoveCutoff(slotB.Grade, catchAllScore - 1, new object());
        Assert.False(belowCatchAll.Success);
        Assert.Equal(CutoffMoveFailure.OrderingViolation, belowCatchAll.Failure);
    }

    [Fact]
    public void MoveCutoff_OnStructuralCatchAll_ThrowsArgumentException()
    {
        // Per ADR-0011 the catch-all is the lowest-Order grade in
        // DefaultCurve. Fixture: DefaultCurve = [A, B, C, F] → F is
        // the catch-all (never draggable).
        var session = TestFixtures.SessionForGrading();

        Assert.Throws<ArgumentException>(() =>
            session.MoveCutoff(TestFixtures.GradeF, 100, new object()));
    }

    [Fact]
    public void LastChange_IsFrom_DistinguishesOriginatorFromOthersAndNull()
    {
        var session = TestFixtures.SessionForGrading();
        var originator = new object();
        var someoneElse = new object();
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);

        session.MoveCutoff(slotB.Grade, slotB.Score + 5, originator);

        Assert.True(session.LastChange.IsFrom(originator));
        Assert.False(session.LastChange.IsFrom(someoneElse));
        Assert.False(session.LastChange.IsFrom(null));
    }

    [Fact]
    public void CanMoveCutoff_WithValidScore_ReturnsOkAndDoesNotMutate()
    {
        var session = TestFixtures.SessionForGrading();
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var initialScore = slotB.Score;
        var stateBefore = session.LastChange.State;

        var result = session.CanMoveCutoff(slotB.Grade, initialScore + 10);

        Assert.True(result.Success);
        Assert.Equal(initialScore, slotB.Score);
        Assert.Same(stateBefore, session.LastChange.State);
    }

    [Fact]
    public void CanMoveCutoff_WithInvalidScore_ReturnsFailureAndDoesNotMutate()
    {
        var session = TestFixtures.SessionForGrading();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var stateBefore = session.LastChange.State;

        var result = session.CanMoveCutoff(slotA.Grade, slotB.Score);

        Assert.False(result.Success);
        Assert.Equal(CutoffMoveFailure.WouldOverlap, result.Failure);
        Assert.Same(stateBefore, session.LastChange.State);
    }

    [Fact]
    public void DisableGrade_FlagsSlotAndRemovesFromEnabledGrades()
    {
        var session = TestFixtures.SessionForGrading();
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var stateBefore = session.LastChange.State;

        session.DisableGrade(slotB.Grade);

        Assert.False(slotB.IsEnabled);
        Assert.DoesNotContain(slotB.Grade, session.CurrentState.EnabledGrades);
        Assert.NotSame(stateBefore, session.LastChange.State);
    }

    [Fact]
    public void DisableGrade_EmitsExactlyOneLastChange()
    {
        var session = TestFixtures.SessionForGrading();
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var lastChangeFires = 0;
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GradingSession.LastChange)) lastChangeFires++;
        };

        session.DisableGrade(slotB.Grade);

        Assert.Equal(1, lastChangeFires);
    }

    [Fact]
    public void MoveCutoff_OnDisabledGrade_FailsWithGradeNotEnabled()
    {
        var session = TestFixtures.SessionForGrading();
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        session.DisableGrade(slotB.Grade);

        var result = session.MoveCutoff(slotB.Grade, slotB.Score + 5, new object());

        Assert.False(result.Success);
        Assert.Equal(CutoffMoveFailure.GradeNotEnabled, result.Failure);
    }

    [Fact]
    public void EnableGrade_AfterDisable_PlacesAtMidpointBetweenEnabledNeighbors()
    {
        // Arrange — move A first so the midpoint is unambiguous.
        // After move: A=400, B=250 (current), C=50 (catch-all).
        // After disable B + re-enable: B should reseed via PlaceNewCursor → midpoint(A,C) = (400+50)/2 = 225.
        var session = TestFixtures.SessionForGrading();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);

        session.MoveCutoff(slotA.Grade, 400, new object());
        session.DisableGrade(slotB.Grade);
        session.EnableGrade(slotB.Grade);

        Assert.True(slotB.IsEnabled);
        Assert.Equal(225, slotB.Score);
        Assert.Contains(slotB.Grade, session.CurrentState.EnabledGrades);
    }

    [Fact]
    public void EnableGrade_EmitsExactlyOneLastChange()
    {
        var session = TestFixtures.SessionForGrading();
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);

        session.DisableGrade(slotB.Grade);

        var lastChangeFires = 0;
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GradingSession.LastChange)) lastChangeFires++;
        };

        session.EnableGrade(slotB.Grade);

        Assert.Equal(1, lastChangeFires);
    }

    [Fact]
    public void DisableGrade_OnStructuralCatchAll_ThrowsArgumentException()
    {
        // F is the catch-all per ADR-0011 (lowest-Order in DefaultCurve).
        var session = TestFixtures.SessionForGrading();

        Assert.Throws<ArgumentException>(() =>
            session.DisableGrade(TestFixtures.GradeF));
    }

    [Fact]
    public void EnableGrade_OnStructuralCatchAll_ThrowsArgumentException()
    {
        // F is the catch-all per ADR-0011 (lowest-Order in DefaultCurve).
        var session = TestFixtures.SessionForGrading();

        Assert.Throws<ArgumentException>(() =>
            session.EnableGrade(TestFixtures.GradeF));
    }

    [Fact]
    public void LoadCutoffs_HydratesSlotScoresAndEnabledGrades_EmitsOneLastChangeWithNullOriginator()
    {
        var session = TestFixtures.SessionForGrading();
        var saved = new List<GradeCutoff>
        {
            new(TestFixtures.GradeA, 420),
            new(TestFixtures.GradeB, 220),
            new(TestFixtures.GradeC, 60),
        };
        var enabledGrades = new HashSet<Grade>
        {
            TestFixtures.GradeA, TestFixtures.GradeB, TestFixtures.GradeC,
        };

        var lastChangeFires = 0;
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GradingSession.LastChange)) lastChangeFires++;
        };

        session.LoadCutoffs(saved, enabledGrades);

        Assert.Equal(1, lastChangeFires);
        Assert.Null(session.LastChange.Originator);
        Assert.Equal(420, session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A).Score);
        Assert.Equal(220, session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B).Score);
        Assert.Contains(TestFixtures.GradeC, session.CurrentState.EnabledGrades);
    }

    [Fact]
    public void LoadCutoffs_WithSubsetEnabledGrades_DisablesMissingSlots()
    {
        var session = TestFixtures.SessionForGrading();
        var saved = new List<GradeCutoff>
        {
            new(TestFixtures.GradeA, 420),
            new(TestFixtures.GradeB, 220),
            new(TestFixtures.GradeC, 60),
        };
        var enabledGrades = new HashSet<Grade>
        {
            TestFixtures.GradeA, TestFixtures.GradeC,  // B disabled
        };

        session.LoadCutoffs(saved, enabledGrades);

        Assert.False(session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B).IsEnabled);
        Assert.True(session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A).IsEnabled);
    }

    [Fact]
    public void ReseedFromDefaults_RestoresInitialCutoffsViaInitialCutoffCalculator()
    {
        var session = TestFixtures.SessionForGrading();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);

        // Move A and B around so the state is materially different from initial.
        session.MoveCutoff(slotA.Grade, 400, new object());
        session.MoveCutoff(slotB.Grade, 200, new object());

        session.ReseedFromDefaults();

        // Initial cutoffs from the fixture: A=450, B=250 (C=50 catch-all).
        Assert.Equal(450, slotA.Score);
        Assert.Equal(250, slotB.Score);
    }

    [Fact]
    public void AssignedGradeFor_ReturnsGradeAccordingToCurrentCutoffs()
    {
        // Fixture initial cutoffs: A=450, B=250, C=50 (catch-all).
        // Student id 50 has AggregateGrade = 540 → above A → assigned A.
        // Student id 1 has AggregateGrade = 50 → at the catch-all boundary → assigned C.
        var session = TestFixtures.SessionForGrading();

        var topStudent = session.AssignedGradeFor(studentId: 50);
        var bottomStudent = session.AssignedGradeFor(studentId: 1);

        Assert.Equal(LetterGrade.A, topStudent.LetterGrade);
        Assert.Equal(LetterGrade.C, bottomStudent.LetterGrade);
    }

    [Fact]
    public void Slots_AreFixedLengthAcrossEnableDisableCycles()
    {
        var session = TestFixtures.SessionForGrading();
        var initialCount = session.Slots.Count;
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);

        session.DisableGrade(slotB.Grade);
        Assert.Equal(initialCount, session.Slots.Count);

        session.EnableGrade(slotB.Grade);
        Assert.Equal(initialCount, session.Slots.Count);
    }

    [Fact]
    public void Slots_IncludeAllGradesFromDefaultCurveExceptStructuralCatchAll()
    {
        // Diagnostic for issue #17 follow-on: per ADR-0008/0011 the
        // structural catch-all is the lowest-Order grade in the
        // *DefaultCurve* — not the lowest in initial cutoffs. Every
        // other grade in DefaultCurve gets a Slot, including those with
        // zero-bound CutoffCountRange, so they can be enabled and
        // dragged later (real grading flows: instructor enables a
        // CMinus row that started as untargeted, expects to drag it).
        //
        // Fixture DefaultCurve: A(10,10), B(20,20), C(20,20), F(0,0).
        // Lowest Order in DefaultCurve = F → F is catch-all.
        // Expected Slots: [A, B, C] (3 entries). C, despite being the
        // lowest *targeted* grade, is still a draggable slot.
        var session = TestFixtures.SessionForGrading();

        Assert.Equal(3, session.Slots.Count);
        var slotGrades = session.Slots.Select(s => s.Grade).ToHashSet();
        Assert.Contains(TestFixtures.GradeA, slotGrades);
        Assert.Contains(TestFixtures.GradeB, slotGrades);
        Assert.Contains(TestFixtures.GradeC, slotGrades);
        Assert.DoesNotContain(TestFixtures.GradeF, slotGrades);
    }

    [Fact]
    public void StructuralCatchAll_IsLowestOrderGradeInDefaultCurve_NotInitialCutoffs()
    {
        // The catch-all is the grade in EnabledGrades that has no Slot.
        // In the fixture, that should be F — not C (which is currently
        // the buggy choice because slice 1 derived catch-all from
        // initial cutoffs after the zero-range filter dropped F).
        var session = TestFixtures.SessionForGrading();
        var slotGrades = session.Slots.Select(s => s.Grade).ToHashSet();
        var catchAllGrade = session.CurrentState.EnabledGrades
            .Single(g => !slotGrades.Contains(g));

        Assert.Equal(LetterGrade.F, catchAllGrade.LetterGrade);
    }

    [Fact]
    public void MoveCutoff_OnLowestTargetedGradeWhenItIsNotCatchAll_IsAccepted()
    {
        // C is the lowest *targeted* grade in the fixture (lowest with
        // non-zero range) but the structural catch-all is F. So C must
        // be a draggable slot. MoveCutoff on it should succeed.
        var session = TestFixtures.SessionForGrading();
        var slotC = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.C);
        var validScore = slotC.Score + 5;

        var result = session.MoveCutoff(slotC.Grade, validScore, new object());

        Assert.True(result.Success);
        Assert.Equal(validScore, slotC.Score);
    }

    [Fact]
    public void CurrentState_FiresPropertyChangedAlongsideLastChange()
    {
        var session = TestFixtures.SessionForGrading();
        var lastChangeFires = 0;
        var currentStateFires = 0;
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GradingSession.LastChange)) lastChangeFires++;
            if (e.PropertyName == nameof(GradingSession.CurrentState)) currentStateFires++;
        };

        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        session.MoveCutoff(slotB.Grade, slotB.Score + 5, new object());

        Assert.Equal(1, lastChangeFires);
        Assert.Equal(1, currentStateFires);
    }
}
