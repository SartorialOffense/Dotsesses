namespace Dotsesses.UI;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dotsesses.Calculators;
using Dotsesses.Models;

/// <summary>
/// Owns one Class's live grading state and broadcasts every accepted
/// change as a single GradingStateChange notification. See ADR-0008
/// (structural immutability) and ADR-0010 (state sync via observable
/// property with originator tagging).
/// </summary>
public sealed class GradingSession : ObservableObject
{
    private readonly ClassAssessment _classAssessment;
    private readonly CursorPlacementCalculator _cursorPlacement;
    private readonly CursorValidation _cursorValidation;
    private readonly CutoffCountCalculator _cutoffCountCalculator;
    private readonly InitialCutoffCalculator _initialCutoffCalculator;
    private readonly Grade _structuralCatchAll;
    private readonly ObservableCollection<CutoffSlot> _slots;
    private readonly int _minScore;
    private readonly int _maxScore;
    private int _catchAllScore;
    private GradingStateChange _lastChange = null!;

    // Asymmetric drag bounds: only the upper side has an `OutOfRange`
    // limit (max + 5, so A can be pushed past the top scorer to make A
    // unattainable). The lower side has no `OutOfRange` floor — issue
    // #18 surfaced that untargeted slot grades start in a fallback band
    // below `minStudentScore - ScoreBoundsMarginBelow`, so a hard lower
    // bound traps them out-of-reach. The catch-all (or whichever worse
    // neighbor is enabled) is now the only floor on cursor drag, via
    // the existing `OrderingViolation` / `WouldOverlap` checks.
    //
    // `_minScore` is still computed for `CursorPlacementCalculator.PlaceNewCursor`
    // (used by `EnableGrade` to seat re-enabled grades) but no longer
    // affects `ValidateMove`. Slice 3 of #6 used [-1, +5]; issue #17
    // widened to [-8, +5]; issue #18 dropped the lower bound entirely.
    private const int ScoreBoundsMarginBelow = 8;
    private const int ScoreBoundsMarginAbove = 5;

    public ReadOnlyObservableCollection<CutoffSlot> Slots { get; }

    public GradingStateChange LastChange
    {
        get => _lastChange;
        private set
        {
            if (SetProperty(ref _lastChange, value))
            {
                OnPropertyChanged(nameof(CurrentState));
            }
        }
    }

    public GradingState CurrentState => LastChange.State;

