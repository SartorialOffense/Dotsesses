using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dotsesses.Models;
using Dotsesses.ViewModels;

namespace Dotsesses.Views;

public partial class ViolinPlotControl : UserControl
{
    public ViolinPlotControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
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
        // Re-render points when control is resized
        if (DataContext is ViolinPlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            RenderPointsAsShapes();
        }
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
            // Write SVG to temp file for display
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotsesses_violin.svg");
            File.WriteAllText(tempPath, svgContent);

            // Load SVG into Image control
            var svgSource = Avalonia.Svg.Skia.SvgSource.Load(tempPath, null);
            var svgImage = new Avalonia.Svg.Skia.SvgImage { Source = svgSource };
            SvgView.Source = svgImage;

            // Render points as Avalonia shapes
            RenderPointsAsShapes();
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

        foreach (var point in allPoints)
        {
            // Calculate position using actual display size
            var (displayX, displayY) = vm.SvgToDisplayWithSize(point.X, point.Y, displayWidth, displayHeight);

            var ellipse = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Color.Parse(point.Color)),
                Opacity = 0.8,
                Tag = point.StudentId
            };

            Canvas.SetLeft(ellipse, displayX - 2.5);
            Canvas.SetTop(ellipse, displayY - 2.5);

            PointsOverlay.Children.Add(ellipse);
        }
    }

    private void UpdateHoverVisualization(ViolinPlotViewModel vm)
    {
        // Clear tooltips
        TooltipsOverlay.Children.Clear();

        // Re-render all points in their correct positions
        RenderPointsAsShapes();

        // If hovering, dim non-hovered points and highlight hovered ones
        if (vm.HoveredStudentId.HasValue)
        {
            // Dim all points first
            foreach (var ellipse in PointsOverlay.Children.OfType<Ellipse>())
            {
                ellipse.Opacity = 0.6;
            }

            // Get all points for this student
            var studentPoints = vm.GetPointsForStudent(vm.HoveredStudentId.Value);

            // Use actual display size
            var controlBounds = Bounds;
            var displayWidth = controlBounds.Width > 0 ? controlBounds.Width : 800;
            var displayHeight = controlBounds.Height > 0 ? controlBounds.Height : 400;

            foreach (var point in studentPoints)
            {
                // Find the ellipse for this student using actual display coordinates
                var (displayX, displayY) = vm.SvgToDisplayWithSize(point.X, point.Y, displayWidth, displayHeight);

                var ellipse = PointsOverlay.Children.OfType<Ellipse>()
                    .FirstOrDefault(e => (int?)e.Tag == point.StudentId &&
                                        Math.Abs(Canvas.GetLeft(e) - (displayX - 2.5)) < 1);

                if (ellipse != null)
                {
                    // Highlight this ellipse
                    ellipse.Opacity = 1.0;
                    ellipse.Width = 15;  // 3x size
                    ellipse.Height = 15;
                    Canvas.SetLeft(ellipse, displayX - 7.5);
                    Canvas.SetTop(ellipse, displayY - 7.5);
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

        var stackPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6
        };

        // Student ID
        var idText = new TextBlock
        {
            Text = $"S{point.StudentId:D3}",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Colors.White)
        };

        // Score value
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

        stackPanel.Children.Add(idText);
        stackPanel.Children.Add(scoreText);
        tooltipBorder.Child = stackPanel;

        Canvas.SetLeft(tooltipBorder, displayX + 20);
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
}
