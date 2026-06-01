using System.Collections.Generic;
using System.Linq;
using Dotsesses.Models;
using Dotsesses.Services;
using Dotsesses.Tests.Python;

namespace Dotsesses.Tests.Python;

/// <summary>
/// ADR-0019 — the Significance Matrix cell shows a box + jittered individual
/// student points per subgroup (replacing the mean ± SEM dot). Verified through
/// the real Python module via CSnakes: the returned points are now ONE PER
/// STUDENT (each with its own score), the box doesn't corrupt the point
/// extraction, and the CI toggle renders either way.
/// </summary>
[Collection(PythonCollection.Name)]
public class SignificanceBoxJitterTests
{
    private readonly PythonEnvironmentFixture _fixture;

    public SignificanceBoxJitterTests(PythonEnvironmentFixture fixture) => _fixture = fixture;

    // One numeric × one categorical cell. A = {S1:10, S2:20, S3:30}, B = {S4:40, S5:50}.
    private static (List<(string, Dictionary<string, double>)> Num,
                    List<(string, Dictionary<string, string>)> Cat) Cell()
    {
        var num = new List<(string, Dictionary<string, double>)>
        {
            ("Score", new Dictionary<string, double>
            {
                ["S1"] = 10, ["S2"] = 20, ["S3"] = 30, ["S4"] = 40, ["S5"] = 50,
            }),
        };
        var cat = new List<(string, Dictionary<string, string>)>
        {
            ("Group", new Dictionary<string, string>
            {
                ["S1"] = "A", ["S2"] = "A", ["S3"] = "A", ["S4"] = "B", ["S5"] = "B",
            }),
        };
        return (num, cat);
    }

    private List<SignificanceDataPoint> Generate(bool showCi)
    {
        var (num, cat) = Cell();
        var service = new SignificancePlotService(_fixture.Env);
        var (_, points) = service.GeneratePlot((6.0, 6.0), num, cat, 5.0,
            ThemeName.DarkMode, SignificanceTestFamily.Parametric, subgroupOrders: null, showCi: showCi);
        return points;
    }

    [Fact]
    public void ReturnsOnePointPerStudent_CarryingItsOwnScoreAndSubgroup()
    {
        var points = Generate(showCi: false);

        // One point per student that has both values (5), not one per subgroup (2).
        Assert.Equal(5, points.Count);

        var expectedScore = new Dictionary<int, double>
        {
            [1] = 10, [2] = 20, [3] = 30, [4] = 40, [5] = 50,
        };
        var expectedSubgroup = new Dictionary<int, string>
        {
            [1] = "A", [2] = "A", [3] = "A", [4] = "B", [5] = "B",
        };
        foreach (var p in points)
        {
            Assert.Equal(expectedScore[p.StudentId], p.Value, precision: 6);
            Assert.Equal(expectedSubgroup[p.StudentId], p.Subgroup);
        }

        // N is the student's subgroup size (A=3, B=2).
        Assert.All(points.Where(p => p.Subgroup == "A"), p => Assert.Equal(3, p.N));
        Assert.All(points.Where(p => p.Subgroup == "B"), p => Assert.Equal(2, p.N));
    }

    [Fact]
    public void CiToggle_RendersTheSamePoints_EitherWay()
    {
        // The CI overlay is decoration drawn with plain lines (no markers), so it
        // must not add/remove extracted student points.
        var off = Generate(showCi: false);
        var on = Generate(showCi: true);

        Assert.Equal(5, off.Count);
        Assert.Equal(5, on.Count);
    }

    [Fact]
    public void EveryPoint_CarriesCellLevelEffectSizeAndP()
    {
        // Cell-level inference is repeated on each student point (unchanged from
        // the per-subgroup model) so the η²/p annotation + any consumer still work.
        var points = Generate(showCi: false);

        Assert.All(points, p =>
        {
            Assert.NotNull(p.EffectSize);
            Assert.NotNull(p.PValue);
        });
    }
}
