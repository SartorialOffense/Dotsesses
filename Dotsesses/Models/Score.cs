namespace Dotsesses.Models;

/// <summary>
/// Represents an individual numeric score component.
/// </summary>
/// <param name="Name">Score name (e.g., "Quiz", "Final")</param>
/// <param name="Index">Optional index for multiple scores of same type (e.g., Quiz 1, Quiz 2)</param>
/// <param name="Value">Numeric score value</param>
public record Score(string Name, int? Index, double Value);

