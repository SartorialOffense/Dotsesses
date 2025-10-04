using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Dotsesses.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public string Greeting { get; } = "Welcome to Avalonia!";

    public PlotModel Data { get; private set; }

    public MainWindowViewModel()
    {
        Data = new PlotModel
        {
            Title = "Scatter Plot Example",
            Background = OxyColors.Black,
            TextColor = OxyColors.White,
            PlotAreaBorderColor = OxyColors.White
        };

        // Add axes with dark theme colors
        Data.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "X",
            AxislineColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            TextColor = OxyColors.White
        });
        Data.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Y",
            AxislineColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            TextColor = OxyColors.White
        });

        var scatterSeries = new ScatterSeries
        {
            MarkerType = MarkerType.Circle,
            MarkerSize = 4,
            MarkerFill = OxyColors.Cyan
        };

        // Add some sample data points (double pairs)
        scatterSeries.Points.Add(new ScatterPoint(1.0, 2.0));
        scatterSeries.Points.Add(new ScatterPoint(2.0, 4.5));
        scatterSeries.Points.Add(new ScatterPoint(3.0, 3.5));
        scatterSeries.Points.Add(new ScatterPoint(4.0, 6.0));
        scatterSeries.Points.Add(new ScatterPoint(5.0, 5.5));
        scatterSeries.Points.Add(new ScatterPoint(6.0, 7.0));
        scatterSeries.Points.Add(new ScatterPoint(7.0, 8.5));
        scatterSeries.Points.Add(new ScatterPoint(8.0, 8.0));

        Data.Series.Add(scatterSeries);

        // Invalidate the plot to ensure it renders
        Data.InvalidatePlot(true);
    }
}