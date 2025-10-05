namespace Dotsesses.Models;

/// <summary>
/// Represents a non-numeric student attribute.
/// </summary>
/// <param name="Name">Attribute name (e.g., "Submitted Outline", "Mid-Term")</param>
/// <param name="Index">Optional index for multiple attributes of same type</param>
/// <param name="Value">Attribute value (e.g., "Yes", "No", "✔✔+")</param>
public record StudentAttribute(string Name, int? Index, string Value);
