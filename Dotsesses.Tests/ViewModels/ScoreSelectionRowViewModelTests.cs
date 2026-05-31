namespace Dotsesses.Tests.ViewModels;

using System.ComponentModel;
using Dotsesses.Models;
using Dotsesses.UI;

public class ScoreSelectionRowViewModelTests
{
    private static ScoreSelection MakeSelection(
        string name = "Q#",
        int? index = 1,
        ScoreColumnType type = ScoreColumnType.Numeric,
        bool display = true,
        bool aggregate = true,
        bool correlation = true) =>
        new(name, index, type, display, aggregate, correlation);

    [Fact]
    public void DisplayName_NoIndex_ReturnsName()
    {
        // Arrange
        var selection = MakeSelection("Total", null);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        // Act / Assert
        Assert.Equal("Total", vm.DisplayName);
    }

    [Fact]
    public void DisplayName_WithIndex_ReturnsNameSpaceIndex()
    {
        // Arrange
        var selection = MakeSelection("Q#", 2);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        // Act / Assert
        Assert.Equal("Q# 2", vm.DisplayName);
    }

    [Theory]
    [InlineData("Total")]
    [InlineData("total")]
    [InlineData("TOTAL")]
    [InlineData("ToTaL")]
    public void IsAggregateLocked_TotalRow_True(string totalName)
    {
        // Arrange
        var selection = MakeSelection(totalName, null, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        // Act / Assert
        Assert.True(vm.IsAggregateLocked);
    }

    [Theory]
    [InlineData("Q#")]
    [InlineData("Essay")]
    [InlineData("Subtotal")]
    [InlineData("")]
    public void IsAggregateLocked_NonTotal_False(string name)
    {
        // Arrange
        var selection = MakeSelection(name, null);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        // Act / Assert
        Assert.False(vm.IsAggregateLocked);
    }

    [Fact]
    public void Aggregate_LockedRow_SetterRejectsTrue()
    {
        // Arrange — Total row starts at false; setter should reject any write.
        var selection = MakeSelection("Total", null, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        // Act
        vm.Aggregate = true;

        // Assert
        Assert.False(vm.Aggregate);
        Assert.DoesNotContain(nameof(ScoreSelectionRowViewModel.Aggregate), changes);
    }

    [Fact]
    public void Aggregate_NotLastEnabled_SetterAllowsClear()
    {
        // Arrange — guard returns true (more than one row still enabled).
        var selection = MakeSelection("Q#", 1, aggregate: true);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        // Act
        vm.Aggregate = false;

        // Assert
        Assert.False(vm.Aggregate);
        Assert.Contains(nameof(ScoreSelectionRowViewModel.Aggregate), changes);
    }

    [Fact]
    public void Aggregate_LastEnabled_SetterRejectsClear()
    {
        // Arrange — guard returns false (this row is the last enabled Aggregate).
        var selection = MakeSelection("Q#", 1, aggregate: true);
        var vm = new ScoreSelectionRowViewModel(selection, () => false);
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        // Act
        vm.Aggregate = false;

        // Assert
        Assert.True(vm.Aggregate);
        Assert.DoesNotContain(nameof(ScoreSelectionRowViewModel.Aggregate), changes);
    }

    [Fact]
    public void Aggregate_SetTrue_AllowedRegardlessOfGuard()
    {
        // Arrange — guard says we can't clear, but setting true is unaffected.
        var selection = MakeSelection("Q#", 1, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => false);

        // Act
        vm.Aggregate = true;

        // Assert
        Assert.True(vm.Aggregate);
    }

    [Fact]
    public void Display_AnyValue_AllowedFreely()
    {
        // Arrange
        var selection = MakeSelection("Q#", 1, display: true);
        var vm = new ScoreSelectionRowViewModel(selection, () => false);

        // Act
        vm.Display = false;
        var clearedToFalse = vm.Display;
        vm.Display = true;
        var setToTrue = vm.Display;

        // Assert — Display is unaffected by the Aggregate guard, even all-off.
        Assert.False(clearedToFalse);
        Assert.True(setToTrue);
    }

    [Fact]
    public void Correlation_AnyValue_AllowedFreely()
    {
        // Arrange
        var selection = MakeSelection("Q#", 1, correlation: true);
        var vm = new ScoreSelectionRowViewModel(selection, () => false);

        // Act
        vm.Correlation = false;
        var clearedToFalse = vm.Correlation;
        vm.Correlation = true;
        var setToTrue = vm.Correlation;

        // Assert — Correlation is unaffected by the Aggregate guard, even all-off.
        Assert.False(clearedToFalse);
        Assert.True(setToTrue);
    }

    [Fact]
    public void Constructor_DoesNotMutateSourceRecord()
    {
        // Arrange
        var selection = MakeSelection("Q#", 1, display: true, aggregate: true, correlation: true);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        // Act — toggle every flag on the VM.
        vm.Display = false;
        vm.Aggregate = false;
        vm.Correlation = false;

        // Assert — the original record is unchanged.
        Assert.Equal("Q#", selection.Name);
        Assert.Equal(1, selection.Index);
        Assert.True(selection.Display);
        Assert.True(selection.Aggregate);
        Assert.True(selection.Correlation);
    }

    // ===== Slice 2 — Type guards =====

    [Fact]
    public void Type_SetToCategorical_FlipsIsNumericFalse()
    {
        var selection = MakeSelection("Mid-Term", null, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);
        Assert.True(vm.IsNumeric);

        vm.Type = ScoreColumnType.Categorical;

        Assert.Equal(ScoreColumnType.Categorical, vm.Type);
        Assert.False(vm.IsNumeric);
        Assert.False(vm.IsAggregateEditable);
    }

    [Fact]
    public void Type_OnTotalRow_AlwaysRejected()
    {
        // G2-type: Total stays Numeric — it's either the spreadsheet's pre-summed
        // Total or our synthesized aggregate mirror, both numeric by definition.
        var selection = MakeSelection("Total", null, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        vm.Type = ScoreColumnType.Categorical;

        Assert.Equal(ScoreColumnType.Numeric, vm.Type);
    }

    [Fact]
    public void Type_NumericToCategorical_WhenLastAggregate_Rejected()
    {
        // G3: converting an Aggregate=true Numeric row would empty the aggregate set
        // if it's the last Aggregate row. Reject silently.
        var selection = MakeSelection("Q#", 1, aggregate: true);
        var vm = new ScoreSelectionRowViewModel(selection, canClearAggregate: () => false);
        var fires = 0;
        vm.PropertyChanged += (_, _) => fires++;

        vm.Type = ScoreColumnType.Categorical;

        Assert.Equal(ScoreColumnType.Numeric, vm.Type);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void Type_NumericToCategorical_WhenAggregateFalse_AcceptedEvenIfLast()
    {
        // G3 is keyed to Aggregate=true. A non-aggregate Numeric row flipping to
        // Categorical never empties the aggregate set, so the canClearAggregate
        // closure is irrelevant — accept regardless.
        var selection = MakeSelection("Notes", null, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, canClearAggregate: () => false);

        vm.Type = ScoreColumnType.Categorical;

        Assert.Equal(ScoreColumnType.Categorical, vm.Type);
    }

    [Fact]
    public void Type_CategoricalToNumeric_WhenAllParse_Accepted()
    {
        var selection = MakeSelection("Numeric strings", null, type: ScoreColumnType.Categorical, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(
            selection,
            canClearAggregate: () => true,
            canSwitchToNumeric: () => true);

        vm.Type = ScoreColumnType.Numeric;

        Assert.Equal(ScoreColumnType.Numeric, vm.Type);
    }

    [Fact]
    public void Type_CategoricalToNumeric_WhenSomeValuesDoNotParse_RejectedSilently()
    {
        // G4: validator says no parse → silent rejection, no PropertyChanged fires
        // (mirrors RejectedAggregateClear_DoesNotFlipIsDirty contract).
        var selection = MakeSelection("Mid-Term", null, type: ScoreColumnType.Categorical, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(
            selection,
            canClearAggregate: () => true,
            canSwitchToNumeric: () => false);
        var fires = 0;
        vm.PropertyChanged += (_, _) => fires++;

        vm.Type = ScoreColumnType.Numeric;

        Assert.Equal(ScoreColumnType.Categorical, vm.Type);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void ToScoreSelection_RoundTripsType()
    {
        var selection = MakeSelection("Mid-Term", null, type: ScoreColumnType.Categorical, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        var snapshot = vm.ToScoreSelection();

        Assert.Equal(ScoreColumnType.Categorical, snapshot.Type);
    }

    // ===== Slice 3 — Significance flag =====

    [Fact]
    public void Significance_DefaultsTrue_FromMakeSelection()
    {
        // MakeSelection uses ScoreSelection's default Significance=true (positional default).
        var selection = MakeSelection("Q", 1);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        Assert.True(vm.Significance);
    }

    [Fact]
    public void Significance_TogglesAndRoundTrips()
    {
        var selection = MakeSelection("Hat", null);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        vm.Significance = false;
        var snapshot = vm.ToScoreSelection();

        Assert.False(snapshot.Significance);
    }

    // ===== Ordinal type is auto-detected, never user-editable (ADR-0017) =====

    [Fact]
    public void Type_OrdinalRow_IsTypeLocked()
    {
        var selection = MakeSelection("Mid-Term", null, type: ScoreColumnType.Ordinal, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        Assert.True(vm.IsTypeLocked);
    }

    [Fact]
    public void Type_OrdinalRow_CannotBeRetyped()
    {
        var selection = MakeSelection("Mid-Term", null, type: ScoreColumnType.Ordinal, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);

        vm.Type = ScoreColumnType.Categorical;
        Assert.Equal(ScoreColumnType.Ordinal, vm.Type);

        vm.Type = ScoreColumnType.Numeric;
        Assert.Equal(ScoreColumnType.Ordinal, vm.Type);
    }

    [Fact]
    public void Type_ConvertingIntoOrdinal_Rejected()
    {
        // Ordinal is never a user-selectable target.
        var selection = MakeSelection("Mid-Term", null, type: ScoreColumnType.Categorical, aggregate: false);
        var vm = new ScoreSelectionRowViewModel(selection, () => true);
        var fires = 0;
        vm.PropertyChanged += (_, _) => fires++;

        vm.Type = ScoreColumnType.Ordinal;

        Assert.Equal(ScoreColumnType.Categorical, vm.Type);
        Assert.Equal(0, fires);
    }
}
