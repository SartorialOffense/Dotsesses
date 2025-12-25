# Violin Plot Cursor UI Implementation Plan

**Date:** 2024-12-24
**Status:** ✅ Implemented
**Estimated scope:** Medium (4 files to modify)

---

## Executive Summary

Add vertical draggable grade cursors to the violin plot (right side of the app), bound to the same `CursorViewModel` collection used by the dot plot. When a cursor is dragged in either plot, both update simultaneously. Region bands overlay the violin area to show grade boundaries visually.

---

## Current Application Architecture

### Dot Plot (Top section)
- **Technology:** OxyPlot `PlotModel` with annotations
- **Cursors:** Vertical `LineAnnotation` objects in a "CursorY" axis region
- **Region bands:** `RectangleAnnotation` objects in the "DotY" axis region
- **Grade labels:** `TextAnnotation` objects
- **Drag handling:** Mouse events on `PlotModel` (`MouseDown`, `MouseMove`, `MouseUp`)
- **State:** `MainWindowViewModel.Cursors` (ObservableCollection<CursorViewModel>)

### Violin Plot (Bottom-right section)
- **Technology:** Hybrid - Python generates SVG (violin shapes), Avalonia renders swarm points on Canvas overlays
- **Current layers (z-order bottom to top):**
  1. `Image` (SvgView) - violin shapes from Python/matplotlib
  2. `Canvas` (PointsOverlay) - swarm point shapes (Ellipse/Rectangle)
  3. `Canvas` (TooltipsOverlay) - hover tooltips
- **State:** `ViolinPlotViewModel` with `SvgContent`, `HoveredStudentId`, data points

### Shared State
- `MainWindowViewModel.Cursors` - ObservableCollection<CursorViewModel>
- `CursorViewModel` has: `Grade`, `Score` (int), `IsEnabled` (bool)
- Changes to `Score` trigger `PropertyChanged` events

---

## Target Architecture

### New Components

**1. Region Bands Canvas (on violin area)**
- New Canvas overlay between SVG and points
- Draws horizontal gray/transparent alternating bands
- Positioned using `ScoreToDisplayY()` coordinate mapping

**2. Cursor Column (OxyPlot PlotView, right side)**
- 50px wide OxyPlot column to the right of violin
- Horizontal `LineAnnotation` for cursor lines (draggable)
- `TextAnnotation` for grade labels
- Same mouse event pattern as dot plot

### Updated Z-Order for Violin Area
```
1. Image (SvgView) - violin shapes
2. Canvas (RegionBandsOverlay) - NEW: grade region bands
3. Canvas (PointsOverlay) - swarm points
4. Canvas (TooltipsOverlay) - hover tooltips
```

### Layout
```
┌────────────────────────────────────────┬──────┐
│  [Violin SVG]                          │  A   │  ← Grade label (OxyPlot TextAnnotation)
│  [Gray band]                           │------│  ← Cursor line (OxyPlot LineAnnotation, draggable)
│  [Transparent band]                    │  B+  │
│  [Gray band]                           │------│
│  [Swarm points on top]                 │  B   │
│  [Tooltips on top]                     │------│
│                                        │  C   │
│                                        │      │  ← No cursor for lowest grade (F)
│                                        │  F   │
└────────────────────────────────────────┴──────┘
     Violin area (Canvas bands)          OxyPlot cursor column (50px)
```

---

## Files to Modify

| File | Changes |
|------|---------|
| `Dotsesses/UI/ViolinPlotViewModel.cs` | Add `Cursors`, `MinScore`, `MaxScore`, `CursorPlotModel` properties; Add coordinate mapping methods; Add drag handlers |
| `Dotsesses/UI/ViolinPlotControl.axaml` | Add `RegionBandsOverlay` Canvas; Add OxyPlot `PlotView` in Grid column |
| `Dotsesses/UI/ViolinPlotControl.axaml.cs` | Add `RenderRegionBands()` method; Subscribe to cursor changes |
| `Dotsesses/UI/MainWindowViewModel.cs` | Wire up cursors to ViolinPlotViewModel after initialization |

---

## Detailed Implementation Steps

### Step 1: Extend ViolinPlotViewModel

**File:** `Dotsesses/UI/ViolinPlotViewModel.cs`

Add these using statements:
```csharp
using System.Collections.ObjectModel;
using Dotsesses.Calculators;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
```

Add new observable properties:
```csharp
[ObservableProperty]
private ObservableCollection<CursorViewModel>? _cursors;

[ObservableProperty]
private int _minScore;

[ObservableProperty]
private int _maxScore;

[ObservableProperty]
private PlotModel? _cursorPlotModel;
```

Add private fields:
```csharp
private CursorViewModel? _draggingCursor;
private bool _isDragging;
private readonly CursorValidation _cursorValidation = new();
```

