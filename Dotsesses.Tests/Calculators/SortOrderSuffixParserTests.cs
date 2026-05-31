namespace Dotsesses.Tests.Calculators;

using Dotsesses.Calculators;
using Xunit;

/// <summary>
/// Covers the <c>~N</c> sort-order suffix grammar (ADR-0017): end-anchored,
/// whitespace-tolerant, last-~-wins, non-negative ints, empty-label guarded.
/// </summary>
public class SortOrderSuffixParserTests
{
    [Theory]
    [InlineData("Pass~2", "Pass", 2)]
    [InlineData("Fail~1", "Fail", 1)]
    [InlineData("✔✔+~3", "✔✔+", 3)]
    [InlineData("Low~0", "Low", 0)]               // zero is a valid non-negative order
    public void Parse_BasicSuffix(string value, string? expectedLabel, int? expectedOrder)
    {
        var (label, order) = SortOrderSuffixParser.Parse(value);
        Assert.Equal(expectedLabel ?? value, label);
        Assert.Equal(expectedOrder, order);
    }

    [Fact]
    public void Parse_LeadingZeros_ParseAsInt()
    {
        var (label, order) = SortOrderSuffixParser.Parse("Honors~03");
        Assert.Equal("Honors", label);
        Assert.Equal(3, order);
    }

    [Theory]
    [InlineData("Pass ~ 2")]   // spaces around the tilde
    [InlineData("Pass~ 2")]
    [InlineData("Pass ~2")]
    [InlineData("  Pass ~ 2  ")] // plus outer whitespace
    public void Parse_WhitespaceTolerant(string value)
    {
        var (label, order) = SortOrderSuffixParser.Parse(value);
        Assert.Equal("Pass", label);
        Assert.Equal(2, order);
    }

    [Fact]
    public void Parse_LastTildeWins()
    {
        var (label, order) = SortOrderSuffixParser.Parse("A~1~2");
        Assert.Equal("A~1", label);
        Assert.Equal(2, order);
    }

    [Theory]
    [InlineData("Top~Tier")]   // non-numeric tail is not a suffix
    [InlineData("Plain")]      // no tilde at all
    [InlineData("No~")]        // tilde but no digits
    public void Parse_NoValidSuffix_ReturnsTrimmedValueAndNull(string value)
    {
        var (label, order) = SortOrderSuffixParser.Parse(value);
        Assert.Equal(value.Trim(), label);
        Assert.Null(order);
    }

    [Fact]
    public void Parse_EmptyLabel_IsGuarded()
    {
        // A bare "~2" would strip to an empty label — keep the raw value instead.
        var (label, order) = SortOrderSuffixParser.Parse("~2");
        Assert.Equal("~2", label);
        Assert.Null(order);
    }

    [Fact]
    public void Parse_OnlyWhitespaceBeforeTilde_IsGuarded()
    {
        var (label, order) = SortOrderSuffixParser.Parse("   ~2");
        Assert.Equal("~2", label);
        Assert.Null(order);
    }

    [Fact]
    public void Parse_TrimsPlainValueWithoutSuffix()
    {
        var (label, order) = SortOrderSuffixParser.Parse("  Yes  ");
        Assert.Equal("Yes", label);
        Assert.Null(order);
    }
}
