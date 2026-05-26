using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;

namespace Dotsesses.UI;

public partial class SignificancePlotControl : UserControl
{
    private CancellationTokenSource? _resizeCts;
    private ThemeName _currentTheme = ThemeName.DarkMode;
    private bool _messengerRegistered;

    public SignificancePlotControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SignificancePlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            RenderPointsAsShapes();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Move overlay dots immediately so they don't visibly lag the SVG during resize.
        if (DataContext is SignificancePlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
        {
            var bounds = SignificancePlotArea.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                UpdateDotPositions(bounds.Width, bounds.Height);
            }
        }

        // Debounce: regenerate (Python re-render) 150ms after resize settles —
        // mirrors CorrelationPlotControl's pattern.
        _resizeCts?.Cancel();
        _resizeCts = new CancellationTokenSource();
        var token = _resizeCts.Token;

        Task.Delay(150, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                if (DataContext is not SignificancePlotViewModel viewModel) return;

                var b = SignificancePlotArea.Bounds;
                var w = b.Width > 0 ? b.Width : 800;
                var h = b.Height > 0 ? b.Height : 600;
                try
                {
                    viewModel.RegeneratePlot(w, h, _currentTheme);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SignificancePlot] Error regenerating plot: {ex.Message}");
                }
            });
        }, token);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SignificancePlotViewModel vm)
        {
            if (!_messengerRegistered)
            {
                _messengerRegistered = true;
                vm.Messenger.Register<RenderWithThemeMessage>(this, OnRenderWithThemeMessage);
            }

            vm.PropertyChanged += OnViewModelPropertyChanged;
            if (!string.IsNullOrEmpty(vm.SvgContent))
            {
                UpdateSvgDisplay(vm.SvgContent);
            }
        }
    }

    private void OnRenderWithThemeMessage(object recipient, RenderWithThemeMessage message)
    {
        _currentTheme = message.Theme;
        Background = ThemeColors.BackgroundBrush(_currentTheme);

        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is SignificancePlotViewModel vm)
            {
                var b = SignificancePlotArea.Bounds;
                if (b.Width > 0 && b.Height > 0)
                {
                    vm.RegeneratePlot(b.Width, b.Height, _currentTheme);
                    if (!string.IsNullOrEmpty(vm.SvgContent))
                    {
                        UpdateSvgDisplay(vm.SvgContent);
                    }
                }
            }
            message.OnRenderComplete?.Invoke();
        }, DispatcherPriority.Render);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SignificancePlotViewModel vm) return;
        if (e.PropertyName == nameof(SignificancePlotViewModel.SvgContent))
        {
            UpdateSvgDisplay(vm.SvgContent);
        }
    }

    private void UpdateSvgDisplay(string? svgContent)
    {
        if (string.IsNullOrEmpty(svgContent)) return;

        try
        {
            var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotsesses_significance.svg");
            File.WriteAllText(tempPath, svgContent);

            var svgSource = Avalonia.Svg.Skia.SvgSource.Load(tempPath, null);
            var svgImage = new Avalonia.Svg.Skia.SvgImage { Source = svgSource };
            SvgView.Source = svgImage;

            Dispatcher.UIThread.Post(RenderPointsAsShapes, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SignificancePlot] Error loading SVG: {ex.Message}");
        }
    }

    private void RenderPointsAsShapes()
    {
        if (DataContext is not SignificancePlotViewModel vm) return;

        PointsOverlay.Children.Clear();
        var points = vm.GetAllPoints();
        if (points.Count == 0) return;

        var bounds = SignificancePlotArea.Bounds;
        var displayW = bounds.Width > 0 ? bounds.Width : 800;
        var displayH = bounds.Height > 0 ? bounds.Height : 600;

        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var (dx, dy) = vm.SvgToDisplayWithSize(p.X, p.Y, displayW, displayH);

            // Transparent hit area sized for forgiving hover.
            var hit = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = Brushes.Transparent,
                Tag = i,
            };
            ToolTip.SetTip(hit, SignificancePlotViewModel.BuildTooltip(p));
            Canvas.SetLeft(hit, dx - 9);
            Canvas.SetTop(hit, dy - 9);
            PointsOverlay.Children.Add(hit);

            // Visible dot (matches the matplotlib scatter dot size visually).
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.Parse(p.Color)),
                Opacity = 0.9,
                Tag = i,
            };
            Canvas.SetLeft(dot, dx - 4);
            Canvas.SetTop(dot, dy - 4);
            PointsOverlay.Children.Add(dot);
        }
    }

    private void UpdateDotPositions(double displayWidth, double displayHeight)
    {
        if (DataContext is not SignificancePlotViewModel vm) return;
        var points = vm.GetAllPoints();
        if (points.Count == 0) return;

        foreach (var child in PointsOverlay.Children.OfType<Control>())
        {
            if (child.Tag is int pointIndex && pointIndex >= 0 && pointIndex < points.Count)
            {
                var p = points[pointIndex];
                var (dx, dy) = vm.SvgToDisplayWithSize(p.X, p.Y, displayWidth, displayHeight);
                double offset = child.Width / 2;
                Canvas.SetLeft(child, dx - offset);
                Canvas.SetTop(child, dy - offset);
            }
        }
    }
}
