namespace Dotsesses.Models;

/// <summary>
/// Represents a non-numeric student attribute. Carries an optional Comment so that
/// per-cell comments survive a Numeric→Categorical type switch and reappear on a
/// Categorical→Numeric reverse (see ADR-0013). ADR-0007 still applies — comments are
/// per-column-per-student data, not a single Student-level field.
/// </summary>
/// <param name="Name">Attribute name (e.g., "Submitted Outline", "Mid-Term")</param>
/// <param name="Index">Optional index for multiple attributes of same type</param>
/// <param name="Value">Attribute value (e.g., "Yes", "No", "✔✔+")</param>
/// <param name="Comment">Optional comment carried over from a converted <see cref="Score"/>, or null otherwise.</param>
public record StudentAttribute(string Name, int? Index, string Value, string? Comment = null);
