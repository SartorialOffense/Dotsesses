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

    // Margin around the AggregateGrade envelope so cursors can sit
    // just outside the data extremes (matches DefaultCursorSpacing
    // in InitialCutoffCalculator). See Q2 in
    // .conversations/2026-05-07_issue-7-grading-session-design.md.
    private const int ScoreBoundsMargin = 12;

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

        _minScore = classAssessment.Assessments.Min(a => a.AggregateGrade) - ScoreBoundsMargin;
        _maxScore = classAssessment.Assessments.Max(a => a.AggregateGrade) + ScoreBoundsMargin;

        var midpointCurve = classAssessment.DefaultCurve
            .Where(r => r.LowerBound > 0 || r.UpperBound > 0)
            .Select(r => new CutoffCount(r.Grade, r.Midpoint))
            .OrderBy(c => c.Grade.Order)
            .ToList();

        if (midpointCurve.Count == 0)
        {
            throw new InvalidOperationException(
                "DefaultCurve has no targeted grades (all ranges are zero); " +
                "GradingSession requires at least one targeted grade.");
        }

        var initialCutoffs = _initialCutoffCalculator
            .Calculate(classAssessment.Assessments, midpointCurve)
            .OrderBy(c => c.Grade.Order)
            .ToList();

        // The lowest-Order grade in the initial cutoffs is the structural
        // catch-all (Q1=C): it has no Slot and is always implicitly
        // enabled. Per ADR-0008, this designation is fixed at construction.
        _structuralCatchAll = initialCutoffs[^1].Grade;
        _catchAllScore = initialCutoffs[^1].Score;

        _slots = new ObservableCollection<CutoffSlot>(
            initialCutoffs
                .Where(c => !c.Grade.Equals(_structuralCatchAll))
                .Select(c => new CutoffSlot(c.Grade, c.Score, isEnabled: true)));

        Slots = new ReadOnlyObservableCollection<CutoffSlot>(_slots);

        var enabledGrades = initialCutoffs
            .Select(c => c.Grade)
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

        if (newScore < _minScore || newScore > _maxScore)
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
