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

public partial class TsnePlotControl : UserControl
{
    private CancellationTokenSource? _resizeCts;
    private ThemeName _currentTheme = ThemeName.DarkMode;

    // Double-click tracking
    private DateTime _lastClickTime;
    private int? _lastClickedStudentId;
    private const int DoubleClickThresholdMs = 500;

    public TsnePlotControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;

        // Add click handler to the points overlay
        PointsOverlay.PointerPressed += OnPointsOverlayClick;

        // Add button handlers
        CopyTsnePlotButton.Click += OnCopyClick;

        // Subscribe to theme change messages
        WeakReferenceMessenger.Default.Register<RenderWithThemeMessage>(this, OnRenderWithThemeMessage);
    }

    private void OnRenderWithThemeMessage(object recipient, RenderWithThemeMessage message)
    {
        _currentTheme = message.Theme;

        // Update background color based on theme
        Background = ThemeColors.BackgroundBrush(_currentTheme);

        // Hide/show UI elements based on theme (hide during export)
        CopyTsnePlotButton.IsVisible = _currentTheme == ThemeName.DarkMode;
        SlidersPanel.IsVisible = _currentTheme == ThemeName.DarkMode;
        LoadingOverlay.IsVisible = false; // Hide loading during export

        // Re-render all visual elements with new theme
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is TsnePlotViewModel vm)
            {
                var width = TsnePlotArea.Bounds.Width;
                var height = TsnePlotArea.Bounds.Height;
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
        if (DataContext is TsnePlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            RenderPointsAsShapes();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Immediately reposition dots during resize
        if (DataContext is TsnePlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            var plotBounds = TsnePlotArea.Bounds;
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
                    if (!token.IsCancellationRequested && DataContext is TsnePlotViewModel viewModel)
                    {
                        var plotBounds = TsnePlotArea.Bounds;
                        var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
                        var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 600;

                        try
                        {
                            viewModel.RegeneratePlot(displayWidth, displayHeight);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TsnePlot] Error regenerating plot: {ex.Message}");
                        }
                    }
                });
            }
        }, token);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is TsnePlotViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;

            if (!string.IsNullOrEmpty(vm.SvgContent))
            {
                UpdateSvgDisplay(vm.SvgContent);
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TsnePlotViewModel vm) return;

        if (e.PropertyName == nameof(TsnePlotViewModel.SvgContent))
        {
            UpdateSvgDisplay(vm.SvgContent);
        }
        else if (e.PropertyName == nameof(TsnePlotViewModel.HoveredStudentId))
        {
            UpdateHoverVisualization(vm);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is TsnePlotViewModel vm)
        {
            var position = e.GetPosition(this);
            var plotBounds = TsnePlotArea.Bounds;
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
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotsesses_tsne.svg");
            File.WriteAllText(tempPath, svgContent);

            var svgSource = Avalonia.Svg.Skia.SvgSource.Load(tempPath, null);
            var svgImage = new Avalonia.Svg.Skia.SvgImage { Source = svgSource };
            SvgView.Source = svgImage;

            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is TsnePlotViewModel viewModel)
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
            Console.WriteLine($"[TsnePlot] Error loading SVG: {ex.Message}");
        }
    }

    private void RenderPointsAsShapes()
    {
        if (DataContext is not TsnePlotViewModel vm)
            return;

        PointsOverlay.Children.Clear();

        var allPoints = vm.GetAllPoints();
        if (!allPoints.Any())
            return;

        var plotBounds = TsnePlotArea.Bounds;
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
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.Parse(point.Color)),
                Opacity = 0.8,
                Tag = (i, point.StudentId)
            };
            Canvas.SetLeft(ellipse, displayX - 4);
            Canvas.SetTop(ellipse, displayY - 4);
            PointsOverlay.Children.Add(ellipse);
        }
    }

    private void UpdateDotPositions(double displayWidth, double displayHeight)
    {
        if (DataContext is not TsnePlotViewModel vm)
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
                    double offset = isHitArea ? 7.5 : 4;

                    Canvas.SetLeft(child, displayX - offset);
                    Canvas.SetTop(child, displayY - offset);
                }
            }
        }
    }

    private void UpdateHoverVisualization(TsnePlotViewModel vm)
    {
        RenderPointsAsShapes();

        if (vm.HoveredStudentId.HasValue)
        {
            var studentPoint = vm.GetPointForStudent(vm.HoveredStudentId.Value);

            if (studentPoint != null)
            {
                var plotBounds = TsnePlotArea.Bounds;
                var displayWidth = plotBounds.Width > 0 ? plotBounds.Width : 800;
                var displayHeight = plotBounds.Height > 0 ? plotBounds.Height : 600;

                var (displayX, displayY) = vm.SvgToDisplayWithSize(studentPoint.X, studentPoint.Y, displayWidth, displayHeight);

                // Add hover ring
                var hoverRing = new Ellipse
                {
                    Width = 18,
                    Height = 18,
                    Stroke = new SolidColorBrush(Color.Parse(studentPoint.Color)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(hoverRing, displayX - 9);
                Canvas.SetTop(hoverRing, displayY - 9);
                PointsOverlay.Children.Add(hoverRing);
            }
        }
    }

    private void OnPlotAreaClick(object? sender, PointerPressedEventArgs e)
    {
        var position = e.GetCurrentPoint(TsnePlotArea);

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
                WeakReferenceMessenger.Default.Send(new StudentHoverMessage(null, "tsne"));
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
                WeakReferenceMessenger.Default.Send(new StudentHoverMessage(null, "tsne"));
                e.Handled = true;
            }
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
