namespace Dotsesses.Tests.ViewModels;

using System;
using System.Collections.Generic;
using System.Linq;
using Dotsesses.Models;
using Dotsesses.UI;

public class SettingsViewModelTests
{
    private static IReadOnlyList<ScoreSelection> MakeInput()
    {
        // Three non-locked rows + one locked Total row. Two Aggregates start enabled
        // so the last-Aggregate guard does not fire on the first programmatic clear.
        return new List<ScoreSelection>
        {
            new("Q#", 1, ScoreColumnType.Numeric, Display: true,  Aggregate: true,  Correlation: true),
            new("Q#", 2, ScoreColumnType.Numeric, Display: true,  Aggregate: true,  Correlation: false),
            new("Mid", null, ScoreColumnType.Numeric, Display: false, Aggregate: false, Correlation: true),
            new("Total", null, ScoreColumnType.Numeric, Display: true, Aggregate: false, Correlation: false),
        };
    }

    private static (SettingsViewModel vm, List<IReadOnlyList<ScoreSelection>> captures)
        MakeVm(IReadOnlyList<ScoreSelection>? input = null)
    {
        var captures = new List<IReadOnlyList<ScoreSelection>>();
        Action<IReadOnlyList<ScoreSelection>> cb = list => captures.Add(list);
        var vm = new SettingsViewModel(input ?? MakeInput(), cb);
        return (vm, captures);
    }

    [Fact]
    public void Constructor_PopulatesRows_OneToOneWithInput()
    {
        // Arrange
        var input = MakeInput();

        // Act
        var (vm, _) = MakeVm(input);

        // Assert
        Assert.Equal(input.Count, vm.Rows.Count);
    }

    [Fact]
    public void Constructor_PreservesInputOrder()
    {
        // Arrange
        var input = MakeInput();

        // Act
        var (vm, _) = MakeVm(input);

        // Assert
        for (int i = 0; i < input.Count; i++)
        {
            Assert.Equal(input[i].Name, vm.Rows[i].Name);
            Assert.Equal(input[i].Index, vm.Rows[i].Index);
        }
    }

    [Fact]
    public void Constructor_DoesNotMutateInputList()
    {
        // Arrange — records are immutable, so capture references and verify they remain.
        var input = MakeInput();
        var snapshotRefs = input.ToArray();

        // Act
        _ = MakeVm(input);

        // Assert — same references in the same positions, same field values.
        for (int i = 0; i < input.Count; i++)
        {
            Assert.Same(snapshotRefs[i], input[i]);
            Assert.Equal(snapshotRefs[i], input[i]);
        }
    }

    [Fact]
    public void ApplyCommand_InvokesCallback_WithCurrentDraftAsList()
    {
        // Arrange
        var (vm, captures) = MakeVm();

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        Assert.Single(captures);
        Assert.NotNull(captures[0]);
        Assert.Equal(vm.Rows.Count, captures[0].Count);
    }

    [Fact]
    public void ApplyCommand_RowOrderPreserved()
    {
        // Arrange
        var input = MakeInput();
        var (vm, captures) = MakeVm(input);

        // Act
        vm.ApplyCommand.Execute(null);

        // Assert
        var captured = captures.Single();
        for (int i = 0; i < input.Count; i++)
        {
            Assert.Equal(input[i].Name, captured[i].Name);
            Assert.Equal(input[i].Index, captured[i].Index);
        }
    }

    [Fact]
    public void ApplyCommand_ReflectsToggles()
    {
        // Arrange
        var (vm, captures) = MakeVm();
        var firstRow = vm.Rows[0];
        var originalDisplay = firstRow.Display;

        // Act — flip Display on the first row, then commit.
        firstRow.Display = !originalDisplay;
        vm.ApplyCommand.Execute(null);

        // Assert — the captured snapshot reflects the toggle.
        var captured = captures.Single();
        Assert.Equal(!originalDisplay, captured[0].Display);
    }

    [Fact]
    public void DismissCommand_DoesNotInvokeCallback()
    {
        // Arrange — the surviving dismiss command (replaces the old Cancel/Close pair).
        var (vm, captures) = MakeVm();

        // Act
        vm.DismissCommand.Execute(null);

        // Assert
        Assert.Empty(captures);
    }

