using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Dotsesses.Models;

namespace Dotsesses.UI;

/// <summary>
/// Per-row ViewModel for the Settings dialog's Score Selection table.
/// Holds mutable draft state for one score's Display/Aggregate/Correlation flags
/// and enforces two guards on the Aggregate setter:
///   (G2) Reject any write when the row is locked (Name == "Total" case-insensitive).
///   (G1) Reject a clear-to-false when the injected canClearAggregate predicate
///        returns false — used by the parent VM to keep at least one Aggregate row enabled.
/// The source <see cref="ScoreSelection"/> record is copied into private state on
/// construction; the VM never mutates the input. Commit reconstruction is the
/// parent VM's responsibility.
/// </summary>
public partial class ScoreSelectionRowViewModel : ViewModelBase
{
    private readonly Func<bool> _canClearAggregate;
    private bool _aggregate;

    public string Name { get; }

    public int? Index { get; }

    /// <summary>
    /// Display name following the canonical project pattern from
    /// MainWindowViewModel: "{Name} {Index}" when Index has a value, else just Name.
    /// </summary>
    public string DisplayName => Index.HasValue ? $"{Name} {Index}" : Name;

    /// <summary>
    /// True when this row's Aggregate cell is locked (i.e. the Excel Total row).
    /// Match is case-insensitive on Name.
    /// </summary>
    public bool IsAggregateLocked =>
        string.Equals(Name, "Total", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _display;

    [ObservableProperty]
    private bool _correlation;

    /// <summary>
    /// Aggregate flag for this score. Hand-rolled (not [ObservableProperty]) so the
    /// guards can reject the change without raising PropertyChanged.
    /// </summary>
    public bool Aggregate
    {
        get => _aggregate;
        set
        {
            // G2: locked rows can never change Aggregate (Total is forced false).
            if (IsAggregateLocked)
            {
                return;
            }

            // G1: only restrict a clear-to-false; setting true is always allowed.
            if (_aggregate && !value && !_canClearAggregate())
            {
                return;
            }

            SetProperty(ref _aggregate, value);
        }
    }

    public ScoreSelectionRowViewModel(ScoreSelection selection, Func<bool> canClearAggregate)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(canClearAggregate);

        Name = selection.Name;
        Index = selection.Index;
        _display = selection.Display;
        _aggregate = selection.Aggregate;
        _correlation = selection.Correlation;
        _canClearAggregate = canClearAggregate;
    }

    /// <summary>
    /// Snapshots current draft state back into a <see cref="ScoreSelection"/> record.
    /// Used by the parent VM at Apply time.
    /// </summary>
    public ScoreSelection ToScoreSelection() =>
        new(Name, Index, Display, Aggregate, Correlation);
}
