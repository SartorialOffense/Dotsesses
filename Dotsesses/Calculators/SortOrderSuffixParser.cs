namespace Dotsesses.Calculators;

using System.Globalization;
using System.Linq;

/// <summary>
/// Decodes the optional <c>~N</c> sort-order suffix on a categorical cell value
/// (ADR-0017). <c>Pass~2</c> → label <c>Pass</c>, order <c>2</c>.
///
/// Grammar:
/// <list type="bullet">
/// <item>End-anchored — only a trailing <c>~N</c> counts.</item>
/// <item>Whitespace-tolerant — the cell is trimmed and spaces around the
/// <c>~</c> are ignored (<c>Pass ~ 2</c> → <c>Pass</c>, 2).</item>
/// <item>Last-<c>~</c>-wins — <c>A~1~2</c> → label <c>A~1</c>, order 2.</item>
/// <item>Non-negative integers only (<c>[0-9]+</c>); leading zeros are fine
/// (<c>~03</c> → 3). A non-numeric tail (<c>Top~Tier</c>) is not a suffix.</item>
/// <item>Empty-label guard — a bare <c>~2</c> is treated as no suffix (the raw
/// value is kept and SortOrder is null) rather than producing a blank label.</item>
/// </list>
/// Pure function — no I/O, no allocation beyond the returned strings.
/// </summary>
public static class SortOrderSuffixParser
{
    /// <summary>
    /// Splits <paramref name="value"/> into its display label and optional
    /// sort order. When there is no valid suffix, returns the trimmed value
    /// and a null order.
    /// </summary>
    public static (string Label, int? SortOrder) Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var trimmed = value.Trim();

        var tildeIndex = trimmed.LastIndexOf('~');
        if (tildeIndex < 0)
        {
            return (trimmed, null);
        }

        var digits = trimmed[(tildeIndex + 1)..].Trim();
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return (trimmed, null);
        }

        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var order))
        {
            // Overflow of a very long digit run — treat as no suffix rather than throw.
            return (trimmed, null);
        }

        var label = trimmed[..tildeIndex].TrimEnd();
        if (label.Length == 0)
        {
            // Empty-label guard: keep the raw "~N" as a literal value.
            return (trimmed, null);
        }

        return (label, order);
    }
}
