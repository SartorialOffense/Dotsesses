namespace Dotsesses.Models;

/// <summary>
/// Contains the whimsical name and emojis for a student.
/// </summary>
/// <param name="Name">Muppet character name from Muppet Wiki</param>
/// <param name="Emojis">1-3 random emojis associated with this student</param>
public record MuppetNameInfo(string Name, string Emojis)
{
    /// <summary>
    /// Returns the full display name with emojis.
    /// </summary>
    public string DisplayName => $"{Name} {Emojis}";
}
