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
    /// Saves the current state to a JSON file.
    /// </summary>
    public async Task SaveAsync(
        string filePath,
        IEnumerable<StudentAssessment> students,
        IEnumerable<CursorViewModel> cursors,
        string? sourceFile = null)
    {
        var state = new SavedState
        {
            Version = 1,
            SavedAt = DateTime.UtcNow,
            SourceFile = sourceFile,
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
                    Value = a.Value
                }).ToList()
            }).ToList(),
            Cursors = cursors.Select(c => new SavedCursor
            {
                Grade = c.Grade.DisplayName,
                Score = c.Score,
                Enabled = c.IsEnabled
            }).ToList()
        };

        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        LastUsedDirectory = Path.GetDirectoryName(filePath);
    }

    /// <summary>
    /// Loads state from a JSON file.
    /// </summary>
    public async Task<SavedState> LoadAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var state = JsonSerializer.Deserialize<SavedState>(json, JsonOptions);

        if (state == null)
        {
            throw new InvalidOperationException("Failed to deserialize state file.");
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
                new StudentAttribute(a.Name, a.Index, a.Value)).ToList();

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
    /// Applies saved cursor positions to existing cursor ViewModels.
    /// </summary>
    public void ApplyCursors(SavedState state, IEnumerable<CursorViewModel> cursors)
    {
        var cursorList = cursors.ToList();

        foreach (var savedCursor in state.Cursors)
        {
            var cursor = cursorList.FirstOrDefault(c =>
                c.Grade.DisplayName == savedCursor.Grade);

            if (cursor != null)
            {
                cursor.Score = savedCursor.Score;
                cursor.IsEnabled = savedCursor.Enabled;
            }
        }
    }
}