Add coordinate mapping method:
```csharp
/// <summary>
/// Converts a score value to display Y coordinate (for Canvas region bands).
/// High scores at top (Y=0), low scores at bottom (Y=height).
/// </summary>
public double ScoreToDisplayY(int score, double displayHeight)
{
    if (_maxScore == _minScore) return displayHeight / 2;
    var normalizedY = 1.0 - (double)(score - _minScore) / (_maxScore - _minScore);
    return normalizedY * displayHeight;
}
```

Add cursor PlotModel initialization:
```csharp
public void InitializeCursorPlotModel()
{
    if (Cursors == null) return;

    CursorPlotModel = new PlotModel
    {
        Background = OxyColor.FromRgb(26, 26, 26),
        PlotAreaBorderThickness = new OxyThickness(0),
        Padding = new OxyThickness(0),
        PlotMargins = new OxyThickness(0)
    };

    // Y-axis: score range (inverted so high scores at top)
    var yAxis = new LinearAxis
    {
        Position = AxisPosition.Left,
        Key = "ScoreY",
        Minimum = _minScore - 5,
        Maximum = _maxScore + 5,
        StartPosition = 1,  // Invert axis
        EndPosition = 0,
        IsAxisVisible = false,
        IsPanEnabled = false,
        IsZoomEnabled = false
    };

    // Hidden X-axis for label positioning
    var xAxis = new LinearAxis
    {
        Position = AxisPosition.Bottom,
        Key = "X",
        Minimum = 0,
        Maximum = 1,
        IsAxisVisible = false,
        IsPanEnabled = false,
        IsZoomEnabled = false
    };

    CursorPlotModel.Axes.Add(yAxis);
    CursorPlotModel.Axes.Add(xAxis);

    // Wire up mouse events
    CursorPlotModel.MouseDown += OnCursorMouseDown;
    CursorPlotModel.MouseMove += OnCursorMouseMove;
    CursorPlotModel.MouseUp += OnCursorMouseUp;

    UpdateCursorAnnotations();
}
```

Add cursor annotation rendering (adapts `MainWindowViewModel.UpdateCursors()`):
```csharp
public void UpdateCursorAnnotations()
{
    if (CursorPlotModel == null || Cursors == null) return;

    CursorPlotModel.Annotations.Clear();

    var enabledCursors = Cursors.Where(c => c.IsEnabled).OrderBy(c => c.Score).ToList();
    if (!enabledCursors.Any()) return;

    var lowestGrade = enabledCursors.OrderByDescending(c => c.Grade.Order).FirstOrDefault();

    // Horizontal cursor lines (excluding lowest grade - it's a catch-all)
    foreach (var cursor in enabledCursors.Where(c => c != lowestGrade))
    {
        var line = new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = cursor.Score,
            Color = OxyColors.White,
            LineStyle = LineStyle.Dash,
            StrokeThickness = 2,
            XAxisKey = "X",
            YAxisKey = "ScoreY"
        };
        CursorPlotModel.Annotations.Add(line);
    }

    // Grade labels centered in each region
    var enabledGrades = enabledCursors.Select(c => c.Grade).OrderBy(g => g.Order).ToList();

    for (int i = 0; i < enabledGrades.Count; i++)
    {
        var grade = enabledGrades[i];
        double labelY;

        if (i == 0)
        {
            // Top region: between highest cursor and max
            labelY = (enabledCursors.Last().Score + _maxScore + 5) / 2.0;
        }
        else if (i == enabledGrades.Count - 1)
        {
            // Bottom region: between min and lowest cursor
            labelY = (_minScore - 5 + enabledCursors.First().Score) / 2.0;
        }
        else
        {
            // Middle regions: between adjacent cursors
            var cursorAbove = enabledCursors.FirstOrDefault(c => c.Grade.Order == enabledGrades[i - 1].Order);
            var cursorBelow = enabledCursors.FirstOrDefault(c => c.Grade.Order == grade.Order);
            if (cursorAbove != null && cursorBelow != null)
            {
                labelY = (cursorAbove.Score + cursorBelow.Score) / 2.0;
            }
            else
            {
                continue;
            }
        }

        var label = new TextAnnotation
        {
            Text = grade.DisplayName,
            TextPosition = new DataPoint(0.5, labelY),
            TextColor = OxyColors.White,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            TextHorizontalAlignment = HorizontalAlignment.Center,
            TextVerticalAlignment = VerticalAlignment.Middle,
            XAxisKey = "X",
            YAxisKey = "ScoreY",
            Stroke = OxyColors.Transparent,
            StrokeThickness = 0
        };
        CursorPlotModel.Annotations.Add(label);
    }

    CursorPlotModel.InvalidatePlot(true);
}
```

