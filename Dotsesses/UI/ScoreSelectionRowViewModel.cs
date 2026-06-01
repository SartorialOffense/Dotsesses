using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Dotsesses.Models;

namespace Dotsesses.UI;

/// <summary>
/// Per-row ViewModel for the Settings dialog's Score Selection table.
/// Holds mutable draft state for one score's Type / Display / Aggregate / Correlation flags
/// and enforces guards on the Aggregate and Type setters:
///   (G2) Reject any Aggregate write when the row is locked (Name == "Total" case-insensitive).
///   (G1) Reject an Aggregate clear-to-false when the injected <see cref="_canClearAggregate"/>
///        predicate returns false — used by the parent VM to keep at least one Aggregate row enabled.
///   (G2-type) Reject any Type write when the row is type-locked (Total stays Numeric).
///   (G3) Reject Numeric→Categorical when the row's Aggregate=true is currently the last
///        Aggregate-flagged Numeric row (parallels G1, prevents emptying the aggregate set
///        as a side-effect of a type switch).
///   (G4) Reject Categorical→Numeric when the injected <see cref="_canSwitchToNumeric"/>
///        predicate returns false (not every stored Attribute value parses as a double).
/// All guards reject silently (no <c>SetProperty</c>, no <c>IsDirty</c> flip), matching the
/// existing <see cref="ScoreSelectionRowViewModel"/> aggregate-guard contract.
/// The source <see cref="ScoreSelection"/> record is copied into private state on
/// construction; the VM never mutates the input. Commit reconstruction is the
/// parent VM's responsibility.
/// </summary>
public partial class ScoreSelectionRowViewModel : ViewModelBase
{
    private readonly Func<bool> _canClearAggregate;
    private readonly Func<bool> _canSwitchToNumeric;
    private bool _aggregate;
    private ScoreColumnType _type;

    public string Name { get; }

    public int? Index { get; }

    /// <summary>
    /// Static option list backing the Type combobox in the Settings dialog.
    /// <see cref="ScoreColumnType.Ordinal"/> is auto-detected from the data
    /// (ADR-0017) and never user-selectable; it appears here only so an Ordinal
    /// row can render its current type, and the combobox is disabled for such a
    /// row (see <see cref="IsTypeLocked"/>).
    /// </summary>
    public IReadOnlyList<ScoreColumnType> TypeOptions { get; } =
        new[] { ScoreColumnType.Numeric, ScoreColumnType.Categorical, ScoreColumnType.Ordinal };

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

    /// <summary>
    /// True when this row's Type cell is locked. Total stays Numeric — either the user's
    /// pre-summed column or our synthesized aggregate mirror, both numeric by definition.
    /// An Ordinal row is also locked: Ordinal is auto-detected from the <c>~N</c> suffix
    /// convention (ADR-0017) and cannot be set or cleared by hand.
    /// </summary>
    public bool IsTypeLocked => IsAggregateLocked || _type == ScoreColumnType.Ordinal;

    [ObservableProperty]
    private bool _display;

    [ObservableProperty]
    private bool _correlation;

    /// <summary>
    /// Whether the column participates in the Significance Matrix plot. Unlike the other
    /// flags, meaningful for both <see cref="ScoreColumnType.Numeric"/> (the column becomes
    /// a matrix row) and <see cref="ScoreColumnType.Categorical"/> (the column becomes a
    /// matrix column). No locked-Total or last-row guard — an empty matrix is a fine
    /// degenerate state.
    /// </summary>
    [ObservableProperty]
    private bool _significance;

    /// <summary>
    /// Whether this column is rest-score de-biased against Total in the correlation
    /// matrix (ADR-0018) — correlated against <c>Total − this column</c>. The sole
    /// trigger for the correction, independent of <see cref="Aggregate"/>, so a
    /// composite column can be de-biased without being aggregated. Meaningful only
    /// for Numeric non-Total rows (gated by <see cref="IsBiasCorrectEditable"/>).
    /// </summary>
    [ObservableProperty]
    private bool _biasCorrect;

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

