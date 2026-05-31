using System.Collections.Generic;
using CSnakes.Runtime;
using Dotsesses.Tests.Python;

namespace Dotsesses.Tests.Python;

/// <summary>
/// Proves the <see cref="PythonEnvironmentFixture"/> can initialise the CSnakes
/// runtime and round-trip a call into an existing module. If this fails, no
/// other Python-interop test can be trusted — fix the fixture first.
/// </summary>
[Collection(PythonCollection.Name)]
public class PythonInteropSmokeTests
{
    private readonly PythonEnvironmentFixture _fixture;

    public PythonInteropSmokeTests(PythonEnvironmentFixture fixture) => _fixture = fixture;

    [Fact]
    public void ComputeSignificancePvalues_TwoCleanlySeparatedGroups_ReturnsSignificantCell()
    {
        var module = _fixture.Env.SignificanceMatrix();

        // One numeric column, one categorical column. Group "A" sits well below
        // group "B", so the omnibus test should report a small (non-NaN) p.
        var numeric = new List<(string, IReadOnlyDictionary<string, double>)>
        {
            ("Score", new Dictionary<string, double>
            {
                ["S1"] = 10, ["S2"] = 11, ["S3"] = 12,
                ["S4"] = 50, ["S5"] = 51, ["S6"] = 52,
            }),
        };
        var categorical = new List<(string, IReadOnlyDictionary<string, string>)>
        {
            ("Group", new Dictionary<string, string>
            {
                ["S1"] = "A", ["S2"] = "A", ["S3"] = "A",
                ["S4"] = "B", ["S5"] = "B", ["S6"] = "B",
            }),
        };

        var rows = module.ComputeSignificancePvalues(numeric, categorical, "parametric");

        Assert.Single(rows);
        var d = rows[0].As<IReadOnlyDictionary<string, CSnakes.Runtime.Python.PyObject>>();
        Assert.Equal("Score", d["num"].As<string>());
        Assert.Equal("Group", d["cat"].As<string>());
        var p = d["p"].As<double>();
        Assert.False(double.IsNaN(p), "two well-separated groups should be testable");
        Assert.True(p < 0.05, $"expected a significant p, got {p}");
    }
}