    [Fact]
    public void Constructor_IsDirty_StartsFalse_AndLabelIsClose()
    {
        // Arrange / Act
        var (vm, _) = MakeVm();

        // Assert — fresh dialog has nothing to apply yet, so the dismiss button reads "Close".
        Assert.False(vm.IsDirty);
        Assert.Equal("Close", vm.DismissButtonLabel);
    }

    [Fact]
    public void RowDisplayChange_FlipsIsDirty_AndDismissLabel()
    {
        // Arrange
        var (vm, _) = MakeVm();

        // Act — toggle Display on the first row.
        vm.Rows[0].Display = !vm.Rows[0].Display;

        // Assert — the row's PropertyChanged subscription must mark the VM dirty
        // and surface the "Cancel" label so the user can back out.
        Assert.True(vm.IsDirty);
        Assert.Equal("Cancel", vm.DismissButtonLabel);
    }

    [Fact]
    public void RowAggregateChange_FlipsIsDirty()
    {
        // Arrange — default input has Aggregate true on rows 0 and 1; row 2 ("Mid") is false
        // and not locked, so flipping it is a clean legal change that should flip IsDirty.
        var (vm, _) = MakeVm();
        Assert.False(vm.Rows[2].Aggregate);

        // Act
        vm.Rows[2].Aggregate = true;

        // Assert
        Assert.True(vm.Rows[2].Aggregate);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void RowCorrelationChange_FlipsIsDirty()
    {
        // Arrange
        var (vm, _) = MakeVm();

        // Act
        vm.Rows[0].Correlation = !vm.Rows[0].Correlation;

        // Assert
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void ApplyCommand_ResetsIsDirty_AndDismissLabel()
    {
        // Arrange — make a real edit so the VM is dirty before Apply runs.
        var (vm, _) = MakeVm();
        vm.Rows[0].Display = !vm.Rows[0].Display;
        Assert.True(vm.IsDirty);
        Assert.Equal("Cancel", vm.DismissButtonLabel);

        // Act — committing the draft must clear the dirty flag and revert the label.
        vm.ApplyCommand.Execute(null);

        // Assert
        Assert.False(vm.IsDirty);
        Assert.Equal("Close", vm.DismissButtonLabel);
    }

    [Fact]
    public void RejectedAggregateClear_DoesNotFlipIsDirty()
    {
        // Arrange — single-Aggregate-true input. Clearing the only Aggregate row must be
        // rejected by the cross-row guard, and because the row VM setter returns early
        // before SetProperty (research §G1), no PropertyChanged fires and IsDirty stays false.
        var input = new List<ScoreSelection>
        {
            new("A", null, ScoreColumnType.Numeric, Display: true, Aggregate: true,  Correlation: false),
            new("B", null, ScoreColumnType.Numeric, Display: true, Aggregate: false, Correlation: false),
        };
        var (vm, _) = MakeVm(input);
        Assert.False(vm.IsDirty);

        // Act — attempt to clear the only Aggregate row.
        vm.Rows[0].Aggregate = false;

        // Assert — guard fires, value stays true, IsDirty stays false.
        Assert.True(vm.Rows[0].Aggregate);
        Assert.False(vm.IsDirty);
        Assert.Equal("Close", vm.DismissButtonLabel);
    }

    [Fact]
    public void DisplayAllCommand_SetsAllRowsTrue()
    {
        // Arrange
        var (vm, _) = MakeVm();

        // Act
        vm.DisplayAllCommand.Execute(null);

        // Assert
        Assert.All(vm.Rows, r => Assert.True(r.Display));
    }

    [Fact]
    public void DisplayNoneCommand_SetsAllRowsFalse()
    {
        // Arrange
        var (vm, _) = MakeVm();

        // Act
        vm.DisplayNoneCommand.Execute(null);

        // Assert
        Assert.All(vm.Rows, r => Assert.False(r.Display));
    }

    [Fact]
    public void CorrelationAllCommand_SetsAllRowsTrue()
    {
        // Arrange
        var (vm, _) = MakeVm();

        // Act
        vm.CorrelationAllCommand.Execute(null);

        // Assert
        Assert.All(vm.Rows, r => Assert.True(r.Correlation));
    }

    [Fact]
    public void CorrelationNoneCommand_SetsAllRowsFalse()
    {
        // Arrange
        var (vm, _) = MakeVm();

        // Act
        vm.CorrelationNoneCommand.Execute(null);

        // Assert
        Assert.All(vm.Rows, r => Assert.False(r.Correlation));
    }

    [Fact]
    public void AggregateAllCommand_SkipsLockedTotalRow()
    {
        // Arrange
        var (vm, _) = MakeVm();

        // Act
        vm.AggregateAllCommand.Execute(null);

        // Assert — every non-locked row is now true; the locked Total row stays false.
        foreach (var row in vm.Rows)
        {
            if (row.IsAggregateLocked)
            {
                Assert.False(row.Aggregate);
            }
            else
            {
                Assert.True(row.Aggregate);
            }
        }
    }

    [Fact]
    public void AggregateNoneCommand_AlwaysDisabled()
    {
        // Arrange
        var (vm, _) = MakeVm();

        // Act / Assert
        Assert.False(vm.AggregateNoneCommand.CanExecute(null));
    }

    [Fact]
    public void LastAggregateGuard_ProgrammaticallyClearAllInSequence_FinalCannotBeCleared()
    {
        // Arrange — a 3-row input where all three start with Aggregate=true so we can
        // walk the clear sequence and observe the last-Aggregate guard fire.
        var input = new List<ScoreSelection>
        {
            new("A", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: false),
            new("B", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: false),
            new("C", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: false),
        };
        var (vm, _) = MakeVm(input);

        // Act / Assert — clearing the first two succeeds, the third is rejected by G1.
        vm.Rows[0].Aggregate = false;
        Assert.False(vm.Rows[0].Aggregate);

        vm.Rows[1].Aggregate = false;
        Assert.False(vm.Rows[1].Aggregate);

        // Now only Rows[2] is true; the cross-row guard returns false and the
        // setter must reject the clear.
        vm.Rows[2].Aggregate = false;
        Assert.True(vm.Rows[2].Aggregate);
    }

    // ===== Slice 2 — Type column =====

    private static IReadOnlyList<ScoreSelection> MakeMixedInput()
    {
        // Two Numeric Aggregate-on rows, one Categorical (Display=true), one locked Total.
        return new List<ScoreSelection>
        {
            new("Q#", 1, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true),
            new("Q#", 2, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true),
            new("Submitted Outline", null, ScoreColumnType.Categorical, Display: true, Aggregate: false, Correlation: false),
            new("Total", null, ScoreColumnType.Numeric, Display: true, Aggregate: false, Correlation: false),
        };
    }

    [Fact]
    public void ExecuteApply_IncludesTypeInSnapshot()
    {
        var (vm, captures) = MakeVm(MakeMixedInput());

        vm.ApplyCommand.Execute(null);

        Assert.Single(captures);
        var snapshot = captures[0];
        Assert.Equal(ScoreColumnType.Numeric, snapshot.First(s => s.Name == "Q#" && s.Index == 1).Type);
        Assert.Equal(ScoreColumnType.Categorical, snapshot.First(s => s.Name == "Submitted Outline").Type);
    }

    [Fact]
    public void SetAllDisplay_SkipsCategoricalRows()
    {
        var (vm, _) = MakeVm(MakeMixedInput());

        vm.DisplayNoneCommand.Execute(null);

        Assert.False(vm.Rows[0].Display); // Numeric
        Assert.False(vm.Rows[1].Display); // Numeric
        Assert.True(vm.Rows[2].Display);  // Categorical — untouched
    }

    [Fact]
    public void SetAllCorrelation_SkipsCategoricalRows()
    {
        var (vm, _) = MakeVm(MakeMixedInput());

        vm.CorrelationAllCommand.Execute(null);

        Assert.True(vm.Rows[0].Correlation);
        Assert.True(vm.Rows[1].Correlation);
        Assert.False(vm.Rows[2].Correlation); // Categorical — untouched
    }

    [Fact]
    public void SetAllAggregate_SkipsCategoricalRows()
    {
        var (vm, _) = MakeVm(MakeMixedInput());
        // Pre-clear Aggregate on the Numeric rows so we can observe AggregateAll re-enabling them.
        vm.Rows[0].Aggregate = false;

        vm.AggregateAllCommand.Execute(null);

        Assert.True(vm.Rows[0].Aggregate);
        Assert.True(vm.Rows[1].Aggregate);
        Assert.False(vm.Rows[2].Aggregate); // Categorical — untouched
        Assert.False(vm.Rows[3].Aggregate); // Total locked
    }

    [Fact]
    public void LastAggregateGuard_CountsOnlyNumericRows()
    {
        // A Categorical row with Aggregate=true (defensive — shouldn't happen in practice)
        // must not count toward the last-Aggregate guard. With one Numeric Aggregate row
        // and one Categorical Aggregate row, clearing the Numeric one would empty the set
        // → must be rejected.
        var input = new List<ScoreSelection>
        {
            new("Num", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: false),
            new("Cat", null, ScoreColumnType.Categorical, Display: true, Aggregate: true, Correlation: false),
        };
        var (vm, _) = MakeVm(input);

        vm.Rows[0].Aggregate = false;

        Assert.True(vm.Rows[0].Aggregate);
    }

    [Fact]
    public void SignificanceAllCommand_SetsAllRowsTrue_OnNumericAndCategorical()
    {
        // Slice 3: Significance is meaningful on BOTH Numeric and Categorical rows
        // (Numeric → matrix row; Categorical → matrix column). Bulk-toggle must
        // apply to all of them, unlike Display/Aggregate/Correlation which skip
        // Categorical rows.
        var input = new List<ScoreSelection>
        {
            new("Q#", 1, ScoreColumnType.Numeric,     Display: true, Aggregate: true,  Correlation: true,  Significance: false),
            new("Cat", null, ScoreColumnType.Categorical, Display: true, Aggregate: false, Correlation: false, Significance: false),
            new("Total", null, ScoreColumnType.Numeric, Display: true, Aggregate: false, Correlation: false, Significance: false),
        };
        var (vm, _) = MakeVm(input);

        vm.SignificanceAllCommand.Execute(null);

        Assert.All(vm.Rows, r => Assert.True(r.Significance));
    }

    [Fact]
    public void SignificanceNoneCommand_SetsAllRowsFalse()
    {
        var input = new List<ScoreSelection>
        {
            new("Q#", 1, ScoreColumnType.Numeric, Display: true, Aggregate: true,  Correlation: true,  Significance: true),
            new("Cat", null, ScoreColumnType.Categorical, Display: true, Aggregate: false, Correlation: false, Significance: true),
        };
        var (vm, _) = MakeVm(input);

        vm.SignificanceNoneCommand.Execute(null);

        Assert.All(vm.Rows, r => Assert.False(r.Significance));
    }

    [Fact]
    public void ExecuteApply_IncludesSignificanceInSnapshot()
    {
        var input = new List<ScoreSelection>
        {
            new("Q", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true, Significance: false),
        };
        var (vm, captures) = MakeVm(input);

        vm.Rows[0].Significance = true;
        vm.ApplyCommand.Execute(null);

        Assert.Single(captures);
        Assert.True(captures[0][0].Significance);
    }

    [Fact]
    public void RowVM_CategoricalToNumeric_HonorsInjectedValidator()
    {
        // SettingsViewModel passes the per-row closure that calls back into the
        // canSwitchToNumeric predicate. Demonstrate end-to-end: a column whose
        // predicate returns false cannot be flipped Categorical → Numeric.
        var input = new List<ScoreSelection>
        {
            new("Mid-Term", null, ScoreColumnType.Categorical, Display: true, Aggregate: false, Correlation: false),
            new("Q", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true),
        };
        var captures = new List<IReadOnlyList<ScoreSelection>>();
        var vm = new SettingsViewModel(input,
            list => captures.Add(list),
            canSwitchToNumeric: (name, idx) => name != "Mid-Term");

        vm.Rows[0].Type = ScoreColumnType.Numeric;

        Assert.Equal(ScoreColumnType.Categorical, vm.Rows[0].Type);
    }
}
