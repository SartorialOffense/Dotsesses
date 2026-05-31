using System.Collections.Generic;
using System.Linq;
using Dotsesses.Models;
using Dotsesses.Services;
using Dotsesses.Tests.Python;

namespace Dotsesses.Tests.Python;

/// <summary>
/// ADR-0018 slice 2 — rest-score de-bias. A Total × aggregate-component cell
/// correlates the component against Total − component (the rest score) instead
/// of against the full Total, which contains the component. We verify the
/// de-biased values actually feed the plot (the stored y_value is the rest
/// score), that uncorrected cells are untouched, and that a single-component
/// Total (rest ≡ 0) blanks the cell.
/// </summary>
[Collection(PythonCollection.Name)]
public class CorrelationDebiasTests
{
    private readonly PythonEnvironmentFixture _fixture;

    public CorrelationDebiasTests(PythonEnvironmentFixture fixture) => _fixture = fixture;

    // Q1+Q2+Q3 = Total; Extra is a non-aggregate numeric. Per-student values.
    private static readonly (int Id, double Q1, double Q2, double Q3, double Extra)[] Rows =
    {
        (1, 10, 20, 30, 7),
        (2, 15, 10, 25, 3),
        (3, 5, 25, 20, 9),
        (4, 20, 15, 15, 1),
    };

    private static Dictionary<string, double> Col(System.Func<(int Id, double Q1, double Q2, double Q3, double Extra), double> pick)
        => Rows.ToDictionary(r => $"S{r.Id:D3}", r => pick(r));

    private static (List<(string, Dictionary<string, double>)> Series,
                    Dictionary<string, CorrelationColumnInfo> Meta) BuildPayload()
    {
        var series = new List<(string, Dictionary<string, double>)>
        {
            ("Q1", Col(r => r.Q1)),
            ("Q2", Col(r => r.Q2)),
            ("Q3", Col(r => r.Q3)),
            ("Extra", Col(r => r.Extra)),
            ("Total", Col(r => r.Q1 + r.Q2 + r.Q3)),  // last → highest row index
        };
        var meta = new Dictionary<string, CorrelationColumnInfo>
        {
            ["Q1"] = new(ScoreColumnType.Numeric, IsAggregateComponent: true, IsTotal: false),
            ["Q2"] = new(ScoreColumnType.Numeric, IsAggregateComponent: true, IsTotal: false),
            ["Q3"] = new(ScoreColumnType.Numeric, IsAggregateComponent: true, IsTotal: false),
            ["Extra"] = new(ScoreColumnType.Numeric, IsAggregateComponent: false, IsTotal: false),
            ["Total"] = new(ScoreColumnType.Numeric, IsAggregateComponent: false, IsTotal: true),
        };
        return (series, meta);
    }

    private List<CorrelationDataPoint> Generate(
        List<(string, Dictionary<string, double>)> series,
        Dictionary<string, CorrelationColumnInfo> meta)
    {
        var service = new CorrelationPlotService(_fixture.Env);
        var muppets = Rows.ToDictionary(r => r.Id, r => $"m{r.Id}");
        var (_, points) = service.GeneratePlot((7.0, 7.0), series, meta, muppets);
        return points;
    }

    [Fact]
    public void TotalVsComponentCell_UsesRestScore_AsTheTotalAxisValue()
    {
        var (series, meta) = BuildPayload();
        var points = Generate(series, meta);

        // The Q1 × Total cell: Total is the y-axis, so its stored y_value must
        // be the rest score Total − Q1 = Q2 + Q3, per student.
        var cell = points.Where(p =>
            (p.XSeries == "Q1" && p.YSeries == "Total")).ToList();
        Assert.Equal(Rows.Length, cell.Count);

        foreach (var p in cell)
        {
            var row = Rows.Single(r => $"S{r.Id:D3}" == $"S{p.StudentId:D3}");
            var expectedRest = row.Q2 + row.Q3;          // Total − Q1
            Assert.Equal(expectedRest, p.YValue, precision: 6);
            Assert.Equal(row.Q1, p.XValue, precision: 6); // component axis untouched
        }
    }

    [Fact]
    public void ComponentVsComponentCell_NotCorrected()
    {
        var (series, meta) = BuildPayload();
        var points = Generate(series, meta);

        // Q1 × Q2: neither axis is Total → both axes carry original values.
        var cell = points.Where(p => p.XSeries == "Q1" && p.YSeries == "Q2").ToList();
        Assert.Equal(Rows.Length, cell.Count);
        foreach (var p in cell)
        {
            var row = Rows.Single(r => r.Id == p.StudentId);
            Assert.Equal(row.Q1, p.XValue, precision: 6);
            Assert.Equal(row.Q2, p.YValue, precision: 6);
        }
    }

    [Fact]
    public void TotalVsNonComponentCell_NotCorrected()
    {
        var (series, meta) = BuildPayload();
        var points = Generate(series, meta);

        // Extra × Total: Extra is not an aggregate component, so Total is NOT
        // de-biased — the y_value stays the full Total.
        var cell = points.Where(p => p.XSeries == "Extra" && p.YSeries == "Total").ToList();
        Assert.Equal(Rows.Length, cell.Count);
        foreach (var p in cell)
        {
            var row = Rows.Single(r => r.Id == p.StudentId);
            Assert.Equal(row.Q1 + row.Q2 + row.Q3, p.YValue, precision: 6); // full Total
            Assert.Equal(row.Extra, p.XValue, precision: 6);
        }
    }

    [Fact]
    public void SingleComponentTotal_RestScoreDegenerate_BlanksTheCell()
    {
        // Total = Q1 alone → Total − Q1 ≡ 0 (zero variance) → r undefined.
        var series = new List<(string, Dictionary<string, double>)>
        {
            ("Q1", Col(r => r.Q1)),
            ("Total", Col(r => r.Q1)),  // Total IS Q1
        };
        var meta = new Dictionary<string, CorrelationColumnInfo>
        {
            ["Q1"] = new(ScoreColumnType.Numeric, IsAggregateComponent: true, IsTotal: false),
            ["Total"] = new(ScoreColumnType.Numeric, IsAggregateComponent: false, IsTotal: true),
        };

        var points = Generate(series, meta);

        // The only lower-triangle cell is Q1 × Total, which must be blanked →
        // no points at all.
        Assert.Empty(points);
    }
}
