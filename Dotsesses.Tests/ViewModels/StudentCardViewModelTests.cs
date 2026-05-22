namespace Dotsesses.Tests.ViewModels;

using System.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Models;
using Dotsesses.UI;

/// <summary>
/// S04/T04: StudentCardViewModel.DisplayScores filtering. The filter must NOT
/// break PropertyChanged subscription on hidden scores (see research §pitfalls #2)
/// so comment edits on filtered-out scores still fire StudentEditedMessage.
/// </summary>
public class StudentCardViewModelTests
{
    private static StudentAssessment MakeAssessmentWithThreeScores()
    {
        return new StudentAssessment
        {
            Id = 42,
            MuppetName = "TestMuppet",
            Scores = new List<Score>
            {
                new Score("Q1", null, 80, comment: null),
                new Score("Q2", null, 70, comment: null),
                new Score("Total", null, 150, comment: null),
            }
        };
    }

    [Fact]
    public void DisplayScores_DefaultsToAllScoresWhenFilterNotProvided()
    {
        // Arrange
        var assessment = MakeAssessmentWithThreeScores();
        var colors = new Dictionary<string, string> { ["Q1"] = "#000", ["Q2"] = "#111", ["Total"] = "#222" };

        // Act — construct with no displayScores arg (back-compat path).
        using var card = new StudentCardViewModel(assessment, "B", colors, new WeakReferenceMessenger());

        // Assert — DisplayScores is the full Assessment.Scores list.
        Assert.Equal(assessment.Scores.Count, card.DisplayScores.Count);
        Assert.True(assessment.Scores.SequenceEqual(card.DisplayScores));
    }

    [Fact]
    public void DisplayScores_ExposesProvidedFilteredList()
    {
        // Arrange
        var assessment = MakeAssessmentWithThreeScores();
        var colors = new Dictionary<string, string> { ["Q1"] = "#000", ["Q2"] = "#111", ["Total"] = "#222" };
        var filtered = assessment.Scores.Take(2).ToList();

        // Act
        using var card = new StudentCardViewModel(assessment, "B", colors, new WeakReferenceMessenger(), clearAction: null, displayScores: filtered);

        // Assert — exactly 2 items, in order, matching the input.
        Assert.Equal(2, card.DisplayScores.Count);
        Assert.Equal(filtered[0], card.DisplayScores[0]);
        Assert.Equal(filtered[1], card.DisplayScores[1]);
    }

    [Fact]
    public void Constructor_SubscribesToAllScoresEvenWhenFiltered()
    {
        // Arrange — filter excludes the third (Total) score, but the subscription
        // contract requires Score.PropertyChanged still be hooked on the EXCLUDED
        // score so comment edits keep firing StudentEditedMessage (research §pitfalls #2).
        // We verify subscription via reflection on the PropertyChanged backing-field
        // delegate, which is robust to xunit having no Avalonia UI message loop.
        var assessment = MakeAssessmentWithThreeScores();
        var colors = new Dictionary<string, string> { ["Q1"] = "#000", ["Q2"] = "#111", ["Total"] = "#222" };
        var filtered = assessment.Scores.Take(2).ToList();
        var excluded = assessment.Scores[2]; // "Total" — not in DisplayScores.

        Assert.False(filtered.Contains(excluded), "Sanity: filter must exclude the third score.");

        // Act
        using var card = new StudentCardViewModel(assessment, "B", colors, new WeakReferenceMessenger(), clearAction: null, displayScores: filtered);

        // Assert — every score in Assessment.Scores (including the excluded one) has at
        // least one PropertyChanged subscriber attached.
        foreach (var score in assessment.Scores)
        {
            var handler = GetPropertyChangedHandler(score);
            Assert.NotNull(handler);
            Assert.NotEmpty(handler!.GetInvocationList());
        }
    }

    /// <summary>
    /// Reflects over the compiler-generated PropertyChanged backing field on
    /// <see cref="Score"/> to read the current event delegate. Used to verify
    /// subscription is wired without depending on the Avalonia UI dispatcher.
    /// </summary>
    private static PropertyChangedEventHandler? GetPropertyChangedHandler(Score score)
    {
        var field = typeof(Score).GetField(
            "PropertyChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(score) as PropertyChangedEventHandler;
    }
}
