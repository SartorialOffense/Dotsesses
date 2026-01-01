using System.Text.Json.Serialization;

namespace Dotsesses.Models;

/// <summary>
/// Root object for saved application state.
/// </summary>
public class SavedState
{
    public int Version { get; set; } = 1;
    public DateTime SavedAt { get; set; }
    public string? SourceFile { get; set; }
    public List<SavedStudent> Students { get; set; } = new();
    public List<SavedCursor> Cursors { get; set; } = new();
}

/// <summary>
/// Saved student data including scores and attributes.
/// </summary>
public class SavedStudent
{
    public int Id { get; set; }
    public string MuppetName { get; set; } = string.Empty;
    public List<SavedScore> Scores { get; set; } = new();
    public List<SavedAttribute> Attributes { get; set; } = new();
}

/// <summary>
/// Saved score with optional comment.
/// </summary>
public class SavedScore
{
    public string Name { get; set; } = string.Empty;
    public int? Index { get; set; }
    public double Value { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// Saved student attribute.
/// </summary>
public class SavedAttribute
{
    public string Name { get; set; } = string.Empty;
    public int? Index { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Saved cursor position for a grade.
/// </summary>
public class SavedCursor
{
    public string Grade { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool Enabled { get; set; } = true;
}