Add drag handlers (same pattern as dot plot):
```csharp
private void OnCursorMouseDown(object? sender, OxyMouseDownEventArgs e)
{
    if (e.ChangedButton != OxyMouseButton.Left || Cursors == null) return;

    var yAxis = CursorPlotModel?.Axes.FirstOrDefault(a => a.Key == "ScoreY");
    if (yAxis == null) return;

    var clickY = yAxis.InverseTransform(e.Position.Y);
    var nearest = FindNearestCursor(clickY);

    if (nearest.cursor != null && nearest.distance < 3)
    {
        _draggingCursor = nearest.cursor;
        _isDragging = true;
        e.Handled = true;
    }
}

private void OnCursorMouseMove(object? sender, OxyMouseEventArgs e)
{
    if (!_isDragging || _draggingCursor == null || Cursors == null) return;

    var yAxis = CursorPlotModel?.Axes.FirstOrDefault(a => a.Key == "ScoreY");
    if (yAxis == null) return;

    var newScore = (int)Math.Round(yAxis.InverseTransform(e.Position.Y));

    // Build cutoffs with proposed position
    var allCutoffs = Cursors
        .Where(c => c.IsEnabled)
        .Select(c => new GradeCutoff(c.Grade, c == _draggingCursor ? newScore : c.Score))
        .ToList();

    // Validate (reuses existing CursorValidation class)
    var validated = _cursorValidation.ValidateMovement(
        _draggingCursor.Grade, newScore, allCutoffs, _minScore - 1, _maxScore + 1);

    _draggingCursor.Score = validated;
    e.Handled = true;
}

private void OnCursorMouseUp(object? sender, OxyMouseEventArgs e)
{
    _isDragging = false;
    _draggingCursor = null;
}

private (CursorViewModel? cursor, double distance) FindNearestCursor(double yPos)
{
    if (Cursors == null) return (null, double.MaxValue);

    var lowestGrade = Cursors.Where(c => c.IsEnabled)
        .OrderByDescending(c => c.Grade.Order).FirstOrDefault();

    CursorViewModel? nearest = null;
    double minDist = double.MaxValue;

    foreach (var cursor in Cursors.Where(c => c.IsEnabled && c != lowestGrade))
    {
        var dist = Math.Abs(cursor.Score - yPos);
        if (dist < minDist)
        {
            minDist = dist;
            nearest = cursor;
        }
    }
    return (nearest, minDist);
}
```

---

### Step 2: Update ViolinPlotControl XAML

**File:** `Dotsesses/UI/ViolinPlotControl.axaml`

Add OxyPlot namespace and restructure with Grid columns:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Dotsesses.UI"
             xmlns:oxy="clr-namespace:OxyPlot.Avalonia;assembly=OxyPlot.Avalonia"
             mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="400"
             x:Class="Dotsesses.UI.ViolinPlotControl"
             x:DataType="vm:ViolinPlotViewModel"
             Background="#000000">

    <Grid ColumnDefinitions="*,50">
        <!-- Violin plot area with region bands -->
        <Grid Grid.Column="0" PointerMoved="OnPointerMoved" Background="Transparent">
            <Image x:Name="SvgView" Stretch="Fill" />
            <Canvas x:Name="RegionBandsOverlay" IsHitTestVisible="False" />
            <Canvas x:Name="PointsOverlay" />
            <Canvas x:Name="TooltipsOverlay" />
        </Grid>

        <!-- Cursor column (OxyPlot) -->
        <oxy:PlotView Grid.Column="1"
                      x:Name="CursorPlotView"
                      Model="{Binding CursorPlotModel}"
                      Background="#1A1A1A" />
    </Grid>
