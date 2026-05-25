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
/// Apply/Dismiss commit semantics against an injected callback.
///
/// Draft isolation: rows hold mutable copies of their source records (per T01 row VM)
/// and the input list is never mutated. Commit happens only when <see cref="ApplyCommand"/>
/// fires — the callback receives a freshly-constructed <see cref="ScoreSelection"/> list
/// in the same order as the input. <see cref="DismissCommand"/> discards the draft
/// (does not invoke the callback).
///
/// Two-button surface (R009 / M002 S01): the dialog renders Apply + a single dismiss
/// button whose label flips between "Cancel" (when <see cref="IsDirty"/> is true) and
/// "Close" (clean). <see cref="DismissButtonLabel"/> exposes that label for binding.
///
/// Per the planner / research, dialog dismissal lives in the View code-behind
/// (mirroring CommentEditorWindow.OnOkClick); these commands only emit the
/// commit-or-discard intent.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly Action<IReadOnlyList<ScoreSelection>> _onApply;
    private readonly Func<string, int?, bool> _canSwitchToNumeric;
    private bool _isDirty;

    /// <summary>
    /// One row VM per input ScoreSelection, in input order.
    /// </summary>
    public IReadOnlyList<ScoreSelectionRowViewModel> Rows { get; }

    /// <summary>
    /// True once any row VM has raised <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
    /// since construction or the last successful Apply. Drives <see cref="DismissButtonLabel"/>.
    /// External callers cannot mutate this directly; it flips via row subscriptions and resets in
    /// <see cref="ExecuteApply"/>.
    ///
    /// Note on the last-Aggregate guard (research §G1): a rejected Aggregate clear does NOT flip
    /// <c>IsDirty</c> because <see cref="ScoreSelectionRowViewModel"/>'s setter returns early before
    /// invoking <c>SetProperty</c>, so no PropertyChanged event fires. That is the correct contract
    /// — the user's intent was rejected, no draft change occurred — and it is exercised by
    /// <c>RejectedAggregateClear_DoesNotFlipIsDirty</c>.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(DismissButtonLabel));
            }
        }
    }

    /// <summary>
    /// "Cancel" while the draft has unapplied changes; "Close" otherwise. Bound to the
    /// surviving dismiss button's <c>Content</c> by the View.
    /// </summary>
    public string DismissButtonLabel => IsDirty ? "Cancel" : "Close";

    public IRelayCommand ApplyCommand { get; }

    /// <summary>
    /// Discards any draft changes. The actual <c>Window.Close()</c> call lives in the
    /// View's click handler; this command is a no-op intent emitter so the contract
    /// stays symmetric with <see cref="ApplyCommand"/>.
    /// </summary>
    public IRelayCommand DismissCommand { get; }

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
        Action<IReadOnlyList<ScoreSelection>> onApply,
        Func<string, int?, bool>? canSwitchToNumeric = null)
    {
        ArgumentNullException.ThrowIfNull(initialSelections);
        ArgumentNullException.ThrowIfNull(onApply);

        _onApply = onApply;
        // Default-allow when no validator is supplied keeps the pre-slice-2 constructor
        // shape working for tests; production callers (MainWindow.axaml.cs) inject a real
        // predicate that walks ClassAssessment.Assessments[*].Attributes.
        _canSwitchToNumeric = canSwitchToNumeric ?? ((_, _) => true);

        // Build rows in input order. The cross-row last-Aggregate guard is satisfied
        // by closing over Rows here; per T01, a row is allowed to clear only when the
        // closure returns true (i.e. more than one Aggregate row is currently enabled).
        var rows = new List<ScoreSelectionRowViewModel>(initialSelections.Count);
        Rows = rows;
        foreach (var sel in initialSelections)
        {
            var selCaptured = sel;
            rows.Add(new ScoreSelectionRowViewModel(
                sel,
                canClearAggregate: () => Rows.Count(r => r.Aggregate && r.IsNumeric) > 1,
                canSwitchToNumeric: () => _canSwitchToNumeric(selCaptured.Name, selCaptured.Index)));
        }

        // Subscribe to each row's PropertyChanged. Row VMs only raise events for
        // settable flags (Display/Aggregate/Correlation); Name/Index are immutable
        // and guard-rejected writes return early before SetProperty fires, so any
        // event we observe here is a real draft change and must flip IsDirty.
        // Subscriptions live for the dialog's lifetime — both VM and rows die together.
        foreach (var row in Rows)
        {
            row.PropertyChanged += (_, _) => IsDirty = true;
        }

        ApplyCommand = new RelayCommand(ExecuteApply);
        DismissCommand = new RelayCommand(ExecuteDismiss);

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
            .Select(r => new ScoreSelection(r.Name, r.Index, r.Type, r.Display, r.Aggregate, r.Correlation))
            .ToList();
        _onApply(snapshot);

        // The draft is now committed; the dismiss button reverts to "Close" until
        // the user makes another edit.
        IsDirty = false;
    }

    private void ExecuteDismiss()
    {
        // Dismiss discards the draft. Dialog dismissal is the View's concern.
    }

    private void SetAllDisplay(bool value)
    {
        // Bulk-toggle Display only on Numeric rows. Categorical rows' Display/Aggregate/
        // Correlation flags are meaningless (they bypass the plots entirely); the Settings
        // UI greys them out, so honoring "All / None" on them would silently flip values
        // the user can't see.
        foreach (var row in Rows)
        {
            if (!row.IsNumeric) continue;
            row.Display = value;
        }
    }

    private void SetAllCorrelation(bool value)
    {
        foreach (var row in Rows)
        {
            if (!row.IsNumeric) continue;
            row.Correlation = value;
        }
    }

    private void SetAllAggregate()
    {
        // Iterate non-locked Numeric rows explicitly. The row VM setter naturally rejects
        // writes to locked rows, but skipping them here keeps intent clear.
        foreach (var row in Rows)
        {
            if (row.IsAggregateLocked) continue;
            if (!row.IsNumeric) continue;
            row.Aggregate = true;
        }
    }
}