    public GradingSession(
        ClassAssessment classAssessment,
        CursorPlacementCalculator cursorPlacement,
        CursorValidation cursorValidation,
        CutoffCountCalculator cutoffCountCalculator,
        InitialCutoffCalculator initialCutoffCalculator)
    {
        ArgumentNullException.ThrowIfNull(classAssessment);
        ArgumentNullException.ThrowIfNull(cursorPlacement);
        ArgumentNullException.ThrowIfNull(cursorValidation);
        ArgumentNullException.ThrowIfNull(cutoffCountCalculator);
        ArgumentNullException.ThrowIfNull(initialCutoffCalculator);

        _classAssessment = classAssessment;
        _cursorPlacement = cursorPlacement;
        _cursorValidation = cursorValidation;
        _cutoffCountCalculator = cutoffCountCalculator;
        _initialCutoffCalculator = initialCutoffCalculator;

        if (classAssessment.Assessments.Count == 0)
        {
            throw new InvalidOperationException(
                "ClassAssessment has no StudentAssessments; GradingSession " +
                "cannot derive score bounds from an empty class.");
        }

        _minScore = classAssessment.Assessments.Min(a => a.AggregateGrade) - ScoreBoundsMarginBelow;
        _maxScore = classAssessment.Assessments.Max(a => a.AggregateGrade) + ScoreBoundsMarginAbove;

        // Catch-all is the lowest-Order grade in DefaultCurve, regardless
        // of targeting (ADR-0011). Issue #18 corrected this from slice 1's
        // mistaken "lowest-Order in initial cutoffs" — the previous logic
        // dropped zero-range grades before picking the catch-all, leaving
        // production grades like CMinus, DPlus, D, F structurally absent
        // from `Slots` and undraggable.
        var allCurveGrades = classAssessment.DefaultCurve
            .Select(r => r.Grade)
            .OrderBy(g => g.Order)
            .ToList();

        if (allCurveGrades.Count < 2)
        {
            throw new InvalidOperationException(
                "DefaultCurve must contain at least two grades " +
                "(one structural catch-all plus at least one slot).");
        }

        _structuralCatchAll = allCurveGrades[^1];
        var slotGrades = allCurveGrades
            .Where(g => !g.Equals(_structuralCatchAll))
            .ToList();

        // Targeted slots get positions from InitialCutoffCalculator
        // (curves with non-zero range, excluding the catch-all).
        var midpointCurve = classAssessment.DefaultCurve
            .Where(r => r.LowerBound > 0 || r.UpperBound > 0)
            .Where(r => !r.Grade.Equals(_structuralCatchAll))
            .Select(r => new CutoffCount(r.Grade, r.Midpoint))
            .OrderBy(c => c.Grade.Order)
            .ToList();

        if (midpointCurve.Count == 0)
        {
            throw new InvalidOperationException(
                "DefaultCurve has no targeted slot grades (all non-catch-all " +
                "ranges are zero); GradingSession requires at least one " +
                "targeted slot grade to seed initial positions.");
        }

        var seededCutoffs = _initialCutoffCalculator
            .Calculate(classAssessment.Assessments, midpointCurve)
            .ToDictionary(c => c.Grade);

        // Untargeted slots (zero-range CutoffCountRange) get fallback
        // positions in a band below the data envelope — visible and
        // draggable but not positioned to catch students by default.
        // Mirrors the legacy SeedCursorsFromDefaults "Second pass" so
        // the legacy `_cursors` mirror sync stays consistent during
        // the slice-3 transition.
        var minStudentScore = classAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxStudentScore = classAssessment.Assessments.Max(a => a.AggregateGrade);
        var fallbackScoreRange = maxStudentScore - minStudentScore;
        var fallbackBaseScore = (int)Math.Round(minStudentScore - fallbackScoreRange * 0.25);

        var untargetedSlotGrades = slotGrades
            .Where(g => !seededCutoffs.ContainsKey(g))
            .OrderBy(g => g.Order)
            .ToList();

        var fallbackScores = new Dictionary<Grade, int>();
        for (int i = 0; i < untargetedSlotGrades.Count; i++)
        {
            // Lower-Order grades sit higher in the fallback band so the
            // overall (Order, Score) relationship stays monotonic
            // descending across all slots.
            fallbackScores[untargetedSlotGrades[i]] = fallbackBaseScore - i * DefaultCursorSpacing;
        }

        _slots = new ObservableCollection<CutoffSlot>(
            slotGrades
                .OrderBy(g => g.Order)
                .Select(g => new CutoffSlot(
                    g,
                    seededCutoffs.TryGetValue(g, out var seeded) ? seeded.Score : fallbackScores[g],
                    isEnabled: true)));

        Slots = new ReadOnlyObservableCollection<CutoffSlot>(_slots);

        // Catch-all sits below the lowest slot so it remains the
        // assignment fallback (until a user drags a slot below it,
        // which ValidateMove rejects via OrderingViolation).
        _catchAllScore = _slots.Min(s => s.Score) - DefaultCursorSpacing;

        var initialCutoffs = BuildCurrentCutoffs();
        var enabledGrades = _slots
            .Where(s => s.IsEnabled)
            .Select(s => s.Grade)
            .Append(_structuralCatchAll)
            .ToHashSet();

        var counts = _cutoffCountCalculator
            .Calculate(classAssessment.Assessments, initialCutoffs)
            .OrderBy(c => c.Grade.Order)
            .ToList();

        var initialState = new GradingState(
            Cutoffs: initialCutoffs,
            Counts: counts,
            EnabledGrades: enabledGrades);

        _lastChange = new GradingStateChange(Originator: null, State: initialState);
    }

