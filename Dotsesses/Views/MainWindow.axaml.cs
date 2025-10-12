using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dotsesses.ViewModels;
using OxyPlot;

namespace Dotsesses.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HoveredStudentId))
        {
            UpdateHoverOverlay();
        }
    }

    private void UpdateHoverOverlay()
    {
        // Clear existing hover markers
        DotPlotHoverOverlay.Children.Clear();

        if (DataContext is not MainWindowViewModel vm || !vm.HoveredStudentId.HasValue)
            return;

        var student = vm.ClassAssessment.Assessments
            .FirstOrDefault(s => s.Id == vm.HoveredStudentId.Value);

        if (student == null)
            return;

        // Calculate data coordinates (same logic as in ViewModel)
        var studentsAtScore = vm.ClassAssessment.Assessments
            .Where(a => a.AggregateGrade == student.AggregateGrade)
            .OrderBy(s => s.Id)
            .ToList();

        int index = studentsAtScore.IndexOf(student);
        double binOffset = student.AggregateGrade % 2 == 1 ? 0.1 : 0.0;
        double yPos = index * 2 + binOffset;

        // Convert data coordinates to screen coordinates using the same axes as the dots
        var xAxis = DotPlotView.ActualModel?.Axes.FirstOrDefault(a => a.Key == "SharedX");
        var yAxis = DotPlotView.ActualModel?.Axes.FirstOrDefault(a => a.Key == "DotY");

        if (xAxis == null || yAxis == null)
            return;

        var screenPoint = xAxis.Transform(student.AggregateGrade, yPos, yAxis);
        if (double.IsNaN(screenPoint.Y))
            return;

        // Get the actual dot color (same alpha as main dots)
        Color dotColor;
        bool colorByAttribute = !string.IsNullOrEmpty(vm.SelectedColorAttribute) && vm.SelectedColorAttribute != "[None]";

        if (colorByAttribute)
        {
            var attributeValue = student.Attributes
                .FirstOrDefault(attr => attr.Name == vm.SelectedColorAttribute)?.Value ?? "Unknown";
            var oxyColor = vm.GetOxyColorForValue(attributeValue);
            dotColor = Color.FromArgb(255, oxyColor.R, oxyColor.G, oxyColor.B);
        }
        else
        {
            dotColor = Color.FromArgb(255, 255, 255, 255); // White
        }

        // Draw hover marker as annulus/ring in screen coordinates (6x normal size)
        double markerSize = vm.DotSize * 6;
        double ringThickness = vm.DotSize * 1.0;
        var hoverMarker = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Stroke = new SolidColorBrush(dotColor),
            StrokeThickness = ringThickness
        };

        Canvas.SetLeft(hoverMarker, screenPoint.X - markerSize / 2);
        Canvas.SetTop(hoverMarker, screenPoint.Y - markerSize / 2);

        DotPlotHoverOverlay.Children.Add(hoverMarker);
    }

    /// <summary>
    /// Saves a PNG snapshot of the window to the specified path or temp folder.
    /// </summary>
    /// <param name="outputPath">Optional output path. If null, saves to temp folder with timestamp.</param>
    /// <returns>The full file path where the snapshot was saved.</returns>
    public async Task<string> SaveSnapshotAsync(string? outputPath = null)
    {
        // Determine output path
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            outputPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dotsesses_snapshot_{timestamp}.png");
        }

        // Ensure directory exists
        var directory = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Wait a moment to ensure rendering is complete
        await Task.Delay(200);

        // Force layout update
        UpdateLayout();

        // Create bitmap with window dimensions
        var pixelSize = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
        var dpiVector = new Vector(96, 96);

        using var bitmap = new RenderTargetBitmap(pixelSize, dpiVector);
        bitmap.Render(this);

        // Save to file with maximum quality
        bitmap.Save(outputPath, 100);

        return outputPath;
    }
}

/// <summary>
/// Converts signed deviation to appropriate color: negative = light blue, positive = red.
/// </summary>
public class DeviationColorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is int signedDeviation)
        {
            if (signedDeviation < 0)
            {
                // Negative deviation (below target) - light blue
                return new SolidColorBrush(Color.FromRgb(100, 180, 230));
            }
            else if (signedDeviation > 0)
            {
                // Positive deviation (above target) - red
                return new SolidColorBrush(Color.FromRgb(255, 107, 107));
            }
        }

        return new SolidColorBrush(Colors.White);
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


/// <summary>
/// Converts boolean to resize cursor type.
/// </summary>
public class ResizeCursorConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is bool isResize && isResize)
        {
            return new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast);
        }

        return new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow);
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
