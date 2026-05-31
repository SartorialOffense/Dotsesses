namespace Dotsesses.Tests.Calculators;

using Dotsesses.Calculators;
using Xunit;

/// <summary>
/// Covers Significance Matrix subgroup ordering (ADR-0017): suffixed-by-N
/// first, unsuffixed alpha after, N ties alpha, conflicts to min N.
/// </summary>
public class SubgroupOrderCalculatorTests
{
    [Fact]
    public void Order_AllSuffixed_OrdersByN()
    {
        var values = new (string, int?)[]
        {
            ("High", 3), ("Low", 1), ("Mid", 2),
        };

        var order = SubgroupOrderCalculator.Order(values);

        Assert.Equal(new[] { "Low", "Mid", "High" }, order);
    }

    [Fact]
    public void Order_UnsuffixedSortAfterSuffixed_Alphabetically()
    {
        var values = new (string, int?)[]
        {
            ("Pass", 2), ("Fail", 1), ("Incomplete", null), ("Absent", null),
        };

        var order = SubgroupOrderCalculator.Order(values);

        // Suffixed first by N (Fail, Pass), then unsuffixed alpha (Absent, Incomplete).
        Assert.Equal(new[] { "Fail", "Pass", "Absent", "Incomplete" }, order);
    }

    [Fact]
    public void Order_TieOnN_BreaksAlphabetically()
    {
        var values = new (string, int?)[]
        {
            ("Pass", 1), ("Fail", 1),
        };

        var order = SubgroupOrderCalculator.Order(values);

        Assert.Equal(new[] { "Fail", "Pass" }, order);
    }

    [Fact]
    public void Order_CollapsesDuplicateLabels()
    {
        var values = new (string, int?)[]
        {
            ("Yes", 1), ("Yes", 1), ("No", 2), ("No", 2), ("No", 2),
        };

        var order = SubgroupOrderCalculator.Order(values);

        Assert.Equal(new[] { "Yes", "No" }, order);
    }

    [Fact]
    public void Order_LabelWithAndWithoutSortOrder_TreatedAsSuffixedAtMinN()
    {
        // Defensive: a label seen both suffixed and bare uses the min present N
        // and sorts among the suffixed group.
        var values = new (string, int?)[]
        {
            ("Pass", 5), ("Pass", null), ("Fail", 1), ("Zzz", null),
        };

        var order = SubgroupOrderCalculator.Order(values);

        // Fail(1), Pass(5) suffixed; Zzz unsuffixed last.
        Assert.Equal(new[] { "Fail", "Pass", "Zzz" }, order);
    }

    [Fact]
    public void Order_AllUnsuffixed_IsAlphabetical()
    {
        var values = new (string, int?)[]
        {
            ("Charlie", null), ("Alpha", null), ("Bravo", null),
        };

        var order = SubgroupOrderCalculator.Order(values);

        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, order);
    }

    [Fact]
    public void Order_Empty_ReturnsEmpty()
    {
        var order = SubgroupOrderCalculator.Order(System.Array.Empty<(string, int?)>());
        Assert.Empty(order);
    }
}
