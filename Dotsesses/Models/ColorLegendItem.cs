namespace Dotsesses.Models;

/// <summary>
/// Represents a color legend item showing an attribute value and its associated color.
/// </summary>
/// <param name="Value">The attribute value (e.g., "Yes", "✓✓+")</param>
/// <param name="Color">The color as a hex string (e.g., "#00FF00" for green)</param>
public record ColorLegendItem(string Value, string Color);
