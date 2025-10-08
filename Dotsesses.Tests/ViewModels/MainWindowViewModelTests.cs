namespace Dotsesses.Tests.ViewModels;

using Dotsesses.ViewModels;
using Dotsesses.Models;
using OxyPlot;

public class MainWindowViewModelTests
{
    [Fact]
    public void Constructor_InitializesPlotModel()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.NotNull(viewModel.DotplotModel);
        Assert.Equal("Student Grade Distribution", viewModel.DotplotModel.Title);
        Assert.Equal(OxyColors.Black, viewModel.DotplotModel.Background);
    }

    [Fact]
    public void Constructor_LoadsSyntheticData()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.NotNull(viewModel.ClassAssessment);
        Assert.Equal(100, viewModel.ClassAssessment.Assessments.Count);
    }

    [Fact]
    public void PlotModel_HasAxes()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.Equal(2, viewModel.DotplotModel.Axes.Count);
        Assert.Contains(viewModel.DotplotModel.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Bottom);
        Assert.Contains(viewModel.DotplotModel.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Left);
    }

    [Fact]
    public void PlotModel_HasScatterSeries()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert - now has 2 series (unselected and selected)
        Assert.Equal(2, viewModel.DotplotModel.Series.Count);
        Assert.IsType<OxyPlot.Series.ScatterSeries>(viewModel.DotplotModel.Series[0]);
        Assert.IsType<OxyPlot.Series.ScatterSeries>(viewModel.DotplotModel.Series[1]);
    }

    [Fact]
    public void ScatterSeries_Has100Students()
    {
        // Act
        var viewModel = new MainWindowViewModel();
        var unselectedSeries = viewModel.DotplotModel.Series[0] as OxyPlot.Series.ScatterSeries;
        var selectedSeries = viewModel.DotplotModel.Series[1] as OxyPlot.Series.ScatterSeries;

        // Assert - initially all students are unselected
        Assert.NotNull(unselectedSeries);
        Assert.NotNull(selectedSeries);
        Assert.Equal(100, unselectedSeries.Points.Count);
        Assert.Equal(0, selectedSeries.Points.Count);
    }

    [Fact]
    public void PlotModel_UsesDarkTheme()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.Equal(OxyColors.Black, viewModel.DotplotModel.Background);
        Assert.Equal(OxyColors.White, viewModel.DotplotModel.TextColor);
        Assert.Equal(OxyColors.White, viewModel.DotplotModel.PlotAreaBorderColor);
    }

    [Fact]
    public void Constructor_InitializesCursors()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.NotNull(viewModel.Cursors);
        Assert.NotEmpty(viewModel.Cursors);
    }

    [Fact]
    public void Constructor_InitializesComplianceGrid()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.NotNull(viewModel.ComplianceRows);
        Assert.Equal(10, viewModel.ComplianceRows.Count); // All grades A through F
    }

    [Fact]
    public void SelectedStudents_InitiallyEmpty()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.NotNull(viewModel.SelectedStudents);
        Assert.Empty(viewModel.SelectedStudents);
    }


    [Fact]
    public void AddingCursor_ClampsPositionToValidDraggingBounds()
    {
        // Arrange
        var viewModel = new MainWindowViewModel();
        var minScore = viewModel.ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxScore = viewModel.ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var minBound = minScore - 1;
        var maxBound = maxScore + 1;

        // Find a grade that's not initially enabled (like F)
        var fGrade = viewModel.ComplianceRows.FirstOrDefault(r => r.Grade.LetterGrade == LetterGrade.F);
        Assert.NotNull(fGrade);
        Assert.False(fGrade.IsEnabled); // F should not be enabled initially
        
        var fCursorBefore = viewModel.Cursors.FirstOrDefault(c => c.Grade.LetterGrade == LetterGrade.F);
        var scoreBefore = fCursorBefore?.Score ?? -1;

        // Act - Enable F grade (this triggers the callback via OnIsEnabledChanged)
        fGrade.IsEnabled = true;

        // Assert - F cursor should be clamped to at least minBound
        var fCursor = viewModel.Cursors.FirstOrDefault(c => c.Grade.LetterGrade == LetterGrade.F);
        Assert.NotNull(fCursor);
        Assert.True(fCursor.IsEnabled, "F cursor should be enabled");
        Assert.True(fCursor.Score >= minBound, $"F cursor score {fCursor.Score} should be >= minBound {minBound} (was {scoreBefore})");
        Assert.True(fCursor.Score <= maxBound, $"F cursor score {fCursor.Score} should be <= maxBound {maxBound}");
    }
}
