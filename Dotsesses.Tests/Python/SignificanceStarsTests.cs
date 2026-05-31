using CSnakes.Runtime;
using Dotsesses.Tests.Python;

namespace Dotsesses.Tests.Python;

/// <summary>
/// Slice 0 of ADR-0018: the shared <c>significance_stars</c> helper in
/// <c>stats_common.py</c> is the single source of truth for the star tiers on
/// both stats tabs. These assert the universal convention (.05/.01/.001) at the
/// boundaries, matching the inline output <c>significance_matrix.py</c> produced
/// before extraction.
/// </summary>
[Collection(PythonCollection.Name)]
public class SignificanceStarsTests
{
    private readonly PythonEnvironmentFixture _fixture;

    public SignificanceStarsTests(PythonEnvironmentFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData(0.0005, "***")]  // below .001
    [InlineData(0.001, "**")]    // exactly .001 → not < .001, but < .01
    [InlineData(0.005, "**")]
    [InlineData(0.01, "*")]      // exactly .01 → not < .01, but < .05
    [InlineData(0.03, "*")]
    [InlineData(0.05, "")]       // exactly .05 → not significant
    [InlineData(0.2, "")]
    [InlineData(0.999, "")]
    public void SignificanceStars_AtTierBoundaries_MatchesUniversalConvention(
        double p, string expected)
    {
        var stars = _fixture.Env.StatsCommon().SignificanceStars(p);
        Assert.Equal(expected, stars);
    }

    [Fact]
    public void SignificanceStars_NaN_ReturnsEmpty()
    {
        var stars = _fixture.Env.StatsCommon().SignificanceStars(double.NaN);
        Assert.Equal("", stars);
    }
}
