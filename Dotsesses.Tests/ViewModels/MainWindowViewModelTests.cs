namespace Dotsesses.Tests.ViewModels;

using Dotsesses.ViewModels;
using OxyPlot;

public class MainWindowViewModelTests
{
    [Fact]
    public void Constructor_InitializesPlotModel()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.NotNull(viewModel.Data);
        Assert.Equal("Scatter Plot Example", viewModel.Data.Title);
        Assert.Equal(OxyColors.Black, viewModel.Data.Background);
    }

    [Fact]
    public void Constructor_InitializesGreeting()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.Equal("Welcome to Avalonia!", viewModel.Greeting);
    }

    [Fact]
    public void PlotModel_HasAxes()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.Equal(2, viewModel.Data.Axes.Count);
        Assert.Contains(viewModel.Data.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Bottom);
        Assert.Contains(viewModel.Data.Axes, a => a.Position == OxyPlot.Axes.AxisPosition.Left);
    }

    [Fact]
    public void PlotModel_HasScatterSeries()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.Single(viewModel.Data.Series);
        Assert.IsType<OxyPlot.Series.ScatterSeries>(viewModel.Data.Series[0]);
    }

    [Fact]
    public void ScatterSeries_HasDataPoints()
    {
        // Act
        var viewModel = new MainWindowViewModel();
        var series = viewModel.Data.Series[0] as OxyPlot.Series.ScatterSeries;

        // Assert
        Assert.NotNull(series);
        Assert.Equal(8, series.Points.Count);
    }

    [Fact]
    public void PlotModel_UsesDarkTheme()
    {
        // Act
        var viewModel = new MainWindowViewModel();

        // Assert
        Assert.Equal(OxyColors.Black, viewModel.Data.Background);
        Assert.Equal(OxyColors.White, viewModel.Data.TextColor);
        Assert.Equal(OxyColors.White, viewModel.Data.PlotAreaBorderColor);
    }
}
