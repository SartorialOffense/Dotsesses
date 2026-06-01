namespace Dotsesses.Tests.ViewModels;

using System.Collections.Generic;
using System.Linq;
using Dotsesses.Models;
using Dotsesses.UI;

/// <summary>
/// Covers <see cref="MainWindowViewModel.BuildCorrelationColumnMetadata"/>
/// (ADR-0018). The per-series <c>BiasCorrect</c> flag — not aggregate membership
/// — drives the rest-score de-bias, so these tests pin that the metadata reads
/// <see cref="ScoreSelection.BiasCorrect"/>, is decoupled from
/// <see cref="ScoreSelection.Aggregate"/>, and is guarded to Numeric non-Total.
/// </summary>
public class BuildCorrelationColumnMetadataTests
{
    /// <summary>
    /// Columns exercising every combination of Aggregate × BiasCorrect:
    ///  - Component: aggregated AND bias-corrected (the usual case)
    ///  - AggOnly:   aggregated but bias-correct OFF (decoupled)
    ///  - Composite: NOT aggregated but bias-correct ON (the motivating case — a
    ///    Q1-Q4 composite whose value is contained in Total)
    ///  - Total:     bias-correct flag set but must be guarded off (can't correct
    ///    Total against itself)
    ///  - Mid-Term:  Ordinal with the flag set — guarded off (not Numeric)
    /// All Correlation=true so they reach the payload.
    /// </summary>
    private static ClassAssessment Fixture()
    {
        var students = new List<StudentAssessment>
        {
            new(1,
                new List<Score> { new("Component", null, 40), new("AggOnly", null, 10),
                                   new("Composite", null, 50), new("Total", null, 100) },
                new List<StudentAttribute> { new("Mid-Term", null, "✔", SortOrder: 1) },
                "muppet1"),
            new(2,
                new List<Score> { new("Component", null, 30), new("AggOnly", null, 5),
                                   new("Composite", null, 35), new("Total", null, 70) },
                new List<StudentAttribute> { new("Mid-Term", null, "✔✔+", SortOrder: 3) },
                "muppet2"),
        };
        var ca = new ClassAssessment(
            students,
            new List<CutoffCountRange>(),
            new Dictionary<int, MuppetNameInfo>(),
            new Dictionary<string, string>());
        ca.ScoreSelections = new List<ScoreSelection>
        {
            new("Component", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true, Significance: true, BiasCorrect: true),
            new("AggOnly", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true, Significance: true, BiasCorrect: false),
            new("Composite", null, ScoreColumnType.Numeric, Display: true, Aggregate: false, Correlation: true, Significance: true, BiasCorrect: true),
            new("Total", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true, Significance: true, BiasCorrect: true),
            new("Mid-Term", null, ScoreColumnType.Ordinal, Display: true, Aggregate: false, Correlation: true, Significance: true, BiasCorrect: true),
        };
        return ca;
    }

    private static Dictionary<string, CorrelationColumnInfo> Build()
        => MainWindowViewModel.BuildCorrelationColumnMetadata(Fixture(), s => s.Correlation);

    [Fact]
    public void BiasCorrectFlag_FlowsThrough_ForNumericNonTotal()
    {
        var map = Build();
        Assert.True(map["Component"].BiasCorrect);
    }

    [Fact]
    public void Composite_NotAggregated_ButBiasCorrected()
    {
        // The motivating case: a column the user does NOT aggregate (so it isn't
        // double-counted) is still de-biased because its BiasCorrect flag is on.
        var map = Build();
        Assert.True(map["Composite"].BiasCorrect);
    }

    [Fact]
    public void Aggregated_ButFlagOff_IsNotBiasCorrected()
    {
        // The decoupling in the other direction: aggregate membership no longer
        // implies de-bias — only the flag does.
        var map = Build();
        Assert.False(map["AggOnly"].BiasCorrect);
    }

    [Fact]
    public void Total_FlaggedIsTotal_AndGuardedOffEvenWithFlagSet()
    {
        var map = Build();
        Assert.True(map["Total"].IsTotal);
        Assert.False(map["Total"].BiasCorrect);   // can't de-bias Total against itself
        Assert.Equal(ScoreColumnType.Numeric, map["Total"].Type);
    }

    [Fact]
    public void Ordinal_WithFlagSet_GuardedOff_NotNumeric()
    {
        var map = Build();
        Assert.Equal(ScoreColumnType.Ordinal, map["Mid-Term"].Type);
        Assert.False(map["Mid-Term"].BiasCorrect);
    }

    [Fact]
    public void Selector_ExcludesUnselectedColumns()
    {
        var ca = Fixture();
        ca.ScoreSelections = ca.ScoreSelections
            .Select(s => s.Name == "Composite" ? s with { Correlation = false } : s)
            .ToList();

        var map = MainWindowViewModel.BuildCorrelationColumnMetadata(ca, s => s.Correlation);

        Assert.False(map.ContainsKey("Composite"));
        Assert.True(map.ContainsKey("Total"));
    }
}
