namespace Dotsesses.Tests.Services;

using System.Text.Json;
using Dotsesses.Models;
using Dotsesses.Services;
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
        var cursors = CreateTestCursors();

        // Act
        await _stateService.SaveAsync(filePath, students, cursors, "test_source.xlsx");

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
        var cursors = CreateTestCursors();

        // Act
        await _stateService.SaveAsync(filePath, students, cursors);

        // Assert
        Assert.Equal(_testDirectory, _stateService.LastUsedDirectory);
    }

    [Fact]
    public async Task LoadAsync_ReturnsCorrectState()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test_state.json");
        var students = CreateTestStudents();
        var cursors = CreateTestCursors();
        await _stateService.SaveAsync(filePath, students, cursors, "original_source.xlsx");

        // Act
        var loadedState = await _stateService.LoadAsync(filePath);

        // Assert
        Assert.NotNull(loadedState);
        Assert.Equal(1, loadedState.Version);
        Assert.Equal("original_source.xlsx", loadedState.SourceFile);
        Assert.Equal(2, loadedState.Students.Count);
        Assert.Equal(2, loadedState.Cursors.Count);
    }

    [Fact]
    public async Task LoadAsync_UpdatesLastUsedDirectory()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "test_state.json");
        var students = CreateTestStudents();
        var cursors = CreateTestCursors();
        await _stateService.SaveAsync(filePath, students, cursors);
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
        var cursors = CreateTestCursors();

        // Act - Save then load
        await _stateService.SaveAsync(filePath, originalStudents, cursors);
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
        // Arrange
        var filePath = Path.Combine(_testDirectory, "cursor_roundtrip.json");
        var students = CreateTestStudents();
        var cursors = CreateTestCursors();
        cursors[0].Score = 275;
        cursors[1].IsEnabled = false;

        // Act - Save then load
        await _stateService.SaveAsync(filePath, students, cursors);
        var loadedState = await _stateService.LoadAsync(filePath);

        // Assert
        Assert.Equal(2, loadedState.Cursors.Count);
        Assert.Equal(275, loadedState.Cursors.First(c => c.Grade == "A").Score);
        Assert.False(loadedState.Cursors.First(c => c.Grade == "B").Enabled);
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
    public void ApplyCursors_UpdatesExistingCursors()
    {
        // Arrange
        var state = new SavedState
        {
            Cursors = new List<SavedCursor>
            {
                new() { Grade = "A", Score = 290, Enabled = true },
                new() { Grade = "B", Score = 260, Enabled = false }
            }
        };
        var cursors = CreateTestCursors();

        // Act
        _stateService.ApplyCursors(state, cursors);

        // Assert
        Assert.Equal(290, cursors.First(c => c.Grade.DisplayName == "A").Score);
        Assert.Equal(260, cursors.First(c => c.Grade.DisplayName == "B").Score);
        Assert.False(cursors.First(c => c.Grade.DisplayName == "B").IsEnabled);
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

    private List<CursorViewModel> CreateTestCursors()
    {
        return new List<CursorViewModel>
        {
            new CursorViewModel(new Grade(LetterGrade.A, 0), 280, true),
            new CursorViewModel(new Grade(LetterGrade.B, 1), 250, true)
        };
    }
}
