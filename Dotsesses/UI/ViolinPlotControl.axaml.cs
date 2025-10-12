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
using Dotsesses.UI;

namespace Dotsesses.UI;

public partial class ViolinPlotControl : UserControl
{
    private CancellationTokenSource? _resizeCts;

    // Double-click tracking
    private DateTime _lastClickTime;
    private int? _lastClickedStudentId;
    private const int DoubleClickThresholdMs = 500;

    public ViolinPlotControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;

        // Add click handler to the points overlay
        PointsOverlay.PointerPressed += OnPointsOverlayClick;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Re-render points after layout is complete with correct bounds
        if (DataContext is ViolinPlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            RenderPointsAsShapes();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Immediately reposition dots to match SVG scaling during resize
        if (DataContext is ViolinPlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            var controlBounds = Bounds;
            if (controlBounds.Width > 0 && controlBounds.Height > 0)
            {
                UpdateDotPositions(controlBounds.Width, controlBounds.Height);
            }
        }

        // Cancel previous resize operation
        _resizeCts?.Cancel();
        _resizeCts = new CancellationTokenSource();
        var token = _resizeCts.Token;

        // Debounce: wait 300ms after resize finishes before regenerating full plot
        Task.Delay(300, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested && DataContext is ViolinPlotViewModel viewModel)
                    {
                        var controlBounds = Bounds;
                        var displayWidth = controlBounds.Width > 0 ? controlBounds.Width : 800;
                        var displayHeight = controlBounds.Height > 0 ? controlBounds.Height : 400;

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
            var controlBounds = Bounds;
            var displayWidth = controlBounds.Width > 0 ? controlBounds.Width : 800;
            var displayHeight = controlBounds.Height > 0 ? controlBounds.Height : 400;
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
                Console.WriteLine("[ViolinPlot] Points re-rendered after SVG update");
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
        var controlBounds = Bounds;
        var displayWidth = controlBounds.Width > 0 ? controlBounds.Width : 800;
        var displayHeight = controlBounds.Height > 0 ? controlBounds.Height : 400;

        Console.WriteLine($"[ViolinPlot] RenderPointsAsShapes: Control bounds = {controlBounds.Width}x{controlBounds.Height}, Using {displayWidth}x{displayHeight}, Points count = {allPoints.Count}");

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
            var controlBounds = Bounds;
            var displayWidth = controlBounds.Width > 0 ? controlBounds.Width : 800;
            var displayHeight = controlBounds.Height > 0 ? controlBounds.Height : 400;

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
}
