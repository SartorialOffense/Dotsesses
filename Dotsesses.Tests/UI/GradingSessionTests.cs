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
        // Arrange — fixture: AggregateGrades 50..540 → max + 12 = 552
        var session = TestFixtures.SessionForGrading();
        var slotA = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A);
        var stateBefore = session.LastChange.State;

        // Act — move A to 600 (well outside the upper bound)
        var result = session.MoveCutoff(slotA.Grade, 600, new object());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(CutoffMoveFailure.OutOfRange, result.Failure);
        Assert.Same(stateBefore, session.LastChange.State);
    }

    [Fact]
    public void MoveCutoff_BelowMinAggregateMinusMargin_FailsWithOutOfRange()
    {
        // Arrange — fixture: AggregateGrades 50..540 → min − 12 = 38
        var session = TestFixtures.SessionForGrading();
        var slotB = session.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B);
        var stateBefore = session.LastChange.State;

        // Act — move B to 0 (below the lower bound)
        var result = session.MoveCutoff(slotB.Grade, 0, new object());

        // Assert
        Assert.False(result.Success);
        Assert.Equal(CutoffMoveFailure.OutOfRange, result.Failure);
        Assert.Same(stateBefore, session.LastChange.State);
    }

    [Fact]
    public void MoveCutoff_OnStructuralCatchAll_ThrowsArgumentException()
    {
        // Arrange — in this fixture C is the structural catch-all (lowest Order in initial cutoffs)
        var session = TestFixtures.SessionForGrading();

        // Act & Assert — caller is asking for something the API doesn't support
        Assert.Throws<ArgumentException>(() =>
            session.MoveCutoff(TestFixtures.GradeC, 100, new object()));
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
        var session = TestFixtures.SessionForGrading();

        Assert.Throws<ArgumentException>(() =>
            session.DisableGrade(TestFixtures.GradeC));
    }

    [Fact]
    public void EnableGrade_OnStructuralCatchAll_ThrowsArgumentException()
    {
        var session = TestFixtures.SessionForGrading();

        Assert.Throws<ArgumentException>(() =>
            session.EnableGrade(TestFixtures.GradeC));
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
