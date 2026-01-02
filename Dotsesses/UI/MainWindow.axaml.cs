using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;
using Dotsesses.UI;
using Microsoft.Extensions.DependencyInjection;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
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

        // Subscribe to theme change messages for DotPlot
        WeakReferenceMessenger.Default.Register<RenderWithThemeMessage>(this, OnRenderWithThemeMessage);
    }

    // Current theme for DotPlot
    private ThemeName _currentDotPlotTheme = ThemeName.DarkMode;

    private void OnRenderWithThemeMessage(object recipient, RenderWithThemeMessage message)
    {
        _currentDotPlotTheme = message.Theme;

        Dispatcher.UIThread.Post(() =>
        {
            // Update DotPlot backgrounds for theme
            DotPlotBorder.Background = ThemeColors.BackgroundBrush(_currentDotPlotTheme);
            DotPlotBorder.BorderBrush = _currentDotPlotTheme == ThemeName.DarkMode
                ? Brushes.Transparent
                : Brushes.Black;

            // Also update DotPlotContainer background (this is what gets rendered/exported)
            DotPlotContainer.Background = ThemeColors.BackgroundBrush(_currentDotPlotTheme);

            // Hide/show copy button based on theme (hide during export)
            CopyDotPlotButton.IsVisible = _currentDotPlotTheme == ThemeName.DarkMode;

            // Apply theme to the OxyPlot model via ViewModel
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ApplyTheme(_currentDotPlotTheme);
            }

            // Re-render hover overlay with new theme colors
            UpdateHoverOverlay();

            // Force OxyPlot to render immediately by updating layout
            DotPlotBorder.UpdateLayout();

            // Note: Callback is handled by ViolinPlotControl/CorrelationPlotControl
            // But we need to ensure the DotPlot has rendered, so post another frame
            Dispatcher.UIThread.Post(() =>
            {
                // This ensures OxyPlot has completed its render cycle
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Render);
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

        // Set initial window size to 1700x1000 or max screen size
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var workArea = screen.WorkingArea;
            var targetWidth = Math.Min(1700, workArea.Width / screen.Scaling);
            var targetHeight = Math.Min(1000, workArea.Height / screen.Scaling);
            Width = targetWidth;
            Height = targetHeight;

            // Center on screen
            Position = new PixelPoint(
                (int)(workArea.X + (workArea.Width - targetWidth * screen.Scaling) / 2),
                (int)(workArea.Y + (workArea.Height - targetHeight * screen.Scaling) / 2));
        }

        // Add PointerMoved handler to DotPlotView to capture mouse movement
        // (OxyPlot captures events, so we need handledEventsToo=true)
        DotPlotView.AddHandler(PointerMovedEvent, OnDotPlotPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        // Wire up Save, Export, and Copy button click handlers
        SaveButton.Click += OnSaveButtonClick;
        ExportButton.Click += OnExportButtonClick;
        ExportPptxButton.Click += OnExportPptxButtonClick;
        CopyDotPlotButton.Click += OnCopyDotPlotClick;

        // Initialize plots asynchronously after window is displayed
        if (DataContext is MainWindowViewModel vm)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MainWindow: Triggering async violin plot initialization");
            vm.InitializeViolinPlotAsync();

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] MainWindow: Triggering async correlation plot initialization");
            vm.InitializeCorrelationPlotAsync();
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

    private async void OnCopyDotPlotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;

        // Copy the DotPlotBorder (includes the border, which becomes visible in light mode)
        await ImageCopyService.CopyControlToClipboardAsync(DotPlotBorder, clipboard);
    }

    private async void OnExportPptxButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExportPptxWithDialog();
    }

    private async Task ExportPptxWithDialog()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Initialize hover delay service if needed
        _hoverDelayService ??= App.Services?.GetService<HoverDelayService>();

        // Check if a student is currently selected
        if (vm.HoveredStudentId.HasValue)
        {
            var choiceBox = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
            {
                ContentTitle = "Student Selected",
                ContentMessage = "A student is currently selected. What would you like to do?",
                Icon = MsBoxIcon.Question,
                ButtonDefinitions =
                [
                    new ButtonDefinition { Name = "Clear selection" },
                    new ButtonDefinition { Name = "Keep selection", IsDefault = true },
                    new ButtonDefinition { Name = "Cancel", IsCancel = true }
                ],
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

            var choice = await choiceBox.ShowWindowDialogAsync(this);

            switch (choice)
            {
                case "Clear selection":
                    _hoverDelayService?.ClearHover();
                    break;
                case "Keep selection":
                    break;
                case "Cancel":
                case null:
                    return;
            }
        }

        // Block hover interactions during export
        if (_hoverDelayService != null)
        {
            _hoverDelayService.IsExporting = true;
        }

        var storageProvider = StorageProvider;

        // Default to same directory as source file
        IStorageFolder? startLocation = null;
        string suggestedFileName = "presentation.pptx";

        if (!string.IsNullOrEmpty(vm.CurrentSourceFile))
        {
            var sourceDir = System.IO.Path.GetDirectoryName(vm.CurrentSourceFile);
            var sourceNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(vm.CurrentSourceFile);
            suggestedFileName = $"{sourceNameWithoutExt}-Presentation.pptx";

            if (!string.IsNullOrEmpty(sourceDir) && Directory.Exists(sourceDir))
            {
                startLocation = await storageProvider.TryGetFolderFromPathAsync(sourceDir);
            }
        }

        // Prompt for save location
        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PowerPoint Presentation",
            SuggestedStartLocation = startLocation,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PowerPoint Presentation") { Patterns = new[] { "*.pptx" } }
            }
        });

        if (result == null)
        {
            // Reset export flag if user cancels file picker
            if (_hoverDelayService != null)
            {
                _hoverDelayService.IsExporting = false;
            }
            return;
        }

        var outputPath = result.Path.LocalPath;

        try
        {
            // Get the ViolinPlotControl instance
            var violinPlotControl = this.FindControl<ViolinPlotControl>("ViolinPlotControl")
                                    ?? this.GetVisualDescendants().OfType<ViolinPlotControl>().FirstOrDefault();

            if (violinPlotControl == null)
            {
                var errorBox = MessageBoxManager.GetMessageBoxStandard(
                    "Export Error",
                    "Could not find ViolinPlot control.",
                    ButtonEnum.Ok,
                    MsBoxIcon.Error);
                await errorBox.ShowWindowDialogAsync(this);
                return;
            }

            // Get the CorrelationPlotControl instance
            var correlationPlotControl = this.FindControl<CorrelationPlotControl>("CorrelationPlotControl")
                                         ?? this.GetVisualDescendants().OfType<CorrelationPlotControl>().FirstOrDefault();

            if (correlationPlotControl == null)
            {
                var errorBox = MessageBoxManager.GetMessageBoxStandard(
                    "Export Error",
                    "Could not find CorrelationPlot control.",
                    ButtonEnum.Ok,
                    MsBoxIcon.Error);
                await errorBox.ShowWindowDialogAsync(this);
                return;
            }

            // Get the PlotTabContainerViewModel for tab switching
            if (vm.PlotTabContainerViewModel == null)
            {
                var errorBox = MessageBoxManager.GetMessageBoxStandard(
                    "Export Error",
                    "Could not find PlotTabContainer view model.",
                    ButtonEnum.Ok,
                    MsBoxIcon.Error);
                await errorBox.ShowWindowDialogAsync(this);
                return;
            }

            var exportService = new PowerPointExportService();
            var className = !string.IsNullOrEmpty(vm.CurrentSourceFile)
                ? System.IO.Path.GetFileNameWithoutExtension(vm.CurrentSourceFile)
                : "Grade Analysis";

            await exportService.ExportAsync(
                outputPath,
                DotPlotBorder,
                violinPlotControl,
                correlationPlotControl,
                vm.PlotTabContainerViewModel,
                vm.ComplianceRows,
                className);

            // Show success message with option to open
            var successBox = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
            {
                ContentTitle = "Export Complete",
                ContentMessage = $"Presentation exported successfully:\n\n• {System.IO.Path.GetFileName(outputPath)}     ",
                Icon = MsBoxIcon.Success,
                ButtonDefinitions =
                [
                    new ButtonDefinition { Name = "Open", IsDefault = true },
                    new ButtonDefinition { Name = "OK" }
                ],
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

            var buttonResult = await successBox.ShowWindowDialogAsync(this);
            if (buttonResult == "Open")
            {
                Process.Start(new ProcessStartInfo(outputPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            var errorBox = MessageBoxManager.GetMessageBoxStandard(
                "Export Error",
                $"Failed to export presentation: {ex.Message}",
                ButtonEnum.Ok,
                MsBoxIcon.Error);
            await errorBox.ShowWindowDialogAsync(this);
        }
        finally
        {
            // Re-enable hover interactions after export completes
            if (_hoverDelayService != null)
            {
                _hoverDelayService.IsExporting = false;
            }
        }
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

            // Build file paths and check for existing files
            var gradesFile = System.IO.Path.Combine(exportDirectory, $"{fileNameStem}-Grades.xlsx");
            var distributionFile = System.IO.Path.Combine(exportDirectory, $"{fileNameStem}-Grade-Distribution.xlsx");

            var existingFiles = new[] { gradesFile, distributionFile }.Where(File.Exists).ToList();
            if (existingFiles.Any())
            {
                if (!await ConfirmActionAsync("Files Exist", "Exported files already exist", "Overwrite existing files"))
                {
                    return;
                }
            }

            try
            {
                var exportService = new Dotsesses.Services.ExportService();
                exportService.Export(
                    exportDirectory,
                    fileNameStem,
                    vm.ClassAssessment.Assessments,
                    vm.GradeAssigner,
                    vm.ComplianceRows);

                // Show success message
                var successBox = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
                {
                    ContentTitle = "Export Complete",
                    ContentMessage = $"Files exported successfully:\n\n• {System.IO.Path.GetFileName(gradesFile)}\n• {System.IO.Path.GetFileName(distributionFile)}    ",
                    Icon = MsBoxIcon.Success,
                    ButtonDefinitions = [new ButtonDefinition { Name = "OK", IsDefault = true }],
                    SizeToContent = SizeToContent.WidthAndHeight,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
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
        const double dotSize = 3.0;  // 50% larger than original 2.0
        double markerSize = dotSize * 6;
        double ringThickness = dotSize * 1.0;
        Color dotColor = Color.FromRgb(255, 51, 51); // Total series red (#FF3333)
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

        // Create tooltip with sigma value and letter grade
        var grade = vm.GradeAssigner.AssignGrade(student.AggregateGrade).DisplayName;
        CreateDotPlotTooltip(student.AggregateGrade, sigmaValue, grade, dotColor, screenPoint.X, screenPoint.Y);
    }

    private void CreateDotPlotTooltip(int score, double sigmaValue, string grade, Color dotColor, double screenX, double screenY)
    {
        var tooltipBorder = new Border
        {
            Background = ThemeColors.BackgroundBrush(_currentDotPlotTheme),
            BorderBrush = ThemeColors.BorderBrush(_currentDotPlotTheme),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2)
        };

        // Format: score | sigma | grade with pipe separators in foreground color
        var colorBrush = new SolidColorBrush(dotColor);
        var pipeBrush = ThemeColors.ForegroundBrush(_currentDotPlotTheme);
        var sigmaSign = sigmaValue >= 0 ? "+" : "";

        var scoreText = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.Bold
        };

        // Score value (colored)
        scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run($"{score} ") { Foreground = colorBrush });
        // Pipe (theme foreground)
        scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run("| ") { Foreground = pipeBrush });
        // Sigma value (colored)
        scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run($"{sigmaSign}{sigmaValue:F1}σ ") { Foreground = colorBrush });
        // Pipe (theme foreground)
        scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run("| ") { Foreground = pipeBrush });
        // Grade (colored)
        scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run(grade) { Foreground = colorBrush });

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
    /// Shows a confirmation dialog with custom action button text.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message</param>
    /// <param name="actionButtonText">Text for the action button</param>
    /// <returns>True if user clicked the action button, false if cancelled</returns>
    private async Task<bool> ConfirmActionAsync(string title, string message, string actionButtonText)
    {
        var box = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams
        {
            ContentTitle = title,
            ContentMessage = message,
            ButtonDefinitions =
            [
                new ButtonDefinition { Name = actionButtonText },
                new ButtonDefinition { Name = "Cancel", IsCancel = true, IsDefault = true }
            ],
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        });
        var result = await box.ShowWindowDialogAsync(this);
        return result == actionButtonText;
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

/// <summary>
/// Converts boolean to color: true = red (#FF6B6B), false = transparent.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b
            ? new SolidColorBrush(Color.FromRgb(255, 107, 107))
            : new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
