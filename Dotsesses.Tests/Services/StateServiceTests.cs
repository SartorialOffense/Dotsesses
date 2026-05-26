namespace Dotsesses.Tests.Services;

using System.Text.Json;
using Dotsesses.Models;
using Dotsesses.Services;
using Dotsesses.Tests.Fixtures;
using Dotsesses.UI;

public class StateServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly StateService _stateService;

    public StateServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"StateServiceTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _stateService = new StateService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_CreatesJsonFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test_state.json");
        var students = CreateTestStudents();
        var session = TestFixtures.SessionForGrading();

        // Act
        await _stateService.SaveAsync(filePath, students, session, Array.Empty<ScoreSelection>(), "test_source.xlsx");

        // Assert
        Assert.True(File.Exists(filePath));
        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"version\":", json);
        Assert.Contains("\"students\":", json);
        Assert.Contains("\"cursors\":", json);
    }

    [Fact]
    public async Task SaveAsync_UpdatesLastUsedDirectory()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test_state.json");
        var students = CreateTestStudents();
        var session = TestFixtures.SessionForGrading();

        // Act
        await _stateService.SaveAsync(filePath, students, session, Array.Empty<ScoreSelection>());

        // Assert
        Assert.Equal(_testDirectory, _stateService.LastUsedDirectory);
    }

    [Fact]
    public async Task LoadAsync_ReturnsCorrectState()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test_state.json");
        var students = CreateTestStudents();
        var session = TestFixtures.SessionForGrading();
        await _stateService.SaveAsync(filePath, students, session, Array.Empty<ScoreSelection>(), "original_source.xlsx");

        // Act
        var loadedState = await _stateService.LoadAsync(filePath);

        // Assert
        Assert.NotNull(loadedState);
        Assert.Equal(4, loadedState.Version);
        Assert.Equal("original_source.xlsx", loadedState.SourceFile);
        Assert.Equal(2, loadedState.Students.Count);
        // Slots are A, B, C (post-#18 fix: catch-all is F, lowest in
        // DefaultCurve). SaveAsync iterates Slots and appends the
        // catch-all → 4 cursor entries total.
        Assert.Equal(4, loadedState.Cursors.Count);
    }

    [Fact]
    public async Task LoadAsync_UpdatesLastUsedDirectory()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test_state.json");
        var students = CreateTestStudents();
        var session = TestFixtures.SessionForGrading();
        await _stateService.SaveAsync(filePath, students, session, Array.Empty<ScoreSelection>());
        _stateService.LastUsedDirectory = null; // Reset

        // Act
        await _stateService.LoadAsync(filePath);

        // Assert
        Assert.Equal(_testDirectory, _stateService.LastUsedDirectory);
    }

    [Fact]
    public async Task RoundTrip_PreservesStudentData()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "roundtrip.json");
        var originalStudents = CreateTestStudents();
        originalStudents[0].Scores[0].Comment = "This is a test comment";
        var session = TestFixtures.SessionForGrading();

        // Act - Save then load
        await _stateService.SaveAsync(filePath, originalStudents, session, Array.Empty<ScoreSelection>());
        var loadedState = await _stateService.LoadAsync(filePath);
        var (students, muppetMap) = _stateService.ConvertToStudents(loadedState);

        // Assert
        Assert.Equal(originalStudents.Count, students.Count);
        Assert.Equal(originalStudents[0].Id, students[0].Id);
        Assert.Equal(originalStudents[0].MuppetName, students[0].MuppetName);
        Assert.Equal(originalStudents[0].Scores.Count, students[0].Scores.Count);
        Assert.Equal("This is a test comment", students[0].Scores[0].Comment);
    }

    [Fact]
    public async Task RoundTrip_PreservesCursorData()
    {
        // Arrange — TestFixtures.SessionForGrading defaults: A=450, B=250, C=50 (catch-all).
        // Move A to 275 and disable B; verify both round-trip through the saved JSON.
        var filePath = Path.Combine(_testDirectory, "cursor_roundtrip.json");
        var students = CreateTestStudents();
        var session = TestFixtures.SessionForGrading();
        session.MoveCutoff(TestFixtures.GradeA, 275, originator: this);
        session.DisableGrade(TestFixtures.GradeB);

        // Act
        await _stateService.SaveAsync(filePath, students, session, Array.Empty<ScoreSelection>());
        var loadedState = await _stateService.LoadAsync(filePath);

        // Assert — A's new score landed; B is recorded as disabled.
        Assert.Equal(275, loadedState.Cursors.First(c => c.Grade == "A").Score);
        Assert.False(loadedState.Cursors.First(c => c.Grade == "B").Enabled);
    }

    [Fact]
    public async Task RoundTrip_PreservesGradingSessionState_ViaConvertToGradingState()
    {
        // Save a session, load the SavedState, hydrate a fresh session via
        // ConvertToGradingState + LoadCutoffs, and verify slot scores and
        // EnabledGrades match the original.
        var filePath = Path.Combine(_testDirectory, "session_roundtrip.json");
        var students = CreateTestStudents();
        var original = TestFixtures.SessionForGrading();
        original.MoveCutoff(TestFixtures.GradeA, 400, originator: this);
        original.DisableGrade(TestFixtures.GradeB);

        await _stateService.SaveAsync(filePath, students, original, Array.Empty<ScoreSelection>());
        var loaded = await _stateService.LoadAsync(filePath);

        var fresh = TestFixtures.SessionForGrading();
        var (cutoffs, enabledGrades) = _stateService.ConvertToGradingState(loaded, fresh);
        fresh.LoadCutoffs(cutoffs, enabledGrades);

        Assert.Equal(400, fresh.Slots.First(s => s.Grade.LetterGrade == LetterGrade.A).Score);
        Assert.False(fresh.Slots.First(s => s.Grade.LetterGrade == LetterGrade.B).IsEnabled);
        Assert.DoesNotContain(TestFixtures.GradeB, fresh.CurrentState.EnabledGrades);
    }

    [Fact]
    public void ConvertToStudents_CreatesMuppetMap()
    {
        // Arrange
        var state = new SavedState
        {
            Students = new List<SavedStudent>
            {
                new() { Id = 1, MuppetName = "Kermit the Frog" },
                new() { Id = 2, MuppetName = "Miss Piggy" }
            }
        };

        // Act
        var (students, muppetMap) = _stateService.ConvertToStudents(state);

        // Assert
        Assert.Equal(2, muppetMap.Count);
        Assert.Equal("Kermit the Frog", muppetMap[1].Name);
        Assert.Equal("Miss Piggy", muppetMap[2].Name);
    }

    [Fact]
    public async Task LoadAsync_WithInvalidFile_ThrowsException()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "nonexistent.json");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _stateService.LoadAsync(filePath));
    }

    [Fact]
    public async Task LoadAsync_WithInvalidJson_ThrowsException()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "invalid.json");
        await File.WriteAllTextAsync(filePath, "not valid json {{{");

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(() => _stateService.LoadAsync(filePath));
    }

    [Fact]
    public async Task SaveAsync_WritesVersion4()
    {
        var filePath = Path.Combine(_testDirectory, "v4_check.json");
        await _stateService.SaveAsync(filePath, CreateTestStudents(), TestFixtures.SessionForGrading(), Array.Empty<ScoreSelection>());
        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"version\": 4", json);
    }

    [Fact]
    public async Task RoundTrip_PreservesScoreSelections()
    {
        var filePath = Path.Combine(_testDirectory, "selection_roundtrip.json");
        var originalSelections = CreateTestScoreSelections();
        await _stateService.SaveAsync(filePath, CreateTestStudents(), TestFixtures.SessionForGrading(), originalSelections);
        var loadedState = await _stateService.LoadAsync(filePath);
        var converted = _stateService.ConvertToScoreSelections(loadedState);
        Assert.Equal(originalSelections.Count, converted.Count);
        for (int i = 0; i < originalSelections.Count; i++)
        {
            Assert.Equal(originalSelections[i].Name,        converted[i].Name);
            Assert.Equal(originalSelections[i].Index,       converted[i].Index);
            Assert.Equal(originalSelections[i].Type,        converted[i].Type);
            Assert.Equal(originalSelections[i].Display,     converted[i].Display);
            Assert.Equal(originalSelections[i].Aggregate,   converted[i].Aggregate);
            Assert.Equal(originalSelections[i].Correlation, converted[i].Correlation);
        }
    }

    [Fact]
    public async Task RoundTrip_PreservesCategoricalScoreSelectionType()
    {
        var filePath = Path.Combine(_testDirectory, "categorical_roundtrip.json");
        var originalSelections = new List<ScoreSelection>
        {
            new("Q", 1, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true),
            new("Submitted Outline", null, ScoreColumnType.Categorical, Display: true, Aggregate: false, Correlation: false),
        };
        await _stateService.SaveAsync(filePath, CreateTestStudents(), TestFixtures.SessionForGrading(), originalSelections);

        var converted = _stateService.ConvertToScoreSelections(await _stateService.LoadAsync(filePath));

        Assert.Equal(ScoreColumnType.Numeric, converted.First(s => s.Name == "Q").Type);
        Assert.Equal(ScoreColumnType.Categorical, converted.First(s => s.Name == "Submitted Outline").Type);
    }

    [Fact]
    public async Task RoundTrip_PreservesAttributeComment()
    {
        // Slice 2 of ADR-0013: StudentAttribute gained an optional Comment so per-cell
        // comments survive a Numeric→Categorical conversion. Persist + reload must
        // round-trip that Comment.
        var filePath = Path.Combine(_testDirectory, "attribute_comment_roundtrip.json");
        var students = new List<StudentAssessment>
        {
            new(1,
                new List<Score> { new("Total", null, 80) },
                new List<StudentAttribute> { new("Outline", null, "Yes", "explanatory note") },
                "Test"),
        };
        await _stateService.SaveAsync(filePath, students, TestFixtures.SessionForGrading(), Array.Empty<ScoreSelection>());

        var loaded = await _stateService.LoadAsync(filePath);
        var (round, _) = _stateService.ConvertToStudents(loaded);

        var attr = round.Single().Attributes.Single();
        Assert.Equal("Yes", attr.Value);
        Assert.Equal("explanatory note", attr.Comment);
    }

    [Fact]
    public async Task RoundTrip_PreservesSignificance()
    {
        var filePath = Path.Combine(_testDirectory, "significance_roundtrip.json");
        var originalSelections = new List<ScoreSelection>
        {
            new("Q", 1, ScoreColumnType.Numeric, Display: true, Aggregate: true, Correlation: true, Significance: true),
            new("Cat", null, ScoreColumnType.Categorical, Display: true, Aggregate: false, Correlation: false, Significance: false),
        };
        await _stateService.SaveAsync(filePath, CreateTestStudents(), TestFixtures.SessionForGrading(), originalSelections);

        var converted = _stateService.ConvertToScoreSelections(await _stateService.LoadAsync(filePath));

        Assert.True(converted.First(s => s.Name == "Q").Significance);
        Assert.False(converted.First(s => s.Name == "Cat").Significance);
    }

    [Fact]
    public async Task LoadAsync_V3File_DefaultsSignificanceToTrue()
    {
        // Slice 3 / ADR-0014: v3 files pre-date the Significance field, so the loader
        // fills in Significance=true (so the new Significance Matrix isn't blank on
        // first load of an existing project file).
        var filePath = Path.Combine(_testDirectory, "v3.dots");
        // type encoded as the integer enum value (0=Numeric, 1=Categorical) — same
        // convention as the live SaveAsync output (no string-enum converter is configured).
        var v3Content = """
            {
              "version": 3,
              "savedAt": "2024-01-01T00:00:00Z",
              "students": [],
              "cursors": [],
              "scoreSelections": [
                { "name": "Q", "index": 1, "type": 0, "display": true, "aggregate": true, "correlation": true },
                { "name": "Hat", "index": null, "type": 1, "display": true, "aggregate": false, "correlation": false }
              ]
            }
            """;
        await File.WriteAllTextAsync(filePath, v3Content);

        var loaded = await _stateService.LoadAsync(filePath);
        var converted = _stateService.ConvertToScoreSelections(loaded);

        Assert.Equal(3, loaded.Version);
        Assert.All(converted, s => Assert.True(s.Significance));
    }

    [Fact]
    public async Task LoadAsync_V2File_DefaultsScoreSelectionTypeToNumeric()
    {
        // Forward-compatible migration per ADR-0002: a v2 file pre-dates the Type
        // field, so the loader fills in Numeric for every saved selection.
        var filePath = Path.Combine(_testDirectory, "v2.dots");
        var v2Content = """
            {
              "version": 2,
              "savedAt": "2024-01-01T00:00:00Z",
              "students": [],
              "cursors": [],
              "scoreSelections": [
                { "name": "Q", "index": 1, "display": true, "aggregate": true, "correlation": true },
                { "name": "Total", "index": null, "display": true, "aggregate": false, "correlation": true }
              ]
            }
            """;
        await File.WriteAllTextAsync(filePath, v2Content);

        var loaded = await _stateService.LoadAsync(filePath);
        var converted = _stateService.ConvertToScoreSelections(loaded);

        Assert.Equal(2, loaded.Version);
        Assert.All(converted, s => Assert.Equal(ScoreColumnType.Numeric, s.Type));
    }

    [Fact]
    public async Task LoadAsync_V1Json_ThrowsForUnsupportedVersion()
    {
        var filePath = Path.Combine(_testDirectory, "v1.dots");
        var v1Content = """{"version": 1, "savedAt": "2024-01-01T00:00:00Z", "students": [], "cursors": []}""";
        await File.WriteAllTextAsync(filePath, v1Content);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _stateService.LoadAsync(filePath));
        Assert.Contains("v1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    private static List<ScoreSelection> CreateTestScoreSelections()
    {
        return new List<ScoreSelection>
        {
            new ScoreSelection("Total", null, ScoreColumnType.Numeric, Display: true,  Aggregate: false, Correlation: true),
            new ScoreSelection("Q",     1,    ScoreColumnType.Numeric, Display: true,  Aggregate: true,  Correlation: true),
            new ScoreSelection("Q",     2,    ScoreColumnType.Numeric, Display: false, Aggregate: true,  Correlation: false)
        };
    }

    private List<StudentAssessment> CreateTestStudents()
    {
        return new List<StudentAssessment>
        {
            new StudentAssessment(
                1,
                new List<Score>
                {
                    new Score("Total", null, 285),
                    new Score("Q", 1, 45)
                },
                new List<StudentAttribute>
                {
                    new StudentAttribute("Section", null, "A")
                },
                "Test Muppet 1"),
            new StudentAssessment(
                2,
                new List<Score>
                {
                    new Score("Total", null, 265),
                    new Score("Q", 1, 40)
                },
                new List<StudentAttribute>
                {
                    new StudentAttribute("Section", null, "B")
                },
                "Test Muppet 2")
        };
    }

}
