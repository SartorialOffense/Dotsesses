using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;

namespace Dotsesses.UI;

public partial class CorrelationPlotControl : UserControl
{
    private CancellationTokenSource? _resizeCts;
    private ThemeName _currentTheme = ThemeName.DarkMode;

    // Double-click tracking
    private DateTime _lastClickTime;
    private int? _lastClickedStudentId;
    private const int DoubleClickThresholdMs = 500;

    public CorrelationPlotControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;

        // Add click handler to the points overlay
        PointsOverlay.PointerPressed += OnPointsOverlayClick;

        // Add button handlers
        CopyCorrelationPlotButton.Click += OnCopyClick;
        DiagonalToggleButton.Click += OnDiagonalToggleClick;

        // Subscribe to theme change messages
        WeakReferenceMessenger.Default.Register<RenderWithThemeMessage>(this, OnRenderWithThemeMessage);
    }

    private void OnRenderWithThemeMessage(object recipient, RenderWithThemeMessage message)
    {
        _currentTheme = message.Theme;

        // Update background color based on theme
        Background = ThemeColors.BackgroundBrush(_currentTheme);

        // Hide/show buttons based on theme (hide during export)
        CopyCorrelationPlotButton.IsVisible = _currentTheme == ThemeName.DarkMode;
        DiagonalToggleButton.IsVisible = _currentTheme == ThemeName.DarkMode;

        // Re-render all visual elements with new theme
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is CorrelationPlotViewModel vm)
            {
                var width = CorrelationPlotArea.Bounds.Width;
                var height = CorrelationPlotArea.Bounds.Height;
                if (width > 0 && height > 0)
                {
                    vm.RegeneratePlot(width, height, _currentTheme);

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

            message.OnRenderComplete?.Invoke();
        }, DispatcherPriority.Render);
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is CorrelationPlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            RenderPointsAsShapes();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Immediately reposition dots during resize
        if (DataContext is CorrelationPlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            var plotBounds = CorrelationPlotArea.Bounds;
            if (plotBounds.Width > 0 && plotBounds.Height > 0)
            {
                UpdateDotPositions(plotBounds.Width, plotBounds.Height);
            }

            if (vm.HoveredStudentId.HasValue)
            {
                UpdateHoverVisualization(vm);
            }
        }

        // Debounce: regenerate plot after resize finishes
        _resizeCts?.Cancel();
        _resizeCts = new CancellationTokenSource();
        var token = _resizeCts.Token;

        Task.Delay(150, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested && DataContext is CorrelationPlotViewModel viewModel)
                    {
                        var plotBounds = CorrelationPlotArea.Bounds;
                        var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
                        var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 600;

                        try
                        {
                            viewModel.RegeneratePlot(displayWidth, displayHeight);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[CorrelationPlot] Error regenerating plot: {ex.Message}");
                        }
                    }
                });
            }
        }, token);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is CorrelationPlotViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;

            if (!string.IsNullOrEmpty(vm.SvgContent))
            {
                UpdateSvgDisplay(vm.SvgContent);
            }

            // Update diagonal toggle icon
            UpdateDiagonalToggleIcon(vm.DiagonalType);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CorrelationPlotViewModel vm) return;

        if (e.PropertyName == nameof(CorrelationPlotViewModel.SvgContent))
        {
            UpdateSvgDisplay(vm.SvgContent);
        }
        else if (e.PropertyName == nameof(CorrelationPlotViewModel.HoveredStudentId))
        {
            UpdateHoverVisualization(vm);
        }
        else if (e.PropertyName == nameof(CorrelationPlotViewModel.DiagonalType))
        {
            UpdateDiagonalToggleIcon(vm.DiagonalType);
        }
    }

    private void UpdateDiagonalToggleIcon(string diagonalType)
    {
        // ~ for KDE (wave), # for histogram (bars)
        DiagonalToggleIcon.Text = diagonalType == "kde" ? "~" : "#";
        ToolTip.SetTip(DiagonalToggleButton, diagonalType == "kde"
            ? "Diagonal: KDE (click for histogram)"
            : "Diagonal: Histogram (click for KDE)");
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is CorrelationPlotViewModel vm)
        {
            var position = e.GetPosition(this);
            var plotBounds = CorrelationPlotArea.Bounds;
            var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
            var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 600;
            vm.OnPointerMoved(position, displayWidth, displayHeight);
        }
    }

    private void UpdateSvgDisplay(string? svgContent)
    {
        if (string.IsNullOrEmpty(svgContent))
            return;

        try
        {
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotsesses_correlation.svg");
            File.WriteAllText(tempPath, svgContent);

            var svgSource = Avalonia.Svg.Skia.SvgSource.Load(tempPath, null);
            var svgImage = new Avalonia.Svg.Skia.SvgImage { Source = svgSource };
            SvgView.Source = svgImage;

            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is CorrelationPlotViewModel viewModel)
                {
                    UpdateHoverVisualization(viewModel);
                }
                else
                {
                    RenderPointsAsShapes();
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CorrelationPlot] Error loading SVG: {ex.Message}");
        }
    }

    private void RenderPointsAsShapes()
    {
        if (DataContext is not CorrelationPlotViewModel vm)
            return;

        PointsOverlay.Children.Clear();

        var allPoints = vm.GetAllPoints();
        if (!allPoints.Any())
            return;

        var plotBounds = CorrelationPlotArea.Bounds;
        var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
        var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 600;

        for (int i = 0; i < allPoints.Count; i++)
        {
            var point = allPoints[i];
            var (displayX, displayY) = vm.SvgToDisplayWithSize(point.X, point.Y, displayWidth, displayHeight);

            // Add transparent hit area (15x15)
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

            // Add visible shape (smaller dot)
            var ellipse = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Color.Parse(point.Color)),
                Opacity = 0.8,
                Tag = (i, point.StudentId)
            };
            Canvas.SetLeft(ellipse, displayX - 2.5);
            Canvas.SetTop(ellipse, displayY - 2.5);
            PointsOverlay.Children.Add(ellipse);
        }
    }

    private void UpdateDotPositions(double displayWidth, double displayHeight)
    {
        if (DataContext is not CorrelationPlotViewModel vm)
            return;

        var allPoints = vm.GetAllPoints();
        if (!allPoints.Any())
            return;

        foreach (var child in PointsOverlay.Children.OfType<Control>())
        {
            if (child.Tag is ValueTuple<int, int> tag)
            {
                var (pointIndex, studentId) = tag;
                if (pointIndex >= 0 && pointIndex < allPoints.Count)
                {
                    var point = allPoints[pointIndex];
                    var (displayX, displayY) = vm.SvgToDisplayWithSize(point.X, point.Y, displayWidth, displayHeight);

                    bool isHitArea = child.Width == 15;
                    double offset = isHitArea ? 7.5 : 2.5;

                    Canvas.SetLeft(child, displayX - offset);
                    Canvas.SetTop(child, displayY - offset);
                }
            }
        }
    }

    private void UpdateHoverVisualization(CorrelationPlotViewModel vm)
    {
        TooltipsOverlay.Children.Clear();
        RenderPointsAsShapes();

        if (vm.HoveredStudentId.HasValue)
        {
            var studentPoints = vm.GetPointsForStudent(vm.HoveredStudentId.Value);

            var plotBounds = CorrelationPlotArea.Bounds;
            var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
            var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 600;

            bool tooltipShown = false;

            foreach (var point in studentPoints)
            {
                var (displayX, displayY) = vm.SvgToDisplayWithSize(point.X, point.Y, displayWidth, displayHeight);

                // Add hover ring
                var hoverRing = new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Stroke = new SolidColorBrush(Color.Parse(point.Color)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(hoverRing, displayX - 7);
                Canvas.SetTop(hoverRing, displayY - 7);
                PointsOverlay.Children.Add(hoverRing);

                // Show tooltip for first point only (avoid clutter)
                if (!tooltipShown)
                {
                    CreateTooltip(point, displayX, displayY);
                    tooltipShown = true;
                }
            }
        }
    }

    private void CreateTooltip(CorrelationDataPoint point, double displayX, double displayY)
    {
        var tooltipBorder = new Border
        {
            Background = ThemeColors.BackgroundBrush(_currentTheme),
            BorderBrush = ThemeColors.BorderBrush(_currentTheme),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4)
        };

        var tooltipContent = new StackPanel { Spacing = 2 };

        // Muppet name (if available)
        if (!string.IsNullOrEmpty(point.MuppetName))
        {
            var nameText = new TextBlock
            {
                Text = point.MuppetName,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = ThemeColors.ForegroundBrush(_currentTheme)
            };
            tooltipContent.Children.Add(nameText);
        }

        // X series value
        var xText = new TextBlock
        {
            FontSize = 11,
            Foreground = ThemeColors.ForegroundBrush(_currentTheme)
        };
        xText.Inlines.Add(new Avalonia.Controls.Documents.Run($"{point.XSeries}: ") { Foreground = ThemeColors.SecondaryTextBrush(_currentTheme) });
        xText.Inlines.Add(new Avalonia.Controls.Documents.Run($"{point.XValue:F0}") { FontWeight = FontWeight.Medium });
        tooltipContent.Children.Add(xText);

        // Y series value
        var yText = new TextBlock
        {
            FontSize = 11,
            Foreground = ThemeColors.ForegroundBrush(_currentTheme)
        };
        yText.Inlines.Add(new Avalonia.Controls.Documents.Run($"{point.YSeries}: ") { Foreground = ThemeColors.SecondaryTextBrush(_currentTheme) });
        yText.Inlines.Add(new Avalonia.Controls.Documents.Run($"{point.YValue:F0}") { FontWeight = FontWeight.Medium });
        tooltipContent.Children.Add(yText);

        tooltipBorder.Child = tooltipContent;

        // Measure tooltip
        tooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tooltipWidth = tooltipBorder.DesiredSize.Width;

        // Position tooltip
        double canvasWidth = TooltipsOverlay.Bounds.Width;
        double leftPos = displayX + 20 + tooltipWidth > canvasWidth
            ? displayX - tooltipWidth - 20
            : displayX + 20;

        Canvas.SetLeft(tooltipBorder, leftPos);
        Canvas.SetTop(tooltipBorder, displayY - 10);

        TooltipsOverlay.Children.Add(tooltipBorder);
    }

    private void OnPlotAreaClick(object? sender, PointerPressedEventArgs e)
    {
        var position = e.GetCurrentPoint(CorrelationPlotArea);

        // Check if we clicked on a point by hit testing the overlay
        var clickedElement = PointsOverlay.InputHitTest(position.Position);

        int? studentId = null;
        if (clickedElement is Control control && control.Tag is ValueTuple<int, int> tag)
        {
            studentId = tag.Item2;
        }

        if (studentId.HasValue)
        {
            // Forward to point click handler
            OnPointsOverlayClick(sender, e);
        }
        else
        {
            // Clicked on empty space - clear hover
            if (position.Properties.IsLeftButtonPressed)
            {
                WeakReferenceMessenger.Default.Send(new StudentHoverMessage(null, "correlation"));
                e.Handled = true;
            }
        }
    }

    private void OnPointsOverlayClick(object? sender, PointerPressedEventArgs e)
    {
        var position = e.GetCurrentPoint(PointsOverlay);
        var clickedElement = PointsOverlay.InputHitTest(position.Position);

        int? studentId = null;
        if (clickedElement is Control control && control.Tag is ValueTuple<int, int> tag)
        {
            studentId = tag.Item2;
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
                    WeakReferenceMessenger.Default.Send(new EditStudentMessage(studentId.Value));
                    _lastClickedStudentId = null;
                    e.Handled = true;
                    return;
                }

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
                WeakReferenceMessenger.Default.Send(new StudentHoverMessage(null, "correlation"));
                e.Handled = true;
            }
        }
    }

    private void OnDiagonalToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is CorrelationPlotViewModel vm)
        {
            vm.ToggleDiagonalType();
        }
    }

    private async void OnCopyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var clipboard = topLevel?.Clipboard;
        if (clipboard == null) return;

        await ImageCopyService.CopyControlToClipboardAsync(this, clipboard);
    }
}
