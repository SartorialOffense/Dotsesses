using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Dotsesses.Models;

namespace Dotsesses.UI;

/// <summary>
/// Parent ViewModel for the Settings dialog. Owns one
/// <see cref="ScoreSelectionRowViewModel"/> per input <see cref="ScoreSelection"/>,
/// exposes per-column All/None bulk-toggle commands, and implements
/// Apply/Cancel/Close commit semantics against an injected callback.
///
/// Draft isolation: rows hold mutable copies of their source records (per T01 row VM)
/// and the input list is never mutated. Commit happens only when <see cref="ApplyCommand"/>
/// fires — the callback receives a freshly-constructed <see cref="ScoreSelection"/> list
/// in the same order as the input. Cancel and Close discard the draft (do not invoke the callback).
///
/// Per the planner / research, dialog dismissal lives in the View code-behind
/// (mirroring CommentEditorWindow.OnOkClick); these commands only emit the
/// commit-or-discard intent.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly Action<IReadOnlyList<ScoreSelection>> _onApply;

    /// <summary>
    /// One row VM per input ScoreSelection, in input order.
    /// </summary>
    public IReadOnlyList<ScoreSelectionRowViewModel> Rows { get; }

    public IRelayCommand ApplyCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand CloseCommand { get; }

    // Per-column bulk-toggle commands.
    public IRelayCommand DisplayAllCommand { get; }

    public IRelayCommand DisplayNoneCommand { get; }

    public IRelayCommand CorrelationAllCommand { get; }

    public IRelayCommand CorrelationNoneCommand { get; }

    public IRelayCommand AggregateAllCommand { get; }

    /// <summary>
    /// Permanently disabled. Clearing every Aggregate row would leave RecalculateAggregate
    /// with nothing to sum — a structurally invalid state. The button surfaces the constraint
    /// by being disabled; the user reaches "mostly off" by clicking individual rows until the
    /// last-Aggregate guard prevents the final clear. (Research §G3, plan T02.)
    /// </summary>
    public IRelayCommand AggregateNoneCommand { get; }

    public SettingsViewModel(
        IReadOnlyList<ScoreSelection> initialSelections,
        Action<IReadOnlyList<ScoreSelection>> onApply)
    {
        ArgumentNullException.ThrowIfNull(initialSelections);
        ArgumentNullException.ThrowIfNull(onApply);

        _onApply = onApply;

        // Build rows in input order. The cross-row last-Aggregate guard is satisfied
        // by closing over Rows here; per T01, a row is allowed to clear only when the
        // closure returns true (i.e. more than one Aggregate row is currently enabled).
        var rows = new List<ScoreSelectionRowViewModel>(initialSelections.Count);
        Rows = rows;
        foreach (var sel in initialSelections)
        {
            rows.Add(new ScoreSelectionRowViewModel(
                sel,
                () => Rows.Count(r => r.Aggregate) > 1));
        }

        ApplyCommand = new RelayCommand(ExecuteApply);
        CancelCommand = new RelayCommand(ExecuteCancel);
        CloseCommand = new RelayCommand(ExecuteClose);

        DisplayAllCommand = new RelayCommand(() => SetAllDisplay(true));
        DisplayNoneCommand = new RelayCommand(() => SetAllDisplay(false));
        CorrelationAllCommand = new RelayCommand(() => SetAllCorrelation(true));
        CorrelationNoneCommand = new RelayCommand(() => SetAllCorrelation(false));
        AggregateAllCommand = new RelayCommand(SetAllAggregate);
        AggregateNoneCommand = new RelayCommand(
            execute: () => { /* no-op; permanently disabled */ },
            canExecute: () => false);
    }

    private void ExecuteApply()
    {
        // Reconstruct fresh records in row (= input) order so the callback receives
        // an independent snapshot the caller can persist or feed downstream.
        var snapshot = Rows
            .Select(r => new ScoreSelection(r.Name, r.Index, r.Display, r.Aggregate, r.Correlation))
            .ToList();
        _onApply(snapshot);
    }

    private void ExecuteCancel()
    {
        // Cancel discards the draft. Dialog dismissal is the View's concern.
    }

    private void ExecuteClose()
    {
        // Close behaves identically to Cancel — separate command so the View can
        // wire two distinct buttons without conflating intent.
    }

    private void SetAllDisplay(bool value)
    {
        foreach (var row in Rows)
        {
            row.Display = value;
        }
    }

    private void SetAllCorrelation(bool value)
    {
        foreach (var row in Rows)
        {
            row.Correlation = value;
        }
    }

    private void SetAllAggregate()
    {
        // Iterate non-locked rows explicitly. The row VM setter naturally rejects
        // writes to locked rows, but skipping them here keeps intent clear.
        foreach (var row in Rows)
        {
            if (row.IsAggregateLocked)
            {
                continue;
            }

            row.Aggregate = true;
        }
    }
}