    // Matches CursorPlacementCalculator's DefaultCursorSpacing — the
    // minimum safe gap between adjacent cutoffs that still respects
    // ordering. Used to space the fallback band for untargeted slots
    // and to position the catch-all below them.
    private const int DefaultCursorSpacing = 12;

    public CutoffMoveResult CanMoveCutoff(Grade grade, int newScore)
    {
        var slot = RequireDraggableSlot(grade);
        return ValidateMove(slot, newScore);
    }

    public CutoffMoveResult MoveCutoff(Grade grade, int newScore, object originator)
    {
        ArgumentNullException.ThrowIfNull(originator);
        var slot = RequireDraggableSlot(grade);

        var validation = ValidateMove(slot, newScore);
        if (!validation.Success) return validation;

        slot.Score = newScore;
        EmitNewState(originator);
        return CutoffMoveResult.Ok();
    }

    public void EnableGrade(Grade grade, object? originator = null)
    {
        var slot = RequireDraggableSlot(grade);
        if (slot.IsEnabled) return;

        var existingCutoffs = BuildCurrentCutoffs();
        var placed = _cursorPlacement
            .PlaceNewCursor(slot.Grade, existingCutoffs, _minScore, _maxScore);

        // PlaceNewCursor may reseed all enabled grades. Apply the result back
        // uniformly across slots and the catch-all so the caller observes a
        // single coherent state when LastChange fires below.
        foreach (var c in placed)
        {
            if (c.Grade.Equals(_structuralCatchAll))
            {
                _catchAllScore = c.Score;
            }
            else
            {
                var matching = _slots.FirstOrDefault(s => s.Grade.Equals(c.Grade));
                if (matching is not null)
                {
                    matching.Score = c.Score;
                }
            }
        }

        slot.IsEnabled = true;
        EmitNewState(originator);
    }

    public void DisableGrade(Grade grade, object? originator = null)
    {
        var slot = RequireDraggableSlot(grade);
        if (!slot.IsEnabled) return;

        slot.IsEnabled = false;
        EmitNewState(originator);
    }

    public void ReseedFromDefaults(object? originator = null)
    {
        var midpointCurve = _classAssessment.DefaultCurve
            .Where(r => r.LowerBound > 0 || r.UpperBound > 0)
            .Select(r => new CutoffCount(r.Grade, r.Midpoint))
            .OrderBy(c => c.Grade.Order)
            .ToList();

        var seeded = _initialCutoffCalculator
            .Calculate(_classAssessment.Assessments, midpointCurve)
            .ToList();

        ApplyCutoffsToInternalState(seeded, enabledGrades: null);
        EmitNewState(originator);
    }

    public void LoadCutoffs(
        IReadOnlyList<GradeCutoff> cutoffs,
        IReadOnlySet<Grade> enabledGrades,
        object? originator = null)
    {
        ArgumentNullException.ThrowIfNull(cutoffs);
        ArgumentNullException.ThrowIfNull(enabledGrades);

        ApplyCutoffsToInternalState(cutoffs, enabledGrades);
        EmitNewState(originator);
    }

    public Grade AssignedGradeFor(int studentId)
    {
        var student = _classAssessment.Assessments.FirstOrDefault(a => a.Id == studentId)
            ?? throw new ArgumentException(
                $"No StudentAssessment with id {studentId} in this class.",
                nameof(studentId));

        var assigner = new GradeAssigner(CurrentState.Cutoffs);
        return assigner.AssignGrade(student.AggregateGrade);
    }

    private void ApplyCutoffsToInternalState(
        IReadOnlyCollection<GradeCutoff> cutoffs,
        IReadOnlySet<Grade>? enabledGrades)
    {
        // If enabledGrades is null (e.g. ReseedFromDefaults), every slot
        // present in the supplied cutoffs is treated as enabled and slots
        // not in the supplied cutoffs are disabled.
        var supplied = cutoffs.ToDictionary(c => c.Grade);

        if (supplied.TryGetValue(_structuralCatchAll, out var catchAllCutoff))
        {
            _catchAllScore = catchAllCutoff.Score;
        }

        foreach (var slot in _slots)
        {
            var hasCutoff = supplied.TryGetValue(slot.Grade, out var c);
            if (hasCutoff)
            {
                slot.Score = c!.Score;
            }

            slot.IsEnabled = enabledGrades is null
                ? hasCutoff
                : enabledGrades.Contains(slot.Grade);
        }
    }

