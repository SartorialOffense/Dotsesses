using System.Collections.Generic;
using System.Linq;
using Dotsesses.Models;
using Dotsesses.Services;
using Dotsesses.Tests.Python;

namespace Dotsesses.Tests.Python;

/// <summary>
/// Drives the real correlation Python module through CSnakes (ADR-0018 test
/// strategy). Slice 1 focus: the new column-metadata payload reaches Python and
/// Total is identified by its flag, not by being the last series.
/// </summary>
[Collection(PythonCollection.Name)]
public class CorrelationPlotServiceTests
{
    private readonly PythonEnvironmentFixture _fixture;

    public CorrelationPlotServiceTests(PythonEnvironmentFixture fixture) => _fixture = fixture;

    private static Dictionary<string, double> Series(params (string Id, double Val)[] pts)
        => pts.ToDictionary(p => p.Id, p => p.Val);

    [Fact]
    public void GeneratePlot_TotalNotLast_RendersAndReturnsLowerTrianglePoints()
    {
        var service = new CorrelationPlotService(_fixture.Env);

        // Total deliberately placed FIRST. The old code colored the *last*
        // series red; with the explicit is_total flag this must still render
        // cleanly with Total identified by flag wherever it sits.
        var seriesData = new List<(string SeriesName, Dictionary<string, double> Scores)>
        {
            ("Total", Series(("S001", 80), ("S002", 65), ("S003", 90), ("S004", 50))),
            ("Q1",    Series(("S001", 40), ("S002", 30), ("S003", 48), ("S004", 20))),
            ("Q2",    Series(("S001", 40), ("S002", 35), ("S003", 42), ("S004", 30))),
        };
        var metadata = new Dictionary<string, CorrelationColumnInfo>
        {
            ["Total"] = new(ScoreColumnType.Numeric, BiasCorrect: false, IsTotal: true),
            ["Q1"] = new(ScoreColumnType.Numeric, BiasCorrect: true, IsTotal: false),
            ["Q2"] = new(ScoreColumnType.Numeric, BiasCorrect: true, IsTotal: false),
        };
        var muppetNames = new Dictionary<int, string>
        {
            [1] = "a", [2] = "b", [3] = "c", [4] = "d",
        };

        var (svg, points) = service.GeneratePlot(
            (6.0, 6.0), seriesData, metadata, muppetNames);

        Assert.False(string.IsNullOrWhiteSpace(svg));
        // A 3-series corner plot has 3 lower-triangle scatter cells, each with
        // one point per student (4) → 12 data points.
        Assert.Equal(12, points.Count);
        // Every emitted point names series that exist in the payload.
        var names = new HashSet<string> { "Total", "Q1", "Q2" };
        Assert.All(points, p =>
        {
            Assert.Contains(p.XSeries, names);
            Assert.Contains(p.YSeries, names);
        });
    }

    [Fact]
    public void GeneratePlot_MissingMetadata_FallsBackToTotalByNameWithoutThrowing()
    {
        var service = new CorrelationPlotService(_fixture.Env);

        // No metadata entries at all (the defensive no-selection passthrough
        // path). Total must still be inferred by name and the plot must render.
        var seriesData = new List<(string SeriesName, Dictionary<string, double> Scores)>
        {
            ("Q1",    Series(("S001", 40), ("S002", 30), ("S003", 48))),
            ("Total", Series(("S001", 80), ("S002", 65), ("S003", 90))),
        };
        var emptyMetadata = new Dictionary<string, CorrelationColumnInfo>();
        var muppetNames = new Dictionary<int, string> { [1] = "a", [2] = "b", [3] = "c" };

        var (svg, points) = service.GeneratePlot(
            (5.0, 5.0), seriesData, emptyMetadata, muppetNames);

        Assert.False(string.IsNullOrWhiteSpace(svg));
        // 2-series corner plot → 1 lower-triangle cell × 3 students = 3 points.
        Assert.Equal(3, points.Count);
    }
}
