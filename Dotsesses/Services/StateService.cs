using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dotsesses.Models;
using Dotsesses.UI;

namespace Dotsesses.Services;

/// <summary>
/// Service for saving and loading application state to/from JSON files.
/// </summary>
public class StateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Last directory used for save/load operations.
    /// </summary>
    public string? LastUsedDirectory { get; set; }

    /// <summary>
    /// Saves the current state to a JSON file. The GradingSession is the
    /// canonical source for cutoff and enabled-grades data; per-slot
    /// scores are persisted (including disabled slots) so a load
    /// round-trip restores the user's last-known positions.
    /// </summary>
    public async Task SaveAsync(
        string filePath,
        IEnumerable<StudentAssessment> students,
        GradingSession gradingSession,
        IEnumerable<ScoreSelection> selections,
        string? sourceFile = null,
        SignificanceTestFamily significanceTestFamily = SignificanceTestFamily.Parametric)
    {
        ArgumentNullException.ThrowIfNull(gradingSession);

        var slotGrades = gradingSession.Slots.Select(s => s.Grade).ToHashSet();

        var savedCursors = gradingSession.Slots
            .Select(slot => new SavedCursor
            {
                Grade = slot.Grade.DisplayName,
                Score = slot.Score,
                Enabled = slot.IsEnabled,
            })
            .Concat(gradingSession.CurrentState.Cutoffs
                .Where(c => !slotGrades.Contains(c.Grade))
                .Select(c => new SavedCursor
                {
                    Grade = c.Grade.DisplayName,
                    Score = c.Score,
                    Enabled = true,
                }))
            .ToList();

        var state = new SavedState
        {
            Version = 7,
            SavedAt = DateTime.UtcNow,
            SourceFile = sourceFile,
            SignificanceTestFamily = significanceTestFamily,
            Students = students.Select(s => new SavedStudent
            {
                Id = s.Id,
                MuppetName = s.MuppetName,
                Scores = s.Scores.Select(sc => new SavedScore
                {
                    Name = sc.Name,
                    Index = sc.Index,
                    Value = sc.Value,
                    Comment = sc.Comment
                }).ToList(),
                Attributes = s.Attributes.Select(a => new SavedAttribute
                {
                    Name = a.Name,
                    Index = a.Index,
                    Value = a.Value,
                    Comment = a.Comment,
                    SortOrder = a.SortOrder
                }).ToList()
            }).ToList(),
            Cursors = savedCursors,
            ScoreSelections = selections.Select(s => new SavedScoreSelection
            {
                Name = s.Name,
                Index = s.Index,
                Type = s.Type,
                Display = s.Display,
                Aggregate = s.Aggregate,
                Correlation = s.Correlation,
                Significance = s.Significance,
                BiasCorrect = s.BiasCorrect
            }).ToList()
        };

        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        LastUsedDirectory = Path.GetDirectoryName(filePath);
    }

    /// <summary>
    /// Loads state from a JSON file. Rejects v1 files per ADR-0009.
    /// </summary>
    public async Task<SavedState> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var state = JsonSerializer.Deserialize<SavedState>(json, JsonOptions);

        if (state == null)
        {
            throw new InvalidOperationException("Failed to deserialize state file.");
        }

        if (state.Version < 2)
        {
            throw new InvalidOperationException(
                $"This .dots file is v{state.Version}, which is no longer supported. " +
                "Re-load the original Excel file and save a fresh .dots file " +
                "(see ADR-0009).");
        }

        LastUsedDirectory = Path.GetDirectoryName(filePath);
        return state;
    }

    /// <summary>
    /// Converts saved state back to domain models.
    /// </summary>
    public (List<StudentAssessment> Students, Dictionary<int, MuppetNameInfo> MuppetMap)
        ConvertToStudents(SavedState state)
    {
        var students = new List<StudentAssessment>();
        var muppetMap = new Dictionary<int, MuppetNameInfo>();

        foreach (var saved in state.Students)
        {
            var scores = saved.Scores.Select(s =>
                new Score(s.Name, s.Index, s.Value, s.Comment)).ToList();

            var attributes = saved.Attributes.Select(a =>
                new StudentAttribute(a.Name, a.Index, a.Value, a.Comment, a.SortOrder)).ToList();

            var student = new StudentAssessment(
                saved.Id,
                scores,
                attributes,
                saved.MuppetName);

            students.Add(student);
            muppetMap[saved.Id] = new MuppetNameInfo(saved.MuppetName, "");
        }

        return (students, muppetMap);
    }

    /// <summary>
    /// Converts saved score selections back to domain models.
    ///
    /// BiasCorrect migration (ADR-0018, v7): a pre-v7 file lacks the field, so
    /// it deserializes to <c>null</c> — we then derive the old behavior, where
    /// de-bias was driven by aggregate membership (<c>Numeric &amp;&amp; Aggregate &amp;&amp;
    /// !Total</c>), so existing projects keep correcting exactly the same cells.
    /// A non-null value is an explicit v7 choice and is taken verbatim.
    /// </summary>
    public IReadOnlyList<ScoreSelection> ConvertToScoreSelections(SavedState state)
    {
        return state.ScoreSelections
            .Select(s => new ScoreSelection(
                s.Name, s.Index, s.Type, s.Display, s.Aggregate, s.Correlation, s.Significance,
                BiasCorrect: s.BiasCorrect ?? (
                    s.Type == ScoreColumnType.Numeric && s.Aggregate &&
                    !(s.Index == null && string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase)))))
            .ToList();
    }

    /// <summary>
    /// Converts a SavedState's cursor data into the (cutoffs, enabledGrades)
    /// pair that GradingSession.LoadCutoffs expects. The supplied
    /// <paramref name="session"/> provides the canonical Grade objects (so
    /// the string-keyed SavedCursor entries can be mapped back to
    /// strongly-typed Grade records).
    /// </summary>
    public (IReadOnlyList<GradeCutoff> Cutoffs, IReadOnlySet<Grade> EnabledGrades)
        ConvertToGradingState(SavedState state, GradingSession session)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(session);

        var allGrades = session.Slots
            .Select(s => s.Grade)
            .Concat(session.CurrentState.Cutoffs.Select(c => c.Grade))
            .Distinct()
            .ToDictionary(g => g.DisplayName);

        var cutoffs = state.Cursors
            .Where(sc => allGrades.ContainsKey(sc.Grade))
            .Select(sc => new GradeCutoff(allGrades[sc.Grade], sc.Score))
            .ToList();

        var enabledGrades = state.Cursors
            .Where(sc => sc.Enabled && allGrades.ContainsKey(sc.Grade))
            .Select(sc => allGrades[sc.Grade])
            .ToHashSet();

        return (cutoffs, enabledGrades);
    }

}
