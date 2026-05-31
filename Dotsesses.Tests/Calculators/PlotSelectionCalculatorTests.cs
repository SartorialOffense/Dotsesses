namespace Dotsesses.Tests.Calculators;

using System;
using System.Collections.Generic;
using System.Linq;
using Dotsesses.Calculators;
using Xunit;

/// <summary>
/// Covers the data-driven default plot selection rules (ADR-0016):
/// distribution = Total + 10 leftmost numerics; correlation = top-r²
/// non-Total pairs' union + Total; significance = qualifying categoricals
/// (min p ≤ 0.2) + their top-3 smallest-p numerics.
/// </summary>
public class PlotSelectionCalculatorTests
{
    private static IReadOnlyDictionary<string, double> Series(params double[] values)
    {
        var d = new Dictionary<string, double>();
        for (int i = 0; i < values.Length; i++) d[$"S{i:D3}"] = values[i];
        return d;
    }

    // ===== Distribution =====

    [Fact]
    public void SelectDistribution_TakesTotalPlusFirstTen()
    {
        var names = new[] { "Total", "Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7", "Q8", "Q9", "Q10", "Q11", "Q12" };
        var result = PlotSelectionCalculator.SelectDistribution(names, "Total");

        Assert.Contains("Total", result);
        Assert.Equal(11, result.Count); // Total + first 10 non-Total
        for (int i = 1; i <= 10; i++) Assert.Contains($"Q{i}", result);
        Assert.DoesNotContain("Q11", result);
        Assert.DoesNotContain("Q12", result);
    }

    [Fact]
    public void SelectDistribution_FewerThanTen_TakesAll()
    {
        var names = new[] { "Total", "Q1", "Q2", "Q3" };
        var result = PlotSelectionCalculator.SelectDistribution(names, "Total");
        Assert.Equal(new HashSet<string> { "Total", "Q1", "Q2", "Q3" }, result);
    }

    [Fact]
    public void SelectDistribution_NoTotal_TakesFirstTen()
    {
        var names = Enumerable.Range(1, 15).Select(i => $"Q{i}").ToList();
        var result = PlotSelectionCalculator.SelectDistribution(names, totalName: null);
        Assert.Equal(10, result.Count);
        Assert.Contains("Q1", result);
        Assert.Contains("Q10", result);
        Assert.DoesNotContain("Q11", result);
    }

    // ===== Pearson r² =====

    [Fact]
    public void PearsonR2_PerfectLinear_IsOne()
    {
        var a = Series(1, 2, 3, 4, 5);
        var b = Series(2, 4, 6, 8, 10);   // b = 2a
        Assert.Equal(1.0, PlotSelectionCalculator.PearsonR2(a, b)!.Value, 6);
    }

    [Fact]
    public void PearsonR2_PerfectInverse_IsOne()
    {
        var a = Series(1, 2, 3, 4, 5);
        var b = Series(5, 4, 3, 2, 1);    // r = -1 → r² = 1
        Assert.Equal(1.0, PlotSelectionCalculator.PearsonR2(a, b)!.Value, 6);
    }

    [Fact]
    public void PearsonR2_KnownValue()
    {
        // x=[1,2,3], y=[1,3,2] → r = 0.5 → r² = 0.25 (hand-computed).
        var x = Series(1, 2, 3);
        var y = Series(1, 3, 2);
        Assert.Equal(0.25, PlotSelectionCalculator.PearsonR2(x, y)!.Value, 6);
    }

    [Fact]
    public void PearsonR2_ZeroVarianceOrTooFewCommon_IsNull()
    {
        Assert.Null(PlotSelectionCalculator.PearsonR2(Series(1, 1, 1), Series(2, 3, 4))); // flat x
        Assert.Null(PlotSelectionCalculator.PearsonR2(
            new Dictionary<string, double> { ["S000"] = 1 },
            new Dictionary<string, double> { ["S000"] = 2 }));                            // <2 common
    }