</UserControl>
```

---

### Step 3: Update ViolinPlotControl Code-Behind

**File:** `Dotsesses/UI/ViolinPlotControl.axaml.cs`

Add using statement:
```csharp
using Avalonia.Controls.Shapes;
```

Add region bands rendering method:
```csharp
private void RenderRegionBands()
{
    RegionBandsOverlay.Children.Clear();

    if (DataContext is not ViolinPlotViewModel vm || vm.Cursors == null) return;

    var height = RegionBandsOverlay.Bounds.Height;
    var width = RegionBandsOverlay.Bounds.Width;
    if (height <= 0 || width <= 0) return;

    var enabledCursors = vm.Cursors.Where(c => c.IsEnabled).OrderBy(c => c.Score).ToList();
    if (!enabledCursors.Any()) return;

    var lowestGrade = enabledCursors.OrderByDescending(c => c.Grade.Order).FirstOrDefault();
    var cursorsWithLines = enabledCursors.Where(c => c != lowestGrade).OrderBy(c => c.Score).ToList();

    if (!cursorsWithLines.Any()) return;

    var grayBrush = new SolidColorBrush(Color.FromArgb(0x20, 255, 255, 255));

    // Build regions from top (high score) to bottom (low score)
    var regions = new List<(double top, double bottom, bool isGray)>();

    // Top region: above highest cursor
    regions.Add((0, vm.ScoreToDisplayY(cursorsWithLines.Last().Score, height), false));

    // Between cursors (alternating)
    for (int i = cursorsWithLines.Count - 1; i > 0; i--)
    {
        var top = vm.ScoreToDisplayY(cursorsWithLines[i].Score, height);
        var bottom = vm.ScoreToDisplayY(cursorsWithLines[i - 1].Score, height);
        bool isGray = (cursorsWithLines.Count - i) % 2 == 1;
        regions.Add((top, bottom, isGray));
    }

    // Bottom region: below lowest cursor
    regions.Add((vm.ScoreToDisplayY(cursorsWithLines.First().Score, height), height,
                 cursorsWithLines.Count % 2 == 1));

    // Draw only the gray bands (skip transparent)
    foreach (var (top, bottom, isGray) in regions)
    {
        if (!isGray) continue;

        var rect = new Rectangle
        {
            Width = width,
            Height = Math.Max(0, bottom - top),
            Fill = grayBrush
        };
        Canvas.SetTop(rect, top);
        RegionBandsOverlay.Children.Add(rect);
    }
}
```

Update `OnDataContextChanged` to subscribe to cursor changes:
```csharp
private void OnDataContextChanged(object? sender, EventArgs e)
{
    if (DataContext is ViolinPlotViewModel vm)
    {
        vm.PropertyChanged += OnViewModelPropertyChanged;

        // Check if SVG content already exists
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
        if (DataContext is ViolinPlotViewModel vm)
        {
            vm.UpdateCursorAnnotations();  // Update OxyPlot cursor column
            RenderRegionBands();            // Update Canvas bands
        }
    }
}
```

Update `OnSizeChanged` to re-render bands:
```csharp
private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
{
    // ... existing code for dot repositioning and plot regeneration ...

    // Also re-render region bands
    RenderRegionBands();
}
```

Update `OnLoaded` to render bands:
```csharp
private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
{
    if (DataContext is ViolinPlotViewModel vm && !string.IsNullOrEmpty(vm.SvgContent))
    {
        RenderPointsAsShapes();
        RenderRegionBands();  // Add this
    }
}
```

---

### Step 4: Wire Up in MainWindowViewModel

**File:** `Dotsesses/UI/MainWindowViewModel.cs`

After cursors are initialized (in constructor, after `InitializeCursors()`), add:

```csharp
// Wire up cursors to violin plot
if (ViolinPlotViewModel != null)
{
    ViolinPlotViewModel.Cursors = Cursors;
    ViolinPlotViewModel.MinScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
    ViolinPlotViewModel.MaxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
    ViolinPlotViewModel.InitializeCursorPlotModel();
}
```

**Important:** This should happen AFTER both `InitializeCursors()` and the ViolinPlotViewModel is created. Check the constructor order:
1. `_violinPlotViewModel = violinPlotViewModel;` (from DI)
2. `InitializeCursors();`
3. Add the wiring code here

---

## Key Patterns from Existing Code

### Dot Plot Cursor Rendering (Reference)
See `MainWindowViewModel.UpdateCursors()` lines 558-731 for:
- How to exclude lowest grade from cursor lines
- How to calculate region boundaries
- How to position grade labels between regions

### Cursor Validation
`CursorValidation.ValidateMovement()` in `Dotsesses/Calculators/CursorValidation.cs`:
- Enforces minimum 1-point spacing between cursors
- Keeps cursors within bounds (minScore-1 to maxScore+1)
- Prevents cursors from crossing each other

### Coordinate Mapping
The violin plot Y-axis is normalized (0-1) per series, but for cursors we use actual score values. The `ScoreToDisplayY()` method maps real scores to Canvas pixel coordinates.

---

## Testing Checklist

- [x] Cursor lines appear in the cursor column
- [x] Grade labels appear centered between cursor lines
- [x] Region bands appear on violin area with correct alternating pattern
- [x] Dragging a cursor in the violin plot updates the dot plot
- [x] Dragging a cursor in the dot plot updates the violin plot
- [x] Compliance grid updates when cursors are dragged
- [x] Cursors cannot cross each other
- [x] Lowest grade (F) has no cursor line (it's a catch-all)
- [x] Region bands and cursor column resize correctly with window

---

## Dependencies

The implementation reuses existing classes:
- `CursorViewModel` - already exists, shared between plots
- `CursorValidation` - already exists in `Dotsesses/Calculators/`
- `GradeCutoff` - already exists in `Dotsesses/Models/`

No new NuGet packages needed - OxyPlot.Avalonia is already referenced.
