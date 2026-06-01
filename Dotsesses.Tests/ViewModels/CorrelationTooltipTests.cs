namespace Dotsesses.Tests.ViewModels;

using Dotsesses.Models;
using Dotsesses.UI;

/// <summary>
/// Covers <see cref="CorrelationPlotViewModel.BuildPointTooltip"/> (ADR-0018
/// slice 3) — the per-cell hover text. The tooltip leads with the effect-size
/// headline (r²), names the method (Pearson/Spearman), shows raw p + stars + N
/// as support, and flags corrected cells. The Avalonia wiring that attaches it
/// is verified by manual smoke test; the formatting is pinned here.
/// </summary>
public class CorrelationTooltipTests
{
    private static CorrelationDataPoint Point(
        string xSeries = "A", string ySeries = "B", double r = 0.5,
        double? p = 0.02, int n = 30, string method = "pearson", bool corrected = false,
        double xValue = 0, double yValue = 0, double yRaw = 0)
        => new(0, 1, 0, 0, 1, xSeries, ySeries, xValue, yValue, "#fff",
               R: r, PValue: p, N: n, Method: method, Corrected: corrected, YRaw: yRaw);

    [Fact]
    public void LeadsWithEffectSize_AndNamesPearson()
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(Point(r: 0.8, method: "pearson"));

        Assert.Contains("A × B", text);
        Assert.Contains("r²=0.64", text);     // 0.8² leads the support line
        Assert.Contains("Pearson", text);
        Assert.Contains("r=0.80", text);
    }

    [Fact]
    public void Spearman_UsesRhoSymbolAndName()
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(Point(r: 1.0, method: "spearman"));

        Assert.Contains("Spearman", text);
        Assert.Contains("ρ=1.00", text);
    }

    [Theory]
    [InlineData(0.0005, "***")]
    [InlineData(0.005, "**")]
    [InlineData(0.03, "*")]
    public void SignificantP_ShowsStars(double p, string stars)
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(Point(p: p));
        Assert.Contains(stars, text);
        Assert.Contains("N=30", text);
    }

    [Fact]
    public void NonSignificantP_NoStars_AndStripsLeadingZero()
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(Point(p: 0.20));
        Assert.Contains("p=.200", text);     // leading zero stripped
        Assert.DoesNotContain("*", text);
    }

    [Fact]
    public void TinyP_RendersThreshold()
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(Point(p: 1e-9));
        Assert.Contains("p<.001", text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void NullP_RendersDash_NoStars()
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(Point(p: null));
        Assert.Contains("p —", text);
        Assert.DoesNotContain("*", text);
    }

    [Fact]
    public void CorrectedCell_NotesRestScore()
    {
        Assert.Contains("corrected (rest score)",
            CorrelationPlotViewModel.BuildPointTooltip(Point(corrected: true)));
        Assert.DoesNotContain("corrected",
            CorrelationPlotViewModel.BuildPointTooltip(Point(corrected: false)));
    }

    [Fact]
    public void UncorrectedDot_ShowsRawValues_NoSubtraction()
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(
            Point(xSeries: "Q1", ySeries: "Q2", corrected: false,
                  xValue: 12, yValue: 15, yRaw: 15));

        Assert.Contains("Q1 = 12", text);
        Assert.Contains("Q2 = 15", text);
        Assert.DoesNotContain("−", text);   // no subtraction for a raw cell
    }

    [Fact]
    public void CorrectedDot_SpellsOutTheRestScoreSubtraction()
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(
            Point(xSeries: "Q1", ySeries: "Total", corrected: true,
                  xValue: 12, yValue: 73, yRaw: 85));

        Assert.Contains("Q1 = 12", text);
        Assert.Contains("Total − Q1 = 85 − 12 = 73", text);
    }

    [Fact]
    public void DecimalValues_DropTrailingZeros()
    {
        var text = CorrelationPlotViewModel.BuildPointTooltip(
            Point(xSeries: "Q1", ySeries: "Total", corrected: true,
                  xValue: 12.5, yValue: 72.5, yRaw: 85));

        Assert.Contains("Total − Q1 = 85 − 12.5 = 72.5", text);
    }
}