    // ===== Correlation =====

    [Fact]
    public void SelectCorrelation_PicksTopPairColumns_PlusTotal_ExcludesLowCorrelates()
    {
        // A=B=C=D are identical → every pair among them has r²=1, so the top-4
        // pairs are all within {A,B,C,D}; E and F are noise and never make the
        // cut. Total is excluded from ranking but always added.
        var lin = new double[] { 1, 2, 3, 4, 5, 6 };
        var series = new List<(string, IReadOnlyDictionary<string, double>)>
        {
            ("A", Series(lin)),
            ("B", Series(lin)),
            ("C", Series(lin)),
            ("D", Series(lin)),
            ("E", Series(1, 1, 2, 2, 1, 1)),
            ("F", Series(3, 1, 4, 1, 5, 2)),
            ("Total", Series(2, 4, 6, 8, 10, 12)),
        };

        var result = PlotSelectionCalculator.SelectCorrelation(series, "Total");

        Assert.Contains("Total", result);
        foreach (var c in new[] { "A", "B", "C", "D" }) Assert.Contains(c, result);
        Assert.DoesNotContain("E", result);
        Assert.DoesNotContain("F", result);
    }

    // ===== Significance =====

    [Fact]
    public void SelectSignificance_QualifiersPlusTopThreeNumerics()
    {
        var numerics = new[] { "Q1", "Q2", "Q3", "Q4", "Q5" };
        var cats = new[] { "Hat", "Section", "Gender" };
        var p = new Dictionary<(string, string), double?>
        {
            // Hat qualifies (min .01); top-3 smallest = Q1, Q3, Q5
            [("Q1", "Hat")] = 0.01, [("Q2", "Hat")] = 0.50, [("Q3", "Hat")] = 0.03,
            [("Q4", "Hat")] = 0.80, [("Q5", "Hat")] = 0.15,
            // Section: all > .2 → dropped
            [("Q1", "Section")] = 0.30, [("Q2", "Section")] = 0.40, [("Q3", "Section")] = 0.55,
            [("Q4", "Section")] = 0.99, [("Q5", "Section")] = 0.60,
            // Gender qualifies at the .2 boundary (inclusive); top-3 = Q2, Q4, Q1
            [("Q1", "Gender")] = 0.30, [("Q2", "Gender")] = 0.20, [("Q3", "Gender")] = null,
            [("Q4", "Gender")] = 0.25, [("Q5", "Gender")] = 0.90,
        };

        var result = PlotSelectionCalculator.SelectSignificance(
            numerics, cats, (n, c) => p.TryGetValue((n, c), out var v) ? v : null);

        // Hat + its top-3
        Assert.Contains("Hat", result);
        Assert.Contains("Q1", result);
        Assert.Contains("Q3", result);
        Assert.Contains("Q5", result);
        // Gender qualifies at exactly 0.2; its top-3 smallest are Q2, Q4, Q1
        Assert.Contains("Gender", result);
        Assert.Contains("Q2", result);
        Assert.Contains("Q4", result);
        // Section dropped (no cell ≤ 0.2)
        Assert.DoesNotContain("Section", result);
        // Q2-of-Hat (.5) is not in Hat's top-3, but Q2 is pulled in via Gender — fine.
    }

    [Fact]
    public void SelectSignificance_NoQualifiers_IsEmpty()
    {
        var result = PlotSelectionCalculator.SelectSignificance(
            new[] { "Q1", "Q2" },
            new[] { "Hat" },
            (_, _) => 0.9);
        Assert.Empty(result);
    }

    [Fact]
    public void SelectSignificance_AllUntestable_IsEmpty()
    {
        var result = PlotSelectionCalculator.SelectSignificance(
            new[] { "Q1", "Q2" },
            new[] { "Hat" },
            (_, _) => null);
        Assert.Empty(result);
    }
}