    private CutoffSlot RequireDraggableSlot(Grade grade)
    {
        ArgumentNullException.ThrowIfNull(grade);

        if (grade.Equals(_structuralCatchAll))
        {
            throw new ArgumentException(
                $"Grade {grade.DisplayName} is the structural catch-all and has no draggable cutoff.",
                nameof(grade));
        }

        return _slots.FirstOrDefault(s => s.Grade.Equals(grade))
            ?? throw new ArgumentException(
                $"Grade {grade.DisplayName} is not part of this session's slot collection.",
                nameof(grade));
    }

    private CutoffMoveResult ValidateMove(CutoffSlot slot, int newScore)
    {
        if (!slot.IsEnabled)
            return CutoffMoveResult.Fail(CutoffMoveFailure.GradeNotEnabled);

        // Only the upper side enforces OutOfRange. See the
        // `ScoreBoundsMargin*` comment for why the lower bound is open.
        if (newScore > _maxScore)
            return CutoffMoveResult.Fail(CutoffMoveFailure.OutOfRange);

        var (better, worse) = FindAdjacentEnabledCutoffs(slot.Grade);

        if (worse is not null)
        {
            if (newScore < worse.Score)
                return CutoffMoveResult.Fail(CutoffMoveFailure.OrderingViolation);
            if (newScore == worse.Score)
                return CutoffMoveResult.Fail(CutoffMoveFailure.WouldOverlap);
        }

        if (better is not null)
        {
            if (newScore > better.Score)
                return CutoffMoveResult.Fail(CutoffMoveFailure.OrderingViolation);
            if (newScore == better.Score)
                return CutoffMoveResult.Fail(CutoffMoveFailure.WouldOverlap);
        }

        return CutoffMoveResult.Ok();
    }

    private (GradeCutoff? Better, GradeCutoff? Worse) FindAdjacentEnabledCutoffs(Grade grade)
    {
        // "Better" = lower Order than `grade` (higher score expected);
        // "Worse" = higher Order (lower score expected; may be the catch-all).
        var enabledCutoffs = CurrentState.Cutoffs
            .Where(c => CurrentState.EnabledGrades.Contains(c.Grade))
            .ToList();

        var better = enabledCutoffs
            .Where(c => c.Grade.Order < grade.Order)
            .OrderByDescending(c => c.Grade.Order)
            .FirstOrDefault();

        var worse = enabledCutoffs
            .Where(c => c.Grade.Order > grade.Order)
            .OrderBy(c => c.Grade.Order)
            .FirstOrDefault();

        return (better, worse);
    }

    private List<GradeCutoff> BuildCurrentCutoffs()
    {
        // Slot scores are authoritative for non-catch-all enabled grades.
        // The catch-all's score lives on _catchAllScore; GradeAssigner uses
        // it as a fallback only (it is never draggable).
        return _slots
            .Where(s => s.IsEnabled)
            .Select(s => new GradeCutoff(s.Grade, s.Score))
            .Append(new GradeCutoff(_structuralCatchAll, _catchAllScore))
            .OrderBy(c => c.Grade.Order)
            .ToList();
    }

    private void EmitNewState(object? originator)
    {
        var newCutoffs = BuildCurrentCutoffs();
        var newCounts = _cutoffCountCalculator
            .Calculate(_classAssessment.Assessments, newCutoffs)
            .OrderBy(c => c.Grade.Order)
            .ToList();

        var enabled = _slots
            .Where(s => s.IsEnabled)
            .Select(s => s.Grade)
            .Append(_structuralCatchAll)
            .ToHashSet();

        var newState = new GradingState(newCutoffs, newCounts, enabled);
        LastChange = new GradingStateChange(originator, newState);
    }
}
