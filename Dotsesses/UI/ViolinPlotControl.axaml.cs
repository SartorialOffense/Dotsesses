using System;
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
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;
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
    private CutoffSlot? _draggingCursor;
    private bool _isDraggingCursor;

    // Current rendering theme
    private ThemeName _currentTheme = ThemeName.DarkMode;

    public ViolinPlotControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;

        // Add click handler to the points overlay
        PointsOverlay.PointerPressed += OnPointsOverlayClick;

        // Add copy button click handler
        CopyViolinPlotButton.Click += OnCopyViolinPlotClick;

        // Render cursor column when its layout is updated (bounds become available)
        CursorColumnCanvas.LayoutUpdated += OnCursorColumnLayoutUpdated;
        // Theme-change registration moves to OnDataContextChanged — it needs
        // the scoped IMessenger from the VM (per ADR-0012).
    }

    private bool _messengerRegistered;

    private void OnRenderWithThemeMessage(object recipient, RenderWithThemeMessage message)
    {
        _currentTheme = message.Theme;

        // Update background color based on theme
        Background = ThemeColors.BackgroundBrush(_currentTheme);

        // Hide/show copy button based on theme (hide during export)
        CopyViolinPlotButton.IsVisible = _currentTheme == ThemeName.DarkMode;

        // Re-render all visual elements with new theme
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is ViolinPlotViewModel vm)
            {
                // Regenerate the entire plot (including SVG from Python) with the new theme
                var width = ViolinPlotArea.Bounds.Width;
                var height = ViolinPlotArea.Bounds.Height;
                if (width > 0 && height > 0)
                {
                    vm.RegeneratePlot(width, height, _currentTheme);

                    // Update the SVG display with new content
                    if (!string.IsNullOrEmpty(vm.SvgContent))
                    {
                        UpdateSvgDisplay(vm.SvgContent);
                    }
                }

                UpdateHoverVisualization(vm);
            }
            else
            {
                RenderPointsAsShapes();
            }
            RenderRegionBands();
            RenderCursorColumn();

            // Invoke callback after rendering is complete
            message.OnRenderComplete?.Invoke();
        }, DispatcherPriority.Render);
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
        if (height > 0 && width > 0 && DataContext is ViolinPlotViewModel vm && vm.GradingSession is { } session && session.Slots.Count > 0)
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

            // Re-render hover visualization if a student is hovered
            if (vm.HoveredStudentId.HasValue)
            {
                UpdateHoverVisualization(vm);
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

    private GradingSession? _subscribedSession;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ViolinPlotViewModel vm)
        {
            if (!_messengerRegistered)
            {
                _messengerRegistered = true;
                vm.Messenger.Register<RenderWithThemeMessage>(this, OnRenderWithThemeMessage);
            }

            vm.PropertyChanged += OnViewModelPropertyChanged;

            // Check if SVG content already exists (set before this control was created)
            if (!string.IsNullOrEmpty(vm.SvgContent))
            {
                UpdateSvgDisplay(vm.SvgContent);
            }

            SubscribeToSession(vm.GradingSession);

            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ViolinPlotViewModel.GradingSession))
                {
                    SubscribeToSession(vm.GradingSession);
                    RenderRegionBands();
                    RenderCursorColumn();
                }
                else if (args.PropertyName == nameof(ViolinPlotViewModel.ComplianceGrid))
                {
                    RenderCursorColumn();
                }
            };
        }
    }

    private void SubscribeToSession(GradingSession? session)
    {
        if (_subscribedSession is not null)
        {
            _subscribedSession.PropertyChanged -= OnSessionPropertyChanged;
        }
        _subscribedSession = session;
        if (_subscribedSession is not null)
        {
            _subscribedSession.PropertyChanged += OnSessionPropertyChanged;
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Session emits LastChange whenever cursor positions or enabled
        // grades shift. Re-render both surfaces so handles AND the
        // count/deviation column track the change without an extra
        // ComplianceRow subscription — the counts are read from
        // ComplianceGrid.Rows during RenderCursorColumn.
        if (e.PropertyName != nameof(GradingSession.LastChange)) return;
        RenderRegionBands();
        RenderCursorColumn();
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
                // Use UpdateHoverVisualization instead of RenderPointsAsShapes directly
                // to preserve hover state (ring, tooltips, comments) after resize regeneration
                if (DataContext is ViolinPlotViewModel viewModel)
                {
                    UpdateHoverVisualization(viewModel);
                }
                else
                {
                    RenderPointsAsShapes();
                }
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
            // Use live comment to determine shape (hollow square for comments, filled circle otherwise)
            var liveComment = vm.GetLiveComment(point.StudentId, point.Series);
            Control shape;
            if (!string.IsNullOrEmpty(liveComment))
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
                    Opacity = 1.0,
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
        // Clear tooltips and comments
        TooltipsOverlay.Children.Clear();
        CommentsOverlay.Children.Clear();

        // Re-render all points in their correct positions
        RenderPointsAsShapes();

        // If hovering, add ring overlays to hovered points
        if (vm.HoveredStudentId.HasValue)
        {
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

                // Create comment at top or bottom based on series index, centered on series X position
                // Use live comment from ClassAssessment instead of cached point.Comment
                var liveComment = vm.GetLiveComment(point.StudentId, point.Series);
                if (!string.IsNullOrEmpty(liveComment))
                {
                    // Find series index to determine top vs bottom positioning
                    var seriesIndex = studentPoints.IndexOf(point);
                    CreateSeriesComment(point, liveComment, displayX, displayHeight, seriesIndex);
                }
            }
        }
    }

    private void CreateSeriesComment(ViolinDataPoint point, string comment, double displayX, double displayHeight, int seriesIndex)
    {
        if (DataContext is not ViolinPlotViewModel vm) return;

        // Parse and lighten color if too dark
        var seriesColor = Color.Parse(point.Color);
        double luminance = 0.2126 * seriesColor.R + 0.7152 * seriesColor.G + 0.0722 * seriesColor.B;
        if (luminance < 128)
        {
            double factor = 0.6;
            seriesColor = Color.FromRgb(
                (byte)(seriesColor.R + (255 - seriesColor.R) * factor),
                (byte)(seriesColor.G + (255 - seriesColor.G) * factor),
                (byte)(seriesColor.B + (255 - seriesColor.B) * factor));
        }

        var commentBorder = new Border
        {
            Background = ThemeColors.BackgroundBrush(_currentTheme),
            BorderBrush = ThemeColors.BorderBrush(_currentTheme),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 3),
            CornerRadius = new CornerRadius(3)
        };

        var commentBlock = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Spacing = 1 };

        // Series name header
        var seriesHeader = new TextBlock
        {
            Text = point.Series,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(seriesColor)
        };
        commentBlock.Children.Add(seriesHeader);

        // Comment lines with bullets
        var commentLines = comment.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line));

        foreach (var line in commentLines)
        {
            var lineText = new TextBlock
            {
                Text = $"● {line}",
                FontSize = 12,
                Foreground = ThemeColors.ForegroundBrush(_currentTheme),
                TextWrapping = TextWrapping.NoWrap
            };
            commentBlock.Children.Add(lineText);
        }

        commentBorder.Child = commentBlock;

        // Measure to get dimensions for centering
        commentBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var commentWidth = commentBorder.DesiredSize.Width;
        var commentHeight = commentBorder.DesiredSize.Height;

        // Position based on series index: even at bottom (-0.3), odd at top (1.3)
        bool isBottom = seriesIndex % 2 == 0;
        var normalizedY = isBottom ? -0.3 : 1.3;
        var commentY = vm.NormalizedYToDisplayY(normalizedY, displayHeight);

        Canvas.SetLeft(commentBorder, displayX - commentWidth / 2);
        if (isBottom)
        {
            // Bottom: position bottom of comment at -0.2
            Canvas.SetTop(commentBorder, commentY - commentHeight);
        }
        else
        {
            // Top: position top of comment at 1.2
            Canvas.SetTop(commentBorder, commentY);
        }

        CommentsOverlay.Children.Add(commentBorder);
    }

    private void CreateTooltip(ViolinDataPoint point, double displayX, double displayY)
    {
        var tooltipBorder = new Border
        {
            Background = ThemeColors.BackgroundBrush(_currentTheme),
            BorderBrush = ThemeColors.BorderBrush(_currentTheme),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2)
        };

        // Use fully saturated series color
        var scoreColor = Color.Parse(point.Color);
        var colorBrush = new SolidColorBrush(scoreColor);
        var whiteBrush = new SolidColorBrush(Colors.White);

        // Build tooltip with white pipe separators
        var sigmaSign = point.SigmaValue >= 0 ? "+" : "";

        var scoreText = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeight.Bold
        };

        // Score value (colored)
        scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run($"{Math.Round(point.Value)} ") { Foreground = colorBrush });
        // Pipe (white)
        scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run("| ") { Foreground = whiteBrush });
        // Sigma value (colored)
        scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run($"{sigmaSign}{point.SigmaValue:F1}σ") { Foreground = colorBrush });

        // For Total series, also include the letter grade
        if (point.Series.Equals("Total", StringComparison.OrdinalIgnoreCase) && DataContext is ViolinPlotViewModel vm)
        {
            var grade = vm.GetGradeForScore(point.Value);
            if (!string.IsNullOrEmpty(grade))
            {
                // Pipe (white)
                scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run(" | ") { Foreground = whiteBrush });
                // Grade (colored)
                scoreText.Inlines.Add(new Avalonia.Controls.Documents.Run(grade) { Foreground = colorBrush });
            }
        }

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

        var messenger = (DataContext as ViolinPlotViewModel)?.Messenger;
        if (messenger == null) return;

        if (studentId.HasValue)
        {
            // Handle right-click - open comment editor
            if (position.Properties.IsRightButtonPressed)
            {
                messenger.Send(new EditStudentMessage(studentId.Value));
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
                    messenger.Send(new EditStudentMessage(studentId.Value));
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
        else
        {
            // Clicked on empty space - clear hover
            if (position.Properties.IsLeftButtonPressed)
            {
                messenger.Send(new StudentHoverMessage(null, "violin"));
                e.Handled = true;
            }
        }
    }

    // Barbell cursor state
    private CutoffSlot? _draggingBarbellCursor;
    private bool _isDraggingBarbell;
    private const double BarbellHandleSize = 8;

    private void RenderRegionBands()
    {
        RegionBandsOverlay.Children.Clear();

        if (DataContext is not ViolinPlotViewModel vm || vm.GradingSession is not { } session) return;

        var height = RegionBandsOverlay.Bounds.Height;
        var width = RegionBandsOverlay.Bounds.Width;
        if (height <= 0 || width <= 0) return;

        // Get the Total series bounds - only draw barbells over the Total column
        var totalBounds = vm.GetTotalSeriesDisplayBounds(width, height);
        if (totalBounds == null) return;

        var (bandLeft, bandRight) = totalBounds.Value;

        // Slots structurally exclude the catch-all (ADR-0011), so every
        // enabled slot here gets a barbell — no lowest-Order exclusion.
        var enabledSlots = session.Slots.Where(s => s.IsEnabled).OrderBy(s => s.Score).ToList();
        if (!enabledSlots.Any()) return;

        var lineBrush = ThemeColors.TransparentLineBrush(_currentTheme);
        var handleBrush = ThemeColors.ForegroundBrush(_currentTheme);
        var handleFill = ThemeColors.BackgroundBrush(_currentTheme);

        // Draw barbell cursors at each grade boundary
        foreach (var slot in enabledSlots)
        {
            var y = vm.ScoreToDisplayY(slot.Score, height);

            // Draw the horizontal line (bar) - 50% transparent
            var line = new Line
            {
                StartPoint = new Point(bandLeft, y),
                EndPoint = new Point(bandRight, y),
                Stroke = lineBrush,
                StrokeThickness = 1,
                Tag = slot,
                IsHitTestVisible = false
            };
            RegionBandsOverlay.Children.Add(line);

            // Left handle (square) - moved inward towards series center, hollow
            var leftHandle = new Rectangle
            {
                Width = BarbellHandleSize,
                Height = BarbellHandleSize,
                Fill = handleFill,
                Stroke = handleBrush,
                StrokeThickness = 2,
                Tag = slot,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth)
            };
            Canvas.SetLeft(leftHandle, bandLeft); // Inward from left edge
            Canvas.SetTop(leftHandle, y - BarbellHandleSize / 2);
            RegionBandsOverlay.Children.Add(leftHandle);

            // Right handle (square) - moved inward towards series center, hollow
            var rightHandle = new Rectangle
            {
                Width = BarbellHandleSize,
                Height = BarbellHandleSize,
                Fill = handleFill,
                Stroke = handleBrush,
                StrokeThickness = 2,
                Tag = slot,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeNorthSouth)
            };
            Canvas.SetLeft(rightHandle, bandRight - BarbellHandleSize); // Inward from right edge
            Canvas.SetTop(rightHandle, y - BarbellHandleSize / 2);
            RegionBandsOverlay.Children.Add(rightHandle);
        }
    }

    private void OnBarbellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ViolinPlotViewModel vm || vm.GradingSession is null) return;

        var point = e.GetCurrentPoint(RegionBandsOverlay);
        if (!point.Properties.IsLeftButtonPressed) return;

        // Check if we clicked on a handle
        var clickedElement = RegionBandsOverlay.InputHitTest(point.Position);
        if (clickedElement is Rectangle rect && rect.Tag is CutoffSlot slot)
        {
            _draggingBarbellCursor = slot;
            _isDraggingBarbell = true;
            e.Pointer.Capture(RegionBandsOverlay);
            e.Handled = true;
        }
        else
        {
            // Clicked on empty space - clear hover
            (DataContext as ViolinPlotViewModel)?.Messenger.Send(new StudentHoverMessage(null, "violin"));
            e.Handled = true;
        }
    }

    private void OnBarbellPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingBarbell || _draggingBarbellCursor == null) return;
        if (DataContext is not ViolinPlotViewModel vm || vm.GradingSession is not { } session) return;

        var height = RegionBandsOverlay.Bounds.Height;
        if (height <= 0) return;

        var point = e.GetCurrentPoint(RegionBandsOverlay);
        var mouseY = point.Position.Y;

        // Convert display Y to score using plot area bounds
        var plotTop = vm.GetPlotAreaTopFraction() * height;
        var plotBottom = vm.GetPlotAreaBottomFraction() * height;
        var plotHeight = plotBottom - plotTop;

        if (plotHeight <= 0) return;

        // Convert Y to normalized value (inverted: top = 1.0, bottom = 0.0)
        // Don't clamp mouseY - allow dragging beyond plot area for ±20 range
        var normalized = 1.0 - (mouseY - plotTop) / plotHeight;

        // Convert to raw score (can be beyond MinScore/MaxScore)
        var newScore = vm.NormalizedToScore(normalized);

        // Validation lives on the session — invalid moves are rejected and
        // leave the slot at its last valid position.
        session.MoveCutoff(_draggingBarbellCursor.Grade, newScore, this);
        e.Handled = true;
    }

    private void OnBarbellPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingBarbell)
        {
            e.Pointer.Capture(null);
            _isDraggingBarbell = false;
            _draggingBarbellCursor = null;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Renders the cursor column with grade labels only (cursor lines replaced by barbells).
    /// </summary>
    private void RenderCursorColumn()
    {
        CursorColumnCanvas.Children.Clear();

        if (DataContext is not ViolinPlotViewModel vm || vm.GradingSession is not { } session) return;

        var height = CursorColumnCanvas.Bounds.Height;
        var width = CursorColumnCanvas.Bounds.Width;
        if (height <= 0 || width <= 0) return;

        // Slots structurally exclude the catch-all (ADR-0011).
        var enabledSlots = session.Slots.Where(s => s.IsEnabled).OrderBy(s => s.Score).ToList();
        if (!enabledSlots.Any()) return;

        // Labels include the catch-all so the bottom grade is named.
        var enabledGrades = session.CurrentState.EnabledGrades.OrderBy(g => g.Order).ToList();

        // Get plot area bounds for label positioning
        var plotTop = vm.GetPlotAreaTopFraction() * height;
        var plotBottom = vm.GetPlotAreaBottomFraction() * height;

        const double labelOffset = 28; // pixels from cursor line (3x original spacing)
        const double countColWidth = 16; // fixed width for count column
        const double deviationColWidth = 26; // fixed width for deviation column
        const double colSpacing = 2;

        for (int i = 0; i < enabledGrades.Count; i++)
        {
            var grade = enabledGrades[i];

            // Get compliance data for this grade
            var compliance = vm.ComplianceGrid?.Rows.FirstOrDefault(r => r.Grade.Equals(grade));
            var currentCount = compliance?.CurrentCount ?? 0;
            var deviation = compliance?.SignedDeviation ?? 0;

            // Create container with fixed-width columns
            var container = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = colSpacing
            };

            // Grade column: white text with tight white border box
            var gradeBox = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = ThemeColors.ForegroundBrush(_currentTheme),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2, 0),
                Child = new TextBlock
                {
                    Text = grade.DisplayName,
                    Foreground = ThemeColors.ForegroundBrush(_currentTheme),
                    FontSize = 11,
                    FontWeight = FontWeight.Bold,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };

            // Add tooltip with compliance info
            if (compliance != null)
            {
                var tooltipText = $"Target: {compliance.LowerTarget}-{compliance.UpperTarget}\nCurrent: {currentCount}";
                if (deviation != 0)
                {
                    var deviationSign = deviation > 0 ? "+" : "";
                    tooltipText += $"\nDelta: {deviationSign}{deviation}";
                }
                ToolTip.SetTip(gradeBox, tooltipText);
            }

            container.Children.Add(gradeBox);

            // Count column: right-aligned
            var countText = new TextBlock
            {
                Text = currentCount.ToString(),
                Foreground = ThemeColors.ForegroundBrush(_currentTheme),
                FontSize = 11,
                Width = countColWidth,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            container.Children.Add(countText);

            // Deviation column: right-aligned, fixed width (show empty if zero)
            IBrush deviationColor = deviation < 0
                ? new SolidColorBrush(Color.FromRgb(255, 100, 100)) // Red for under
                : deviation > 0
                    ? new SolidColorBrush(Color.FromRgb(100, 255, 100)) // Green for over
                    : Brushes.Transparent;

            var deviationText = new TextBlock
            {
                Text = deviation != 0 ? $"[{deviation:+#;-#;0}]" : "",
                Foreground = deviationColor,
                FontSize = 11,
                Width = deviationColWidth,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            container.Children.Add(deviationText);

            // Measure container to get dimensions
            container.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var containerHeight = container.DesiredSize.Height;
            var containerWidth = container.DesiredSize.Width;

            double labelY;

            if (i == 0)
            {
                // Top grade (A): position above its cursor line with offset
                var slot = enabledSlots.FirstOrDefault(s => s.Grade.Order == grade.Order);
                if (slot != null)
                {
                    var cursorY = vm.ScoreToDisplayY(slot.Score, height);
                    // Position label above cursor (smaller Y = higher on screen)
                    labelY = cursorY - labelOffset - containerHeight;
                }
                else
                {
                    labelY = plotTop;
                }
            }
            else if (i == enabledGrades.Count - 1)
            {
                // Bottom grade (catch-all): position BELOW the lowest cursor line
                var lowestSlot = enabledSlots.FirstOrDefault(); // First = lowest score
                if (lowestSlot != null)
                {
                    var cursorY = vm.ScoreToDisplayY(lowestSlot.Score, height);
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
                var slotAbove = enabledSlots.FirstOrDefault(s => s.Grade.Order == enabledGrades[i - 1].Order);
                var slotBelow = enabledSlots.FirstOrDefault(s => s.Grade.Order == grade.Order);
                if (slotAbove != null && slotBelow != null)
                {
                    var aboveY = vm.ScoreToDisplayY(slotAbove.Score, height);
                    var belowY = vm.ScoreToDisplayY(slotBelow.Score, height);
                    labelY = (aboveY + belowY) / 2.0 - containerHeight / 2.0;
                }
                else
                {
                    continue;
                }
            }

            // Left align with minimal margin
            Canvas.SetLeft(container, 1);
            Canvas.SetTop(container, labelY);

            CursorColumnCanvas.Children.Add(container);
        }
    }

    private void OnCursorColumnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ViolinPlotViewModel vm || vm.GradingSession is not { } session) return;

        var point = e.GetCurrentPoint(CursorColumnCanvas);
        if (!point.Properties.IsLeftButtonPressed) return;

        var height = CursorColumnCanvas.Bounds.Height;
        if (height <= 0) return;

        var clickY = point.Position.Y;

        // Find nearest slot line. Slots already exclude the catch-all.
        CutoffSlot? nearest = null;
        double minDist = double.MaxValue;

        foreach (var slot in session.Slots.Where(s => s.IsEnabled))
        {
            var cursorY = vm.ScoreToDisplayY(slot.Score, height);
            var dist = Math.Abs(cursorY - clickY);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = slot;
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
        if (DataContext is not ViolinPlotViewModel vm || vm.GradingSession is not { } session) return;

        var height = CursorColumnCanvas.Bounds.Height;
        if (height <= 0) return;

        var point = e.GetCurrentPoint(CursorColumnCanvas);
        var mouseY = point.Position.Y;

        // Convert display Y to score using plot area bounds
        var plotTop = vm.GetPlotAreaTopFraction() * height;
        var plotBottom = vm.GetPlotAreaBottomFraction() * height;
        var plotHeight = plotBottom - plotTop;

        if (plotHeight <= 0) return;

        // Convert Y to normalized value (inverted: top = 1.0, bottom = 0.0)
        // Don't clamp mouseY - allow dragging beyond plot area for ±20 range
        var normalized = 1.0 - (mouseY - plotTop) / plotHeight;

        // Convert to raw score (can be beyond MinScore/MaxScore)
        var newScore = vm.NormalizedToScore(normalized);

        // Validation lives on the session — invalid moves are rejected and
        // leave the slot at its last valid position.
        session.MoveCutoff(_draggingCursor.Grade, newScore, this);
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

    private async void OnCopyViolinPlotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var clipboard = topLevel?.Clipboard;
        if (clipboard == null) return;
        if (DataContext is not ViolinPlotViewModel vm) return;

        // Copy the entire control (includes all overlays)
        await ImageCopyService.CopyControlToClipboardAsync(this, clipboard, vm.Messenger);
    }
}
