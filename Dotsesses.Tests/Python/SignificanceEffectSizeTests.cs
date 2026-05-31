using System.Collections.Generic;
using System.Linq;
using Dotsesses.Models;
using Dotsesses.Services;
using Dotsesses.Tests.Python;

namespace Dotsesses.Tests.Python;

/// <summary>
/// ADR-0018 slice 4 — variance-explained effect size on the Significance Matrix:
/// η² (Welch ANOVA / parametric) and ε² (Kruskal–Wallis / non-parametric),
/// the headline both stats tabs share. Verified against hand-worked numbers
/// through the real Python module via CSnakes (the cell's effect size rides on
/// every dot, like its p-value).
/// </summary>
[Collection(PythonCollection.Name)]
public class SignificanceEffectSizeTests
{
    private readonly PythonEnvironmentFixture _fixture;

    public SignificanceEffectSizeTests(PythonEnvironmentFixture fixture) => _fixture = fixture;

    // One numeric × one categorical cell. Group A = {1,2,3}, B = {7,8,9}.
    private static (List<(string, Dictionary<string, double>)> Num,
                    List<(string, Dictionary<string, string>)> Cat) TwoGroupCell()
    {
        var num = new List<(string, Dictionary<string, double>)>
        {
            ("Score", new Dictionary<string, double>
            {
                ["S1"] = 1, ["S2"] = 2, ["S3"] = 3,
                ["S4"] = 7, ["S5"] = 8, ["S6"] = 9,
            }),
        };
        var cat = new List<(string, Dictionary<string, string>)>
        {
            ("Group", new Dictionary<string, string>
            {
                ["S1"] = "A", ["S2"] = "A", ["S3"] = "A",
                ["S4"] = "B", ["S5"] = "B", ["S6"] = "B",
            }),
        };
        return (num, cat);
    }

    private List<SignificanceDataPoint> Generate(SignificanceTestFamily family)
    {
        var (num, cat) = TwoGroupCell();
        var service = new SignificancePlotService(_fixture.Env);
        var (_, points) = service.GeneratePlot((5.0, 5.0), num, cat, 5.0,
            ThemeName.DarkMode, family);
        return points;
    }

    [Fact]
    public void EtaSquared_TwoGroups_MatchesHandComputed()
    {
        // grand mean 5; SS_total = 58; SS_between = 3·(2−5)² + 3·(8−5)² = 54.
        // η² = 54/58 = 0.93103…  (η² is well-defined for the 2-group case.)
        var points = Generate(SignificanceTestFamily.Parametric);

        Assert.NotEmpty(points);
        Assert.All(points, p =>
        {
            Assert.NotNull(p.EffectSize);
            Assert.Equal(54.0 / 58.0, p.EffectSize!.Value, precision: 4);
        });
    }

    [Fact]
    public void EpsilonSquared_TwoGroups_MatchesHandComputed()
    {
        // KW H for fully separated {1,2,3} vs {7,8,9}: ranks 1-3 vs 4-6 →
        // H = 12/(6·7)·(6²/3 + 15²/3) − 3·7 = 3.857142…; ε² = H/(n−1) = H/5.
        var expected = (12.0 / (6 * 7) * (36.0 / 3 + 225.0 / 3) - 21.0) / 5.0;
        var points = Generate(SignificanceTestFamily.Nonparametric);

        Assert.NotEmpty(points);
        Assert.All(points, p =>
        {
            Assert.NotNull(p.EffectSize);
            Assert.Equal(expected, p.EffectSize!.Value, precision: 4);
        });
    }

    [Fact]
    public void Untestable_AllSubgroupsTooSmall_EffectSizeNull()
    {
        // Each subgroup has N=1 → fewer than 2 valid groups → untestable.
        var num = new List<(string, Dictionary<string, double>)>
        {
            ("Score", new Dictionary<string, double> { ["S1"] = 1, ["S2"] = 9 }),
        };
        var cat = new List<(string, Dictionary<string, string>)>
        {
            ("Group", new Dictionary<string, string> { ["S1"] = "A", ["S2"] = "B" }),
        };
        var service = new SignificancePlotService(_fixture.Env);
        var (_, points) = service.GeneratePlot((5.0, 5.0), num, cat, 5.0,
            ThemeName.DarkMode, SignificanceTestFamily.Parametric);

        Assert.NotEmpty(points);  // N=1 dots still render
        Assert.All(points, p => Assert.Null(p.EffectSize));
    }
}
