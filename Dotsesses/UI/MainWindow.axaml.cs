using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Services;
using Dotsesses.UI;
using Microsoft.Extensions.DependencyInjection;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using OxyPlot;

namespace Dotsesses.UI;

public partial class MainWindow : Window
{
    private HoverDelayService? _hoverDelayService;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
        PointerMoved += OnGlobalPointerMoved;

        // Subscribe to edit student messages
        WeakReferenceMessenger.Default.Register<EditStudentMessage>(this, async (r, m) =>
        {
            await HandleEditStudentRequest(m);
        });
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.HasUnsavedChanges)
        {
            e.Cancel = true; // Prevent immediate close

            var box = MessageBoxManager.GetMessageBoxStandard(
                "Unsaved Changes",
                "You have unsaved changes. Would you like to save before closing?",
                ButtonEnum.YesNoCancel,
                MsBoxIcon.Question);

            var result = await box.ShowWindowDialogAsync(this);

            if (result == ButtonResult.Yes)
            {
                await SaveWithDialog();
                Close(); // Close after saving
            }
            else if (result == ButtonResult.No)
            {
                vm.HasUnsavedChanges = false; // Clear flag to allow close
                Close();
            }
            // Cancel: do nothing, window stays open
        }
    }

    private void OnGlobalPointerMoved(object? sender, PointerEventArgs e)
    {
        // Get service if not already cached
        _hoverDelayService ??= App.Services?.GetService<HoverDelayService>();

        if (_hoverDelayService != null)
        {
            var position = e.GetPosition(this);
            _hoverDelayService.ReportMousePosition(position);
        }
    }

    private void OnWindowLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MainWindow: Loaded event fired");

        // Add PointerMoved handler to DotPlotView to capture mouse movement
        // (OxyPlot captures events, so we need handledEventsToo=true)
        DotPlotView.AddHandler(PointerMovedEvent, OnDotPlotPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        // Wire up Save and Export button click handlers
        SaveButton.Click += OnSaveButtonClick;
        ExportButton.Click += OnExportButtonClick;

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

    private async void OnSaveButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SaveWithDialog();
    }

    private async void OnExportButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExportWithDialog();
    }

    private async Task SaveWithDialog(bool forceDialog = false)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // If we have a current file path and not forcing dialog, save directly
        if (!forceDialog && !string.IsNullOrEmpty(vm.CurrentSaveFilePath))
        {
            await vm.SaveStateCommand.ExecuteAsync(vm.CurrentSaveFilePath);
            return;
        }

        var storageProvider = StorageProvider;

        // Default to same directory and name as source file, but with .dots extension
        IStorageFolder? startLocation = null;
        string suggestedFileName = "dotsesses_state.dots";

        if (!string.IsNullOrEmpty(vm.CurrentSourceFile))
        {
            var sourceDir = System.IO.Path.GetDirectoryName(vm.CurrentSourceFile);
            var sourceNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(vm.CurrentSourceFile);
            suggestedFileName = sourceNameWithoutExt + ".dots";

            if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
            {
                startLocation = await storageProvider.TryGetFolderFromPathAsync(sourceDir);
            }
        }
        else if (!string.IsNullOrEmpty(vm.StateService.LastUsedDirectory) &&
                 Directory.Exists(vm.StateService.LastUsedDirectory))
        {
            startLocation = await storageProvider.TryGetFolderFromPathAsync(vm.StateService.LastUsedDirectory);
        }

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save State",
            SuggestedStartLocation = startLocation,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Dotsesses files") { Patterns = new[] { "*.dots" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });

        if (result != null)
        {
            var filePath = result.Path.LocalPath;
            await vm.SaveStateCommand.ExecuteAsync(filePath);
        }
    }

    private async Task ExportWithDialog()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var storageProvider = StorageProvider;

        // Default to same directory as source file
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrEmpty(vm.CurrentSourceFile))
        {
            var sourceDir = System.IO.Path.GetDirectoryName(vm.CurrentSourceFile);
            if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
            {
                startLocation = await storageProvider.TryGetFolderFromPathAsync(sourceDir);
            }
        }

        // Prompt for export directory
        var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Export Directory",
            SuggestedStartLocation = startLocation,
            AllowMultiple = false
        });

        if (result.Count > 0)
        {
            var exportDirectory = result[0].Path.LocalPath;
            var fileNameStem = !string.IsNullOrEmpty(vm.CurrentSourceFile)
                ? System.IO.Path.GetFileNameWithoutExtension(vm.CurrentSourceFile)
                : "export";

            try
            {
                var exportService = new Dotsesses.Services.ExportService();
                var (gradesFile, distributionFile) = exportService.Export(
                    exportDirectory,
                    fileNameStem,
                    vm.ClassAssessment.Assessments,
                    vm.GradeAssigner,
                    vm.ComplianceRows);

                // Show success message
                var successBox = MessageBoxManager.GetMessageBoxStandard(
                    "Export Complete",
                    $"Files exported successfully:\n\n• {gradesFile}\n• {distributionFile}",
                    ButtonEnum.Ok,
                    MsBoxIcon.Success);
                await successBox.ShowWindowDialogAsync(this);
            }
            catch (Exception ex)
            {
                var errorBox = MessageBoxManager.GetMessageBoxStandard(
                    "Export Error",
                    $"Failed to export files: {ex.Message}",
                    ButtonEnum.Ok,
                    MsBoxIcon.Error);
                await errorBox.ShowWindowDialogAsync(this);
            }
        }
    }

    private void OnDotPlotPointerMoved(object? sender, PointerEventArgs e)
    {
        // Get service if not already cached
        _hoverDelayService ??= App.Services?.GetService<HoverDelayService>();

        if (_hoverDelayService != null)
        {
            // Convert to window coordinates for consistent velocity tracking
            var position = e.GetPosition(this);
            _hoverDelayService.ReportMousePosition(position);
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

        // Calculate mean and standard deviation for sigma display
        var scores = vm.ClassAssessment.Assessments.Select(a => (double)a.AggregateGrade).ToList();
        var mean = scores.Average();
        var stdDev = Math.Sqrt(scores.Average(s => Math.Pow(s - mean, 2)));
        var sigmaValue = (student.AggregateGrade - mean) / stdDev;

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

        // Draw hover marker as annulus/ring in screen coordinates (6x normal size)
        const double dotSize = 2.0;
        double markerSize = dotSize * 6;
        double ringThickness = dotSize * 1.0;
        Color dotColor = Color.FromArgb(255, 255, 255, 255); // White
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

        // Create tooltip with sigma value
        CreateDotPlotTooltip(student.AggregateGrade, sigmaValue, dotColor, screenPoint.X, screenPoint.Y);
    }

    private void CreateDotPlotTooltip(int score, double sigmaValue, Color dotColor, double screenX, double screenY)
    {
        var tooltipBorder = new Border
        {
            Background = new SolidColorBrush(Colors.Black),
            BorderBrush = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2)
        };

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

        // Format sigma with sign
        var sigmaSign = sigmaValue >= 0 ? "+" : "";
        var scoreText = new TextBlock
        {
            Text = $"{score} {sigmaSign}{sigmaValue:F1}σ",
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

        // Get the Total score's comment (or empty if none)
        var totalScore = student.Scores.FirstOrDefault(s => s.Name.Equals("Total", StringComparison.OrdinalIgnoreCase));
        var currentComment = totalScore?.Comment ?? "";

        var editor = new CommentEditorWindow(muppetName, currentComment);

        await editor.ShowDialog(this);

        if (editor.WasOkClicked && totalScore != null)
        {
            var newComment = editor.GetComment();
            totalScore.Comment = newComment;

            // Broadcast that the student was edited
            WeakReferenceMessenger.Default.Send(new StudentEditedMessage(message.StudentId));
        }
    }

    /// <summary>
    /// Triggers the save dialog. Called from native menu.
    /// </summary>
    public async Task TriggerSave() => await SaveWithDialog();

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
