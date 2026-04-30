namespace Dotsesses.Tests.ViewModels;

using System.ComponentModel;
using Dotsesses.Models;
using Dotsesses.UI;

public class ScoreSelectionRowViewModelTests
{
    private static ScoreSelection MakeSelection(
        string name = "Q#",
        int? index = 1,
        bool display = true,
        bool aggregate = true,
        bool correlation = true) =>
        new(name, index, display, aggregate, correlation);

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
}
