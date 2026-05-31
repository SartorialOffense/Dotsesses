namespace Dotsesses.Tests.ViewModels;

using System.Collections.Generic;
using System.Linq;
using Dotsesses.Models;
using Dotsesses.UI;

/// <summary>
/// Covers <see cref="MainWindowViewModel.BuildCorrelationColumnMetadata"/>
/// (ADR-0018 slice 1) — the per-series flags Python keys behavior off instead
/// of inferring column roles from position. Total is identified by name+flag,
/// aggregate components by <c>Numeric &amp;&amp; Aggregate &amp;&amp; !Total</c>, and the
/// map honors the same selector filtering as the series builder.
/// </summary>
public class BuildCorrelationColumnMetadataTests
{
    /// <summary>
    /// Two aggregate components (Q1, Q2), a Total that sums them, a displayed
    /// non-aggregate numeric (Extra), and an Ordinal (Mid-Term). All marked
    /// Correlation=true so they reach the correlation payload.
    /// </summary>
    private static ClassAssessment Fixture()
    {
        var students = new List<StudentAssessment>
        {
            new(1,
                new List<Score> { new("Q1", null, 40), new("Q2", null, 40),
                                   new("Extra", null, 5), new("Total", null, 80) },
                new List<StudentAttribute> { new("Mid-Term", null, "✔", SortOrder: 1) },
                "muppet1"),
            new(2,
                new List<Score> { new("Q1", null, 30), new("Q2", null, 35),
                                   new("Extra", null, 9), new("Total", null, 65) },
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
            new("Q1", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true),
            new("Q2", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true),
            new("Extra", null, ScoreColumnType.Numeric, Display: true, Aggregate: false, Correlation: true),
            new("Total", null, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true),
            new("Mid-Term", null, ScoreColumnType.Ordinal, Display: true, Aggregate: false, Correlation: true),
        };
        return ca;
    }

    [Fact]
    public void Total_FlaggedIsTotal_AndNotAComponent()
    {
        var map = MainWindowViewModel.BuildCorrelationColumnMetadata(Fixture(), s => s.Correlation);

        var total = map["Total"];
        Assert.True(total.IsTotal);
        Assert.False(total.IsAggregateComponent);
        Assert.Equal(ScoreColumnType.Numeric, total.Type);
    }

    [Fact]
    public void AggregateComponents_FlaggedComponent_NotTotal()
    {
        var map = MainWindowViewModel.BuildCorrelationColumnMetadata(Fixture(), s => s.Correlation);

        foreach (var name in new[] { "Q1", "Q2" })
        {
            Assert.True(map[name].IsAggregateComponent, $"{name} should be an aggregate component");
            Assert.False(map[name].IsTotal, $"{name} should not be Total");
        }
    }

    [Fact]
    public void NonAggregateNumeric_NotAComponent()
    {
        var map = MainWindowViewModel.BuildCorrelationColumnMetadata(Fixture(), s => s.Correlation);

        Assert.False(map["Extra"].IsAggregateComponent);
        Assert.False(map["Extra"].IsTotal);
    }

    [Fact]
    public void Ordinal_TypedOrdinal_NeverComponent()
    {
        var map = MainWindowViewModel.BuildCorrelationColumnMetadata(Fixture(), s => s.Correlation);

        Assert.Equal(ScoreColumnType.Ordinal, map["Mid-Term"].Type);
        Assert.False(map["Mid-Term"].IsAggregateComponent);
        Assert.False(map["Mid-Term"].IsTotal);
    }

    [Fact]
    public void OrdinalMarkedAggregate_StillNotAComponent()
    {
        // Defensive: an Ordinal's N is a rank, never summed — even if Aggregate
        // somehow reads true, the Type guard keeps it out of the component set.
        var ca = Fixture();
        ca.ScoreSelections = ca.ScoreSelections
            .Select(s => s.Type == ScoreColumnType.Ordinal ? s with { Aggregate = true } : s)
            .ToList();

        var map = MainWindowViewModel.BuildCorrelationColumnMetadata(ca, s => s.Correlation);

        Assert.False(map["Mid-Term"].IsAggregateComponent);
    }

    [Fact]
    public void Selector_ExcludesUnselectedColumns()
    {
        var ca = Fixture();
        ca.ScoreSelections = ca.ScoreSelections
            .Select(s => s.Name == "Extra" ? s with { Correlation = false } : s)
            .ToList();

        var map = MainWindowViewModel.BuildCorrelationColumnMetadata(ca, s => s.Correlation);

        Assert.False(map.ContainsKey("Extra"));
        Assert.True(map.ContainsKey("Total"));
    }
}
