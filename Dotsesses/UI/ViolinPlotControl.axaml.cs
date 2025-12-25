using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Calculators;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.UI;

namespace Dotsesses.UI;

public partial class ViolinPlotControl : UserControl
{
    private CancellationTokenSource? _resizeCts;

    // Double-click tracking
    private DateTime _lastClickTime;
    private int? _lastClickedStudentId;
    private const int DoubleClickThresholdMs = 500;

    // Cursor dragging state
    private CursorViewModel? _draggingCursor;
    private bool _isDraggingCursor;
    private readonly CursorValidation _cursorValidation = new();

    public ViolinPlotControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;

        // Add click handler to the points overlay
        PointsOverlay.PointerPressed += OnPointsOverlayClick;

        // Render cursor column when its layout is updated (bounds become available)
        CursorColumnCanvas.LayoutUpdated += OnCursorColumnLayoutUpdated;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Re-render points after layout is complete with correct bounds
        if (DataContext is ViolinPlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            RenderPointsAsShapes();
            RenderRegionBands();
            RenderCursorColumn();
        }
    }

    private bool _cursorColumnHasRenderedOnce;
    private double _lastCursorColumnHeight;

    private void OnCursorColumnLayoutUpdated(object? sender, EventArgs e)
    {
        var height = CursorColumnCanvas.Bounds.Height;
        var width = CursorColumnCanvas.Bounds.Width;

        // Render when canvas gets valid bounds for the first time, or when height changes significantly
        if (height > 0 && width > 0 && DataContext is ViolinPlotViewModel vm && vm.Cursors != null && vm.Cursors.Count > 0)
        {
            // Only re-render if this is the first time or height changed significantly (avoid excessive renders)
            if (!_cursorColumnHasRenderedOnce || Math.Abs(height - _lastCursorColumnHeight) > 1)
            {
                _cursorColumnHasRenderedOnce = true;
                _lastCursorColumnHeight = height;
                RenderCursorColumn();
                Console.WriteLine($"[ViolinPlot] Cursor column rendered via LayoutUpdated: {width}x{height}");
            }
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Immediately reposition dots to match SVG scaling during resize
        if (DataContext is ViolinPlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            var plotBounds = ViolinPlotArea.Bounds;
            if (plotBounds.Width > 0 && plotBounds.Height > 0)
            {
                UpdateDotPositions(plotBounds.Width, plotBounds.Height);
            }
        }

        // Re-render region bands and cursor column on resize
        RenderRegionBands();
        RenderCursorColumn();

        // Cancel previous resize operation
        _resizeCts?.Cancel();
        _resizeCts = new CancellationTokenSource();
        var token = _resizeCts.Token;

        // Debounce: wait 150ms after resize finishes before regenerating full plot
        Task.Delay(150, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested && DataContext is ViolinPlotViewModel viewModel)
                    {
                        var plotBounds = ViolinPlotArea.Bounds;
                        var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
                        var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 400;

                        Console.WriteLine($"[ViolinPlot] Regenerating plot: {displayWidth}x{displayHeight}");

                        try
                        {
                            // Trigger full plot regeneration in ViewModel
                            viewModel.RegeneratePlot(displayWidth, displayHeight);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[ViolinPlot] Error regenerating plot: {ex.Message}");
                        }
                    }
                });
            }
        }, token);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ViolinPlotViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;

            // Check if SVG content already exists (set before this control was created)
            if (!string.IsNullOrEmpty(vm.SvgContent))
            {
                UpdateSvgDisplay(vm.SvgContent);
            }

            // Subscribe to cursor changes
            SubscribeToCursors(vm);

            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ViolinPlotViewModel.Cursors))
                {
                    SubscribeToCursors(vm);
                    RenderRegionBands();
                }
            };
        }
    }

    private void SubscribeToCursors(ViolinPlotViewModel vm)
    {
        if (vm.Cursors == null) return;

        foreach (var cursor in vm.Cursors)
        {
            cursor.PropertyChanged += OnCursorPropertyChanged;
        }
    }

    private void OnCursorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CursorViewModel.Score) ||
            e.PropertyName == nameof(CursorViewModel.IsEnabled))
        {
            RenderRegionBands();    // Update Canvas bands
            RenderCursorColumn();   // Update Canvas cursor column
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ViolinPlotViewModel vm) return;

        if (e.PropertyName == nameof(ViolinPlotViewModel.SvgContent))
        {
            UpdateSvgDisplay(vm.SvgContent);
        }
        else if (e.PropertyName == nameof(ViolinPlotViewModel.HoveredStudentId))
        {
            UpdateHoverVisualization(vm);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is ViolinPlotViewModel vm)
        {
            var position = e.GetPosition(this);
            var plotBounds = ViolinPlotArea.Bounds;
            var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
            var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 400;
            vm.OnPointerMoved(position, displayWidth, displayHeight);
        }
    }

    private void UpdateSvgDisplay(string? svgContent)
    {
        if (string.IsNullOrEmpty(svgContent))
            return;

        try
        {
            Console.WriteLine("[ViolinPlot] UpdateSvgDisplay called");

            // Write SVG to temp file for display
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotsesses_violin.svg");
            File.WriteAllText(tempPath, svgContent);

            // Load SVG into Image control
            var svgSource = Avalonia.Svg.Skia.SvgSource.Load(tempPath, null);
            var svgImage = new Avalonia.Svg.Skia.SvgImage { Source = svgSource };
            SvgView.Source = svgImage;

            // Delay rendering points slightly to let SVG settle
            Dispatcher.UIThread.Post(() =>
            {
                RenderPointsAsShapes();
                RenderRegionBands();
                RenderCursorColumn();
                Console.WriteLine("[ViolinPlot] Points, bands, and cursors re-rendered after SVG update");
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading SVG: {ex.Message}");
        }
    }

    private void RenderPointsAsShapes()
    {
        if (DataContext is not ViolinPlotViewModel vm)
            return;

        // Clear existing points
        PointsOverlay.Children.Clear();

        var allPoints = vm.GetAllPoints();
        if (!allPoints.Any())
            return;

        // Get actual rendered bounds - use control bounds as the display area
        var plotBounds = ViolinPlotArea.Bounds;
        var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
        var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 400;

        Console.WriteLine($"[ViolinPlot] RenderPointsAsShapes: Control bounds = {plotBounds.Width}x{plotBounds.Height}, Using {displayWidth}x{displayHeight}, Points count = {allPoints.Count}");

        for (int i = 0; i < allPoints.Count; i++)
        {
            var point = allPoints[i];

            // Calculate position using actual display size
            var (displayX, displayY) = vm.SvgToDisplayWithSize(point.X, point.Y, displayWidth, displayHeight);

            // Add larger transparent hit area (15x15) for easier clicking
            // Store both point index (for resize) and student ID (for click handling)
            var hitArea = new Ellipse
            {
                Width = 15,
                Height = 15,
                Fill = Brushes.Transparent,
                Tag = (i, point.StudentId)
            };
            Canvas.SetLeft(hitArea, displayX - 7.5);
            Canvas.SetTop(hitArea, displayY - 7.5);
            PointsOverlay.Children.Add(hitArea);

            // Add visible shape on top
            Control shape;
            if (!string.IsNullOrEmpty(point.Comment))
            {
                // Hollow square for students with comments
                var rect = new Rectangle
                {
                    Width = 5,
                    Height = 5,
                    Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(Color.Parse(point.Color)),
                    StrokeThickness = 1.5,
                    Tag = (i, point.StudentId)
                };
                shape = rect;
            }
            else
            {
                // Filled circle for students without comments
                var ellipse = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = new SolidColorBrush(Color.Parse(point.Color)),
                    Opacity = 0.8,
                    Tag = (i, point.StudentId)
                };
                shape = ellipse;
            }

            Canvas.SetLeft(shape, displayX - 2.5);
            Canvas.SetTop(shape, displayY - 2.5);

            PointsOverlay.Children.Add(shape);
        }
    }

    private void UpdateDotPositions(double displayWidth, double displayHeight)
    {
        if (DataContext is not ViolinPlotViewModel vm)
            return;

        var allPoints = vm.GetAllPoints();
        if (!allPoints.Any())
            return;

        // Update positions of existing shapes without clearing/recreating
        foreach (var child in PointsOverlay.Children.OfType<Control>())
        {
            if (child.Tag is ValueTuple<int, int> tag)
            {
                var (pointIndex, studentId) = tag;
                if (pointIndex >= 0 && pointIndex < allPoints.Count)
                {
                    var point = allPoints[pointIndex];
                    var (displayX, displayY) = vm.SvgToDisplayWithSize(point.X, point.Y, displayWidth, displayHeight);

                    // Determine if this is a hit area (15x15) or visual shape (5x5)
                    bool isHitArea = child.Width == 15;
                    double offset = isHitArea ? 7.5 : 2.5;

                    Canvas.SetLeft(child, displayX - offset);
                    Canvas.SetTop(child, displayY - offset);
                }
            }
        }
    }

    private void UpdateHoverVisualization(ViolinPlotViewModel vm)
    {
        // Clear tooltips
        TooltipsOverlay.Children.Clear();

        // Re-render all points in their correct positions
        RenderPointsAsShapes();

        // If hovering, dim non-hovered points and add ring overlays to hovered ones
        if (vm.HoveredStudentId.HasValue)
        {
            // Dim all points first (both ellipses and rectangles)
            foreach (var shape in PointsOverlay.Children.OfType<Control>())
            {
                if (shape is Ellipse ellipse)
                {
                    ellipse.Opacity = 0.45;
                }
                else if (shape is Rectangle rect)
                {
                    rect.Opacity = 0.45;
                }
            }

            // Get all points for this student
            var studentPoints = vm.GetPointsForStudent(vm.HoveredStudentId.Value);

            // Use actual display size
            var plotBounds = ViolinPlotArea.Bounds;
            var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
            var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 400;

            foreach (var point in studentPoints)
            {
                // Find the shape for this student using actual display coordinates
                var (displayX, displayY) = vm.SvgToDisplayWithSize(point.X, point.Y, displayWidth, displayHeight);

                var shape = PointsOverlay.Children.OfType<Control>()
                    .FirstOrDefault(s => s.Tag is ValueTuple<int, int> tag && tag.Item2 == point.StudentId &&
                                        Math.Abs(Canvas.GetLeft(s) - (displayX - 2.5)) < 1);

                if (shape != null)
                {
                    // Keep original shape at full opacity
                    if (shape is Ellipse ellipse)
                    {
                        ellipse.Opacity = 1.0;
                    }
                    else if (shape is Rectangle rect)
                    {
                        rect.Opacity = 1.0;
                    }

                    double ringSize = 14;
                    double ringThickness = 2;

                    var hoverRing = new Ellipse
                    {
                        Width = ringSize,
                        Height = ringSize,
                        Stroke = new SolidColorBrush(Color.Parse(point.Color)),
                        StrokeThickness = ringThickness
                    };

                    Canvas.SetLeft(hoverRing, displayX - ringSize / 2);
                    Canvas.SetTop(hoverRing, displayY - ringSize / 2);

                    PointsOverlay.Children.Add(hoverRing);
                }

                // Create tooltip
                CreateTooltip(point, displayX, displayY);
            }
        }
    }

    private void CreateTooltip(ViolinDataPoint point, double displayX, double displayY)
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
        var scoreColor = Color.Parse(point.Color);
        // Lighten if too dark
        double luminance = 0.2126 * scoreColor.R + 0.7152 * scoreColor.G + 0.0722 * scoreColor.B;
        if (luminance < 128)
        {
            double factor = 0.6;
            scoreColor = Color.FromRgb(
                (byte)(scoreColor.R + (255 - scoreColor.R) * factor),
                (byte)(scoreColor.G + (255 - scoreColor.G) * factor),
                (byte)(scoreColor.B + (255 - scoreColor.B) * factor));
        }

        var scoreText = new TextBlock
        {
            Text = Math.Round(point.Value).ToString(),
            FontSize = 11,
            Foreground = new SolidColorBrush(scoreColor)
        };

        tooltipBorder.Child = scoreText;

        // Measure tooltip to determine positioning
        tooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tooltipWidth = tooltipBorder.DesiredSize.Width;

        // Get canvas width
        double canvasWidth = TooltipsOverlay.Bounds.Width;

        // Position on left if too close to right edge, otherwise on right
        double leftPos = displayX + 20 + tooltipWidth > canvasWidth
            ? displayX - tooltipWidth - 20
            : displayX + 20;

        Canvas.SetLeft(tooltipBorder, leftPos);
        Canvas.SetTop(tooltipBorder, displayY - 10);

        TooltipsOverlay.Children.Add(tooltipBorder);
    }

    private void AnimateHover(Ellipse ellipse)
    {
        if (ellipse.RenderTransform is not ScaleTransform transform)
            return;

        var scaleXTransition = new DoubleTransition
        {
            Property = ScaleTransform.ScaleXProperty,
            Duration = TimeSpan.FromSeconds(0.5),
            Easing = new BounceEaseOut()
        };

        var scaleYTransition = new DoubleTransition
        {
            Property = ScaleTransform.ScaleYProperty,
            Duration = TimeSpan.FromSeconds(0.5),
            Easing = new BounceEaseOut()
        };

        transform.Transitions = new Transitions { scaleXTransition, scaleYTransition };
        transform.ScaleX = 3.0;
        transform.ScaleY = 3.0;
    }

    private void AnimateUnhover(Ellipse ellipse)
    {
        if (ellipse.RenderTransform is not ScaleTransform transform)
            return;

        var scaleXTransition = new DoubleTransition
        {
            Property = ScaleTransform.ScaleXProperty,
            Duration = TimeSpan.FromSeconds(0.25)
        };

        var scaleYTransition = new DoubleTransition
        {
            Property = ScaleTransform.ScaleYProperty,
            Duration = TimeSpan.FromSeconds(0.25)
        };

        transform.Transitions = new Transitions { scaleXTransition, scaleYTransition };
        transform.ScaleX = 1.0;
        transform.ScaleY = 1.0;
    }

    private void OnPointsOverlayClick(object? sender, PointerPressedEventArgs e)
    {
        var position = e.GetCurrentPoint(PointsOverlay);

        // Find clicked shape (ellipse or rectangle) using hit testing
        var clickedElement = PointsOverlay.InputHitTest(position.Position);

        int? studentId = null;
        if (clickedElement is Control control && control.Tag is ValueTuple<int, int> tag)
        {
            studentId = tag.Item2; // Second item is the student ID
        }

        if (studentId.HasValue)
        {
            // Handle right-click - open comment editor
            if (position.Properties.IsRightButtonPressed)
            {
                WeakReferenceMessenger.Default.Send(new EditStudentMessage(studentId.Value));
                e.Handled = true;
                return;
            }

            // Handle left-click - check for double-click
            if (position.Properties.IsLeftButtonPressed)
            {
                var now = DateTime.Now;
                var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;

                if (_lastClickedStudentId == studentId && timeSinceLastClick < DoubleClickThresholdMs)
                {
                    // Double-click detected - open comment editor
                    WeakReferenceMessenger.Default.Send(new EditStudentMessage(studentId.Value));
                    _lastClickedStudentId = null; // Reset to prevent triple-click
                    e.Handled = true;
                    return;
                }

                // Single click - record for potential double-click
                _lastClickTime = now;
                _lastClickedStudentId = studentId;
                e.Handled = true;
            }
        }
    }

    private void RenderRegionBands()
    {
        RegionBandsOverlay.Children.Clear();

        if (DataContext is not ViolinPlotViewModel vm || vm.Cursors == null) return;

        var height = RegionBandsOverlay.Bounds.Height;
        var width = RegionBandsOverlay.Bounds.Width;
        if (height <= 0 || width <= 0) return;

        // Get the Total series bounds - only draw bands over the Total column
        var totalBounds = vm.GetTotalSeriesDisplayBounds(width, height);
        if (totalBounds == null) return;

        var (bandLeft, bandRight) = totalBounds.Value;
        var bandWidth = bandRight - bandLeft;

        var enabledCursors = vm.Cursors.Where(c => c.IsEnabled).OrderBy(c => c.Score).ToList();
        if (!enabledCursors.Any()) return;

        var lowestGrade = enabledCursors.OrderByDescending(c => c.Grade.Order).FirstOrDefault();
        var cursorsWithLines = enabledCursors.Where(c => c != lowestGrade).OrderBy(c => c.Score).ToList();

        if (!cursorsWithLines.Any()) return;

        var lineBrush = new SolidColorBrush(Color.FromArgb(0x80, 255, 255, 255));

        // Draw horizontal lines at each cursor position (grade boundary)
        foreach (var cursor in cursorsWithLines)
        {
            var y = vm.ScoreToDisplayY(cursor.Score, height);

            var line = new Line
            {
                StartPoint = new Point(bandLeft, y),
                EndPoint = new Point(bandRight, y),
                Stroke = lineBrush,
                StrokeThickness = 1
            };
            RegionBandsOverlay.Children.Add(line);
        }
    }

    /// <summary>
    /// Renders the cursor column with horizontal cursor lines and grade labels.
    /// </summary>
    private void RenderCursorColumn()
    {
        CursorColumnCanvas.Children.Clear();

        if (DataContext is not ViolinPlotViewModel vm || vm.Cursors == null) return;

        var height = CursorColumnCanvas.Bounds.Height;
        var width = CursorColumnCanvas.Bounds.Width;
        if (height <= 0 || width <= 0) return;

        var enabledCursors = vm.Cursors.Where(c => c.IsEnabled).OrderBy(c => c.Score).ToList();
        if (!enabledCursors.Any()) return;

        var lowestGrade = enabledCursors.OrderByDescending(c => c.Grade.Order).FirstOrDefault();
        var cursorsWithLines = enabledCursors.Where(c => c != lowestGrade).OrderBy(c => c.Score).ToList();

        // Draw horizontal dashed cursor lines (excluding lowest grade)
        var lineBrush = new SolidColorBrush(Colors.White);
        foreach (var cursor in cursorsWithLines)
        {
            var y = vm.ScoreToDisplayY(cursor.Score, height);

            var line = new Line
            {
                StartPoint = new Point(0, y),
                EndPoint = new Point(width, y),
                Stroke = lineBrush,
                StrokeThickness = 2,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 2 }
            };
            CursorColumnCanvas.Children.Add(line);
        }

        // Draw grade labels
        // Top grade (A): locked just below its cursor line
        // Bottom grade (F): locked just above the plot bottom (no cursor)
        // Middle grades: centered between their cursor lines
        var enabledGrades = enabledCursors.Select(c => c.Grade).OrderBy(g => g.Order).ToList();

        // Get plot area bounds for label positioning
        var plotTop = vm.GetPlotAreaTopFraction() * height;
        var plotBottom = vm.GetPlotAreaBottomFraction() * height;

        const double labelOffset = 4; // pixels from cursor line

        for (int i = 0; i < enabledGrades.Count; i++)
        {
            var grade = enabledGrades[i];

            var label = new TextBlock
            {
                Text = grade.DisplayName,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                TextAlignment = TextAlignment.Center
            };

            // Measure first to get height
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var labelHeight = label.DesiredSize.Height;

            double labelY;

            if (i == 0)
            {
                // Top grade (A): position ABOVE its cursor line
                var cursor = enabledCursors.FirstOrDefault(c => c.Grade.Order == grade.Order);
                if (cursor != null)
                {
                    var cursorY = vm.ScoreToDisplayY(cursor.Score, height);
                    labelY = cursorY - labelHeight - labelOffset; // Above the cursor line
                }
                else
                {
                    labelY = plotTop + labelOffset;
                }
            }
            else if (i == enabledGrades.Count - 1)
            {
                // Bottom grade (F): position BELOW the lowest cursor line
                // The lowest cursor is the one that separates this grade from the one above
                var lowestCursor = cursorsWithLines.FirstOrDefault(); // First = lowest score
                if (lowestCursor != null)
                {
                    var cursorY = vm.ScoreToDisplayY(lowestCursor.Score, height);
                    labelY = cursorY + labelOffset; // Below the cursor line
                }
                else
                {
                    labelY = plotBottom + labelOffset;
                }
            }
            else
            {
                // Middle grades: centered between adjacent cursors
                var cursorAbove = enabledCursors.FirstOrDefault(c => c.Grade.Order == enabledGrades[i - 1].Order);
                var cursorBelow = enabledCursors.FirstOrDefault(c => c.Grade.Order == grade.Order);
                if (cursorAbove != null && cursorBelow != null)
                {
                    var aboveY = vm.ScoreToDisplayY(cursorAbove.Score, height);
                    var belowY = vm.ScoreToDisplayY(cursorBelow.Score, height);
                    labelY = (aboveY + belowY) / 2.0 - labelHeight / 2.0;
                }
                else
                {
                    continue;
                }
            }

            Canvas.SetLeft(label, (width - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, labelY);

            CursorColumnCanvas.Children.Add(label);
        }
    }

    private void OnCursorColumnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ViolinPlotViewModel vm || vm.Cursors == null) return;

        var point = e.GetCurrentPoint(CursorColumnCanvas);
        if (!point.Properties.IsLeftButtonPressed) return;

        var height = CursorColumnCanvas.Bounds.Height;
        if (height <= 0) return;

        var clickY = point.Position.Y;

        // Find nearest cursor line
        var lowestGrade = vm.Cursors.Where(c => c.IsEnabled)
            .OrderByDescending(c => c.Grade.Order).FirstOrDefault();

        CursorViewModel? nearest = null;
        double minDist = double.MaxValue;

        foreach (var cursor in vm.Cursors.Where(c => c.IsEnabled && c != lowestGrade))
        {
            var cursorY = vm.ScoreToDisplayY(cursor.Score, height);
            var dist = Math.Abs(cursorY - clickY);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = cursor;
            }
        }

        // Start dragging if close enough (within 10 pixels)
        if (nearest != null && minDist < 10)
        {
            _draggingCursor = nearest;
            _isDraggingCursor = true;
            e.Pointer.Capture(CursorColumnCanvas);
            e.Handled = true;
        }
    }

    private void OnCursorColumnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingCursor || _draggingCursor == null) return;
        if (DataContext is not ViolinPlotViewModel vm || vm.Cursors == null) return;

        var height = CursorColumnCanvas.Bounds.Height;
        if (height <= 0) return;

        var point = e.GetCurrentPoint(CursorColumnCanvas);
        var mouseY = point.Position.Y;

        // Convert display Y to score using plot area bounds
        var plotTop = vm.GetPlotAreaTopFraction() * height;
        var plotBottom = vm.GetPlotAreaBottomFraction() * height;
        var plotHeight = plotBottom - plotTop;

        if (plotHeight <= 0) return;

        // Clamp mouseY to plot area
        mouseY = Math.Max(plotTop, Math.Min(plotBottom, mouseY));

        // Convert Y to normalized value (inverted: top = 1.0, bottom = 0.0)
        var normalized = 1.0 - (mouseY - plotTop) / plotHeight;

        // Convert to raw score
        var newScore = vm.NormalizedToScore(normalized);

        // Build cutoffs with proposed position
        var allCutoffs = vm.Cursors
            .Where(c => c.IsEnabled)
            .Select(c => new GradeCutoff(c.Grade, c == _draggingCursor ? newScore : c.Score))
            .ToList();

        // Validate movement
        var validated = _cursorValidation.ValidateMovement(
            _draggingCursor.Grade, newScore, allCutoffs, vm.MinScore - 1, vm.MaxScore + 1);

        _draggingCursor.Score = validated;
        e.Handled = true;
    }

    private void OnCursorColumnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingCursor)
        {
            e.Pointer.Capture(null);
            _isDraggingCursor = false;
            _draggingCursor = null;
            e.Handled = true;
        }
    }
}
