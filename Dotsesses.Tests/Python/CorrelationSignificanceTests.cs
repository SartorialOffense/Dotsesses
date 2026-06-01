using System;
using System.Collections.Generic;
using System.Linq;
using Dotsesses.Models;
using Dotsesses.Services;
using Dotsesses.Tests.Python;

namespace Dotsesses.Tests.Python;

/// <summary>
/// ADR-0018 slice 3 — per-cell inference on the correlation tab: Pearson for
/// Numeric×Numeric (r + raw p), Spearman for any Ordinal-touching cell (ρ),
/// N, and the corrected flag, all surfaced on the data points for the tooltip.
/// Verified end-to-end through the real Python module via CSnakes.
/// </summary>
[Collection(PythonCollection.Name)]
public class CorrelationSignificanceTests
{
    private readonly PythonEnvironmentFixture _fixture;

    public CorrelationSignificanceTests(PythonEnvironmentFixture fixture) => _fixture = fixture;

    private static Dictionary<string, double> Col(params double[] vals)
        => vals.Select((v, i) => ($"S{i + 1:D3}", v)).ToDictionary(t => t.Item1, t => t.Item2);

    private List<CorrelationDataPoint> Generate(
        List<(string, Dictionary<string, double>)> series,
        Dictionary<string, CorrelationColumnInfo> meta)
    {
        var service = new CorrelationPlotService(_fixture.Env);
        var muppets = Enumerable.Range(1, 12).ToDictionary(i => i, i => $"m{i}");
        var (_, points) = service.GeneratePlot((6.0, 6.0), series, meta, muppets);
        return points;
    }

    private static CorrelationColumnInfo Numeric =>
        new(ScoreColumnType.Numeric, BiasCorrect: false, IsTotal: false);

    [Fact]
    public void NumericPair_UsesPearson_WithKnownPValue()
    {
        // Perfectly linear, positively correlated → r = 1.0, p ≈ 0, method pearson.
        var series = new List<(string, Dictionary<string, double>)>
        {
            ("A", Col(1, 2, 3, 4, 5, 6)),
            ("B", Col(2, 4, 6, 8, 10, 12)),
        };
        var meta = new Dictionary<string, CorrelationColumnInfo> { ["A"] = Numeric, ["B"] = Numeric };

        var p = Generate(series, meta).First();

        Assert.Equal("pearson", p.Method);
        Assert.Equal(1.0, p.R, precision: 6);
        Assert.Equal(6, p.N);
        Assert.NotNull(p.PValue);
        Assert.True(p.PValue < 0.001, $"perfect correlation should be highly significant, got {p.PValue}");
        Assert.False(p.Corrected);
    }

    [Fact]
    public void OrdinalTouchingCell_UsesSpearman()
    {
        // "Rank" is an Ordinal column → the A×Rank cell must use Spearman. The
        // relationship is monotonic but non-linear, so ρ = 1 while Pearson r < 1
        // — but we only assert the method here.
        var series = new List<(string, Dictionary<string, double>)>
        {
            ("A", Col(1, 2, 3, 4, 5, 6)),
            ("Rank", Col(1, 2, 3, 4, 5, 6)),
        };
        var meta = new Dictionary<string, CorrelationColumnInfo>
        {
            ["A"] = Numeric,
            ["Rank"] = new(ScoreColumnType.Ordinal, BiasCorrect: false, IsTotal: false),
        };

        var p = Generate(series, meta).First();

        Assert.Equal("spearman", p.Method);
        Assert.Equal(6, p.N);
        Assert.NotNull(p.PValue);
    }

    [Fact]
    public void SpearmanBeatsPearson_OnMonotonicNonlinearData()
    {
        // y = x³ is monotonic but not linear: Spearman ρ = 1, Pearson r < 1.
        // Confirms we genuinely route through spearmanr, not a relabeled Pearson.
        var xs = new double[] { 1, 2, 3, 4, 5, 6, 7 };
        var cube = xs.Select(x => x * x * x).ToArray();

        var ordinalSeries = new List<(string, Dictionary<string, double>)>
        {
            ("Cube", Col(cube)),
            ("Rank", Col(xs)),
        };
        var ordinalMeta = new Dictionary<string, CorrelationColumnInfo>
        {
            ["Cube"] = Numeric,
            ["Rank"] = new(ScoreColumnType.Ordinal, BiasCorrect: false, IsTotal: false),
        };
        var rho = Generate(ordinalSeries, ordinalMeta).First().R;

        var numericSeries = new List<(string, Dictionary<string, double>)>
        {
            ("Cube", Col(cube)),
            ("Lin", Col(xs)),
        };
        var numericMeta = new Dictionary<string, CorrelationColumnInfo> { ["Cube"] = Numeric, ["Lin"] = Numeric };
        var r = Generate(numericSeries, numericMeta).First().R;

        Assert.Equal(1.0, rho, precision: 6);     // Spearman: perfect monotonic
        Assert.True(r < 0.99, $"Pearson on x³ should be < 1, got {r}");
    }

    [Fact]
    public void UncorrelatedData_NotSignificant_NoStarsImpliedByHighP()
    {
        // Deliberately flat/noisy relationship → high p (≥ .05). We assert the
        // raw p crosses no star threshold; the in-cell stars derive from this.
        var series = new List<(string, Dictionary<string, double>)>
        {
            ("A", Col(1, 2, 3, 4, 5, 6)),
            ("B", Col(3, 1, 4, 1, 5, 2)),
        };
        var meta = new Dictionary<string, CorrelationColumnInfo> { ["A"] = Numeric, ["B"] = Numeric };

        var p = Generate(series, meta).First();

        Assert.NotNull(p.PValue);
        Assert.True(p.PValue >= 0.05, $"expected non-significant p, got {p.PValue}");
    }

    [Fact]
    public void CorrectedCell_CarriesCorrectedFlag()
    {
        // Q1 is an aggregate component, Total contains it → the Q1×Total cell is
        // de-biased and must report Corrected=true; an uncorrected cell false.
        var series = new List<(string, Dictionary<string, double>)>
        {
            ("Q1", Col(10, 15, 5, 20, 12, 8)),
            ("Q2", Col(20, 10, 25, 15, 18, 22)),
            ("Total", Col(30, 25, 30, 35, 30, 30)),
        };
        var meta = new Dictionary<string, CorrelationColumnInfo>
        {
            ["Q1"] = new(ScoreColumnType.Numeric, BiasCorrect: true, IsTotal: false),
            ["Q2"] = new(ScoreColumnType.Numeric, BiasCorrect: true, IsTotal: false),
            ["Total"] = new(ScoreColumnType.Numeric, BiasCorrect: false, IsTotal: true),
        };

        var points = Generate(series, meta);

        var corrected = points.First(p => p.XSeries == "Q1" && p.YSeries == "Total");
        Assert.True(corrected.Corrected);

        var plain = points.First(p => p.XSeries == "Q1" && p.YSeries == "Q2");
        Assert.False(plain.Corrected);
    }
}