            if (SetProperty(ref _aggregate, value))
            {
                // Aggregate participating in the last-Aggregate count affects IsNumeric
                // permissibility on its own row only indirectly (through the closure),
                // but the cross-row guard is evaluated at the moment of a Type write,
                // so no additional notification is required here.
            }
        }
    }

    /// <summary>
    /// Column type. Hand-rolled with three guards (see class summary). Slice 2 of ADR-0013.
    /// </summary>
    public ScoreColumnType Type
    {
        get => _type;
        set
        {
            if (_type == value)
            {
                return;
            }

            // G2-type: Total stays Numeric, and an auto-detected Ordinal row
            // can't be retyped by hand (ADR-0017).
            if (IsTypeLocked)
            {
                return;
            }

            // G5-type: Ordinal is auto-detected only — never a user-selectable
            // target. Reject any attempt to convert a row *into* Ordinal.
            if (value == ScoreColumnType.Ordinal)
            {
                return;
            }

            if (_type == ScoreColumnType.Numeric && value == ScoreColumnType.Categorical)
            {
                // G3: converting a Numeric row that is currently the last Aggregate-flagged
                // Numeric row would empty the aggregate set — block it. Re-use the
                // _canClearAggregate closure: if this row's Aggregate is false, the conversion
                // doesn't change the aggregate set, so it's always allowed; if true, only
                // allow when more than one Aggregate row currently exists.
                if (_aggregate && !_canClearAggregate())
                {
                    return;
                }
            }
            else if (_type == ScoreColumnType.Categorical && value == ScoreColumnType.Numeric)
            {
                // G4: every stored Attribute value for this column must parse as a double.
                if (!_canSwitchToNumeric())
                {
                    return;
                }
            }

            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(IsNumeric));
                OnPropertyChanged(nameof(IsAggregateEditable));
                OnPropertyChanged(nameof(IsBiasCorrectEditable));
            }
        }
    }

    /// <summary>
    /// True when this row's Type is Numeric. The Settings UI gates the other three
    /// checkboxes on this — Display/Aggregate/Correlation are meaningless on Categorical
    /// rows and the checkboxes disable accordingly.
    /// </summary>
    public bool IsNumeric => _type == ScoreColumnType.Numeric;

    /// <summary>
    /// True when the Aggregate checkbox should be editable: row is Numeric AND not
    /// the locked Total row. Exposed as a single binding target so the XAML can
    /// avoid expressing the AND.
    /// </summary>
    public bool IsAggregateEditable => IsNumeric && !IsAggregateLocked;

    /// <summary>
    /// True when the Bias Correct checkbox should be editable: row is Numeric AND
    /// not the locked Total row (Total can't be de-biased against itself; the flag
    /// is meaningless for Categorical/Ordinal columns). Same condition as
    /// <see cref="IsAggregateEditable"/> but a distinct binding target.
    /// </summary>
    public bool IsBiasCorrectEditable => IsNumeric && !IsAggregateLocked;

    /// <summary>
    /// TEMPORARY (TD010): whether the Aggregate column is shown — mirrors
    /// <see cref="FeatureFlags.UseSpreadsheetTotal"/>. Exposed on the row VM so
    /// the per-row template can bind without walking up to the parent VM.
    /// </summary>
    public bool ShowAggregateColumn => !FeatureFlags.UseSpreadsheetTotal;

    /// <summary>Width of the Aggregate grid column (0 when hidden, TD010).</summary>
    public Avalonia.Controls.GridLength AggregateColumnWidth =>
        ShowAggregateColumn ? new Avalonia.Controls.GridLength(100) : new Avalonia.Controls.GridLength(0);

    public ScoreSelectionRowViewModel(
        ScoreSelection selection,
        Func<bool> canClearAggregate,
        Func<bool>? canSwitchToNumeric = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(canClearAggregate);

        Name = selection.Name;
        Index = selection.Index;
        _type = selection.Type;
        _display = selection.Display;
        _aggregate = selection.Aggregate;
        _correlation = selection.Correlation;
        _significance = selection.Significance;
        _biasCorrect = selection.BiasCorrect;
        _canClearAggregate = canClearAggregate;
        // Default-allow when no validator is supplied (e.g. row VM unit tests that don't
        // exercise the type-switch path). Production callers always pass a real closure.
        _canSwitchToNumeric = canSwitchToNumeric ?? (() => true);
    }

    /// <summary>
    /// Snapshots current draft state back into a <see cref="ScoreSelection"/> record.
    /// Used by the parent VM at Apply time.
    /// </summary>
    public ScoreSelection ToScoreSelection() =>
        new(Name, Index, Type, Display, Aggregate, Correlation, Significance, BiasCorrect);
}
