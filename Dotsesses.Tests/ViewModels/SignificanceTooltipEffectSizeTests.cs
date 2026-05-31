namespace Dotsesses.Tests.ViewModels;

using Dotsesses.Models;
using Dotsesses.UI;

/// <summary>
/// ADR-0018 slice 4 — the Significance Matrix tooltip is now effect-size-led:
/// the variance-explained headline (η²/ε²) comes first, with raw p + test
/// demoted to a supporting line. Pins the formatting; the Avalonia attachment
/// is covered by manual smoke test.
/// </summary>
public class SignificanceTooltipEffectSizeTests
{
    private static SignificanceDataPoint Point(
        double? p = 0.01, double? effect = 0.42,
        SignificanceTestFamily family = SignificanceTestFamily.Parametric,
        int n = 12, bool excluded = false)
        => new(0, 0, 0, 0, "Group", "Score", "A", Mean: 5, Sem: 1, N: n, Color: "#fff",
               PValue: p, EffectSize: effect, TestFamily: family, Excluded: excluded);

    [Fact]
    public void Parametric_LeadsWithEtaSquared_PDemoted()
    {
        var text = SignificancePlotViewModel.BuildTooltip(Point(p: 0.002, effect: 0.42,
            family: SignificanceTestFamily.Parametric));

        Assert.Contains("η²=.42 variance explained", text);
        Assert.Contains("Welch ANOVA", text);
        Assert.Contains("p=.002", text);
        Assert.Contains("**", text);   // raw-p stars still present as support
    }

    [Fact]
    public void Nonparametric_UsesEpsilonSquared()
    {
        var text = SignificancePlotViewModel.BuildTooltip(Point(p: 0.04, effect: 0.30,
            family: SignificanceTestFamily.Nonparametric));

        Assert.Contains("ε²=.30 variance explained", text);
        Assert.Contains("Kruskal–Wallis", text);
        Assert.Contains("*", text);
    }

    [Fact]
    public void UntestableCell_NotTestable_NoEffectHeadline()
    {
        var text = SignificancePlotViewModel.BuildTooltip(Point(p: null, effect: null));
        Assert.Contains("not testable", text);
    }

    [Fact]
    public void ExcludedDot_StillReportsExclusion()
    {
        var text = SignificancePlotViewModel.BuildTooltip(Point(n: 1, excluded: true));
        Assert.Contains("excluded from test", text);
    }
}
