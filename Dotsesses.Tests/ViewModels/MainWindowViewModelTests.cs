namespace Dotsesses.Tests.ViewModels;

using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Services;
using Dotsesses.UI;
using Dotsesses.Models;
using OxyPlot;

public class MainWindowViewModelTests
{
    private static MainWindowViewModel CreateViewModel()
    {
        var hoverDelayService = new HoverDelayService();
        return new MainWindowViewModel(WeakReferenceMessenger.Default, null!, null!, hoverDelayService);
    }

    [Fact]
    public void Constructor_InitializesPlotModel()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.NotNull(viewModel.DotplotModel);
        Assert.Equal(OxyColors.Transparent, viewModel.DotplotModel.Background);
    }

    [Fact]
    public void Constructor_LoadsSyntheticData()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.NotNull(viewModel.ClassAssessment);
        Assert.True(viewModel.ClassAssessment.Assessments.Count > 0, "Should have at least some students");
    }

    [Fact]
    public void PlotModel_HasAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert - Now has 4 axes: SharedX, StatsY, DotY, CursorY
        Assert.Equal(4, viewModel.DotplotModel.Axes.Count);
        Assert.Contains(viewModel.DotplotModel.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Bottom);
        Assert.Contains(viewModel.DotplotModel.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Left);
    }

    [Fact]
    public void PlotModel_HasScatterSeries()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert - now has 2 series (unselected and selected)
        Assert.Equal(2, viewModel.DotplotModel.Series.Count);
        Assert.IsType<OxyPlot.Series.ScatterSeries>(viewModel.DotplotModel.Series[0]);
        Assert.IsType<OxyPlot.Series.ScatterSeries>(viewModel.DotplotModel.Series[1]);
    }

    [Fact]
    public void ScatterSeries_HasStudents()
    {
        // Act
        var viewModel = CreateViewModel();
        var circleSeries = viewModel.DotplotModel.Series[0] as OxyPlot.Series.ScatterSeries;
        var squareSeries = viewModel.DotplotModel.Series[1] as OxyPlot.Series.ScatterSeries;

        Assert.NotNull(circleSeries);
        Assert.NotNull(squareSeries);
        // Total points across both series should match student count
        var totalPoints = circleSeries.Points.Count + squareSeries.Points.Count;
        Assert.Equal(viewModel.ClassAssessment.Assessments.Count, totalPoints);
    }

    [Fact]
    public void PlotModel_UsesDarkTheme()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert - Uses transparent background now for theme integration
        Assert.Equal(OxyColors.Transparent, viewModel.DotplotModel.Background);
        Assert.Equal(OxyColor.FromRgb(60, 60, 60), viewModel.DotplotModel.PlotAreaBorderColor);
    }

    [Fact]
    public void Constructor_InitializesCursors()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.NotNull(viewModel.Cursors);
        Assert.NotEmpty(viewModel.Cursors);
    }

    [Fact]
    public void Constructor_InitializesComplianceGrid()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        Assert.NotNull(viewModel.ComplianceRows);
        Assert.Equal(11, viewModel.ComplianceRows.Count); // All grades A through F (including C-, D+)
    }

    [Fact]
    public void AllGrades_AreEnabledByDefault()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert - All grades (A through F) should be enabled by default
        var fGrade = viewModel.ComplianceRows.FirstOrDefault(r => r.Grade.LetterGrade == LetterGrade.F);
        Assert.NotNull(fGrade);
        Assert.True(fGrade.IsEnabled, "F grade should be enabled by default");

        var fCursor = viewModel.Cursors.FirstOrDefault(c => c.Grade.LetterGrade == LetterGrade.F);
        Assert.NotNull(fCursor);
        Assert.True(fCursor.IsEnabled, "F cursor should be enabled by default");
    }
}
