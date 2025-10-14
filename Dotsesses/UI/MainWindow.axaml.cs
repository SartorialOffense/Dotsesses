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
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.UI;
using OxyPlot;

namespace Dotsesses.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnWindowLoaded;

        // Subscribe to edit student messages
        WeakReferenceMessenger.Default.Register<EditStudentMessage>(this, async (r, m) =>
        {
            await HandleEditStudentRequest(m);
        });
    }

    private void OnWindowLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MainWindow: Loaded event fired");
        // Initialize violin plot asynchronously after window is displayed
        if (DataContext is MainWindowViewModel vm)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MainWindow: Triggering async violin plot initialization");
            vm.InitializeViolinPlotAsync();
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MainWindow: DataContext is not MainWindowViewModel!");
        }
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
        // Clear existing hover markers and tooltips
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

        // Create tooltip
        CreateDotPlotTooltip(student.AggregateGrade, dotColor, screenPoint.X, screenPoint.Y);
    }

    private void CreateDotPlotTooltip(int score, Color dotColor, double screenX, double screenY)
    {
        var tooltipBorder = new Border
        {
            Background = new SolidColorBrush(Colors.Black),
            BorderBrush = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2)
        };

        // Score value only
        // Lighten color if too dark
        double luminance = 0.2126 * dotColor.R + 0.7152 * dotColor.G + 0.0722 * dotColor.B;
        if (luminance < 128)
        {
            double factor = 0.6;
            dotColor = Color.FromRgb(
                (byte)(dotColor.R + (255 - dotColor.R) * factor),
                (byte)(dotColor.G + (255 - dotColor.G) * factor),
                (byte)(dotColor.B + (255 - dotColor.B) * factor));
        }

        var scoreText = new TextBlock
        {
            Text = score.ToString(),
            FontSize = 11,
            Foreground = new SolidColorBrush(dotColor)
        };

        tooltipBorder.Child = scoreText;

        // Measure tooltip to determine positioning
        tooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tooltipWidth = tooltipBorder.DesiredSize.Width;

        // Get canvas width
        double canvasWidth = DotPlotHoverOverlay.Bounds.Width;

        // Position on left if too close to right edge, otherwise on right
        double leftPos = screenX + 20 + tooltipWidth > canvasWidth
            ? screenX - tooltipWidth - 20
            : screenX + 20;

        Canvas.SetLeft(tooltipBorder, leftPos);
        Canvas.SetTop(tooltipBorder, screenY - 10);

        DotPlotHoverOverlay.Children.Add(tooltipBorder);
    }

    private async Task HandleEditStudentRequest(EditStudentMessage message)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var student = vm.ClassAssessment.Assessments.FirstOrDefault(s => s.Id == message.StudentId);
        if (student == null)
            return;

        var muppetName = vm.ClassAssessment.MuppetNameMap.TryGetValue(student.Id, out var info) ? info.Name : "Unknown";
        var editor = new CommentEditorWindow(muppetName, student.Comment);

        await editor.ShowDialog(this);

        if (editor.WasOkClicked)
        {
            var newComment = editor.GetComment();
            student.Comment = newComment;

            // Broadcast that the student was edited
            WeakReferenceMessenger.Default.Send(new StudentEditedMessage(message.StudentId));
        }
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

/// <summary>
/// Converts comment string to display text (shows placeholder if empty).
/// </summary>
public class CommentDisplayConverter : IMultiValueConverter
{
    public static readonly CommentDisplayConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is string comment && !string.IsNullOrWhiteSpace(comment))
        {
            return comment;
        }

        return "(No comment)";
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
