using System.Text.Json.Serialization;

namespace Dotsesses.Models;

/// <summary>
/// Root object for saved application state.
/// </summary>
public class SavedState
{
    public int Version { get; set; } = 6;
    public DateTime SavedAt { get; set; }
    public string? SourceFile { get; set; }
    public List<SavedStudent> Students { get; set; } = new();
    public List<SavedCursor> Cursors { get; set; } = new();
    public List<SavedScoreSelection> ScoreSelections { get; set; } = new();

    /// <summary>
    /// The Significance Matrix's per-cell omnibus test family (ADR-0014
    /// slice 4). Defaults to <see cref="SignificanceTestFamily.Parametric"/>
    /// so v4 files (which lack this field) silently migrate to the parametric
    /// default on load, per the ADR-0002 first-save-rewrites pattern.
    /// </summary>
    public SignificanceTestFamily SignificanceTestFamily { get; set; }
        = SignificanceTestFamily.Parametric;
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
/// Saved student attribute. The <c>Comment</c> field is null on v2 files (which
/// pre-date the Numeric↔Categorical switch); v3 files written after slice 2 of
/// ADR-0013 may carry per-cell comments preserved across a type conversion. The
/// <c>SortOrder</c> field is null on pre-v6 files (which pre-date ADR-0017's
/// <c>~N</c> sort-order suffix); it migrates cleanly as null per the ADR-0002
/// first-save-rewrites pattern.
/// </summary>
public class SavedAttribute
{
    public string Name { get; set; } = string.Empty;
    public int? Index { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public int? SortOrder { get; set; }
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

/// <summary>
/// Persistence DTO mirror of <see cref="ScoreSelection"/>. The <c>Type</c> field defaults
/// to <see cref="ScoreColumnType.Numeric"/> so v2 files load cleanly into v3 code per
/// ADR-0002 (first save-after-load silently rewrites at the current version).
/// </summary>
public class SavedScoreSelection
{
    public string Name { get; set; } = string.Empty;
    public int? Index { get; set; }
    public ScoreColumnType Type { get; set; } = ScoreColumnType.Numeric;
    public bool Display { get; set; }
    public bool Aggregate { get; set; }
    public bool Correlation { get; set; }
    /// <summary>
    /// Whether the column participates in the Significance Matrix plot.
    /// Defaults to <c>true</c> so v3 files migrate cleanly per ADR-0002
    /// (Significance Matrix is a new plot — old files should show it by default).
    /// </summary>
    public bool Significance { get; set; } = true;
}
