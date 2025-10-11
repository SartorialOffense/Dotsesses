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
    private Ellipse? _currentlyHoveredEllipse;

    public ViolinPlotControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ViolinPlotViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
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
            vm.OnPointerMoved(position);
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

        var allPoints = vm.GetPointsForStudent(0); // Get all points (studentId 0 won't exist, so this is a hack - need better API)
        // TODO: Add method to ViolinPlotViewModel to get all points

        // For now, we'll render points when we implement hover
        // This will be completed in the hover visualization method
    }

    private void UpdateHoverVisualization(ViolinPlotViewModel vm)
    {
        // Clear previous hover
        if (_currentlyHoveredEllipse != null)
        {
            AnimateUnhover(_currentlyHoveredEllipse);
            _currentlyHoveredEllipse = null;
        }

        // Clear tooltips
        TooltipsOverlay.Children.Clear();

        // Dim all ellipses
        foreach (var ellipse in PointsOverlay.Children.OfType<Ellipse>())
        {
            ellipse.Opacity = 1.0;
        }

        if (vm.HoveredStudentId.HasValue)
        {
            // Get all points for this student
            var studentPoints = vm.GetPointsForStudent(vm.HoveredStudentId.Value);

            foreach (var point in studentPoints)
            {
                // Find or create ellipse for this point
                var (displayX, displayY) = vm.SvgToDisplay(point.X, point.Y);

                // For now, create temporary hover visualization
                // In full implementation, ellipses would be pre-rendered
                var hoverMarker = new Ellipse
                {
                    Width = 15, // 3x normal size
                    Height = 15,
                    Fill = new SolidColorBrush(Color.Parse(point.Color)),
                    Opacity = 1.0
                };

                Canvas.SetLeft(hoverMarker, displayX - 7.5);
                Canvas.SetTop(hoverMarker, displayY - 7.5);

                PointsOverlay.Children.Add(hoverMarker);

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
