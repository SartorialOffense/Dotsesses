namespace Dotsesses.UI;

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Dotsesses.Models;

/// <summary>
/// Owns the Compliance grid (one row per Grade in the DefaultCurve)
/// and stays in sync with a <see cref="GradingSession"/>: target ranges
/// are read once from <see cref="ClassAssessment.DefaultCurve"/>, while
/// counts and enable state propagate from every
/// <see cref="GradingSession.LastChange"/>.
///
/// Toggling <see cref="ComplianceRowViewModel.IsEnabled"/> on a slot row
/// dispatches to <see cref="GradingSession.EnableGrade"/> /
/// <see cref="GradingSession.DisableGrade"/>. The structural catch-all
/// row (ADR-0011) has no slot and is always enabled, so its toggle is
/// inert — the next session sync flips it back to <c>true</c>.
/// </summary>
public sealed class ComplianceGridViewModel : ObservableObject
{
    private readonly GradingSession _session;
    private readonly HashSet<Grade> _slotGrades;
    private readonly ObservableCollection<ComplianceRowViewModel> _rows = new();
    private bool _isSyncingFromSession;

    public ReadOnlyObservableCollection<ComplianceRowViewModel> Rows { get; }

    public ComplianceGridViewModel(ClassAssessment classAssessment, GradingSession session)
    {
        ArgumentNullException.ThrowIfNull(classAssessment);
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _slotGrades = session.Slots.Select(s => s.Grade).ToHashSet();
        Rows = new ReadOnlyObservableCollection<ComplianceRowViewModel>(_rows);

        var state = session.CurrentState;
        foreach (var range in classAssessment.DefaultCurve.OrderBy(r => r.Grade.Order))
        {
            var grade = range.Grade;
            var count = state.Counts.FirstOrDefault(c => c.Grade.Equals(grade))?.Count ?? 0;
            var isEnabled = state.EnabledGrades.Contains(grade);

            _rows.Add(new ComplianceRowViewModel(
                grade,
                range.LowerBound,
                range.UpperBound,
                count,
                isEnabled,
                onEnabledChanged: () => OnRowToggled(grade)));
        }

        session.PropertyChanged += OnSessionPropertyChanged;
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GradingSession.LastChange)) return;
        SyncFromSession();
    }

    private void SyncFromSession()
    {
        _isSyncingFromSession = true;
        try
        {
            var state = _session.CurrentState;
            foreach (var row in _rows)
            {
                row.IsEnabled = state.EnabledGrades.Contains(row.Grade);
                var count = state.Counts.FirstOrDefault(c => c.Grade.Equals(row.Grade))?.Count ?? 0;
                row.CurrentCount = count;
            }
        }
        finally
        {
            _isSyncingFromSession = false;
        }
    }

    private void OnRowToggled(Grade grade)
    {
        if (_isSyncingFromSession) return;
        if (!_slotGrades.Contains(grade)) return; // catch-all row is inert

        var row = _rows.First(r => r.Grade.Equals(grade));
        var slot = _session.Slots.First(s => s.Grade.Equals(grade));
        if (row.IsEnabled && !slot.IsEnabled)
        {
            _session.EnableGrade(grade, this);
        }
        else if (!row.IsEnabled && slot.IsEnabled)
        {
            _session.DisableGrade(grade, this);
        }
    }
}
