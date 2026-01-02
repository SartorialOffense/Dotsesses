# Category Correlation Comparison Plot Design

**Date:** January 1, 2026
**Status:** Design Proposal - Awaiting Approval

---

## 1. Overview

This design document outlines the implementation plan for adding a "Category Correlation Comparison Plot Set" - an N×N matrix of mini-scatter plots showing all pairwise combinations of scores. This visualization will coexist with the existing violin plot in a tabbed interface.

### 1.1 Goals

- Create an N×N grid of scatter plots showing correlation between all score pairs
- Reuse the existing Python → SVG → C# overlay architecture from the violin plot
- Support hover interactions synchronized with the violin plot and dotplot
- Support theme switching (dark/light) for clipboard/export
- Integrate via a sleek, modern tabbed interface

---

## 2. Architecture Analysis

### 2.1 Existing Violin Plot Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    DATA FLOW SUMMARY                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  C# ViewModel                                                   │
│       │                                                         │
│       ├─── seriesData: List<(SeriesName, Dict<ID, Score>)>      │
│       ├─── commentMap: Dict<(StudentId, SeriesName), Comment>   │
│       ├─── muppetNameMap: Dict<StudentId, MuppetName>           │
│       └─── theme: 'dark' | 'light'                              │
│                                                                 │
│       │  CSnakes Python Integration                             │
│       ▼                                                         │
│                                                                 │
│  Python Module (violin_swarm.py)                                │
│       │                                                         │
│       ├─── Create matplotlib figure with seaborn                │
│       ├─── Generate SVG (transparent bg, 300 DPI)               │
│       ├─── Extract point coordinates from SVG XML               │
│       ├─── Remove swarm points from SVG                         │
│       └─── Return: (timing, svg_string, point_data_list)        │
│                                                                 │
│       │                                                         │
│       ▼                                                         │
│                                                                 │
│  C# View (ViolinPlotControl)                                    │
│       │                                                         │
│       ├─── Load SVG to Image control (Avalonia.Svg.Skia)        │
│       ├─── Render points on Canvas overlay                      │
│       ├─── Handle hover with velocity-based delays              │
│       └─── Sync hover via StudentHoverMessage                   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Key Components to Reuse

| Component | Purpose | Reusability |
|-----------|---------|-------------|
| `ViolinPlotService` | Python integration pattern | Template for new service |
| `HoverDelayService` | Velocity-based hover delays | Shared singleton |
| `StudentHoverMessage` | Cross-view hover sync | Shared, add "correlation" source |
| `ThemeColors` | Theme-aware styling | Fully reusable |
| `RenderWithThemeMessage` | Theme change notifications | Fully reusable |
| `ViolinDataPoint` | Point metadata | Adapt to `CorrelationDataPoint` |

---

## 3. Python Implementation

### 3.1 New Python Module: `correlation_matrix.py`

```python
def create_correlation_matrix(
    fig_size: Tuple[float, float],
    series: List[Tuple[str, Dict[str, float]]],
    theme: str = 'dark',
    dot_size: float = 3.0,
    show_correlation_coefficients: bool = True
) -> Tuple[Dict[str, int], str, List[Dict]]:
    """
    Creates an NxN grid of scatter plots for all score pair combinations.

    Parameters:
    - fig_size: Figure size in inches
    - series: List of (series_name, {student_id: value})
    - theme: 'dark' or 'light'
    - dot_size: Size for scatter dots
    - show_correlation_coefficients: Whether to display r values

    Returns:
    - timing_dict: Performance metrics
    - svg_string: SVG with plots but no points (rendered in C#)
    - point_data_list: [{cell_row, cell_col, x, y, id, x_series, y_series, color}]
    """
```

### 3.2 Plot Structure

```
     Score1   Score2   Score3   ...   Total
   ┌────────┬────────┬────────┬─────┬────────┐
S1 │  KDE   │scatter │scatter │ ... │scatter │
   │        │r=0.82  │r=0.71  │     │r=0.89  │
   ├────────┼────────┼────────┼─────┼────────┤
S2 │scatter │  KDE   │scatter │ ... │scatter │
   │r=0.82  │        │r=0.65  │     │r=0.91  │
   ├────────┼────────┼────────┼─────┼────────┤
S3 │scatter │scatter │  KDE   │ ... │scatter │
   │r=0.71  │r=0.65  │        │     │r=0.85  │
   ├────────┼────────┼────────┼─────┼────────┤
...│        │        │        │     │        │
   ├────────┼────────┼────────┼─────┼────────┤
Tot│scatter │scatter │scatter │ ... │  KDE   │
   │r=0.89  │r=0.91  │r=0.85  │     │        │
   └────────┴────────┴────────┴─────┴────────┘
```

**Design choices:**
- **Diagonal:** Kernel Density Estimate (KDE) or histogram for single variable distribution
- **Lower triangle:** Scatter plots with correlation coefficient (r value)
- **Upper triangle:** Mirror of lower (or blank for cleaner look - user preference?)
- **Axis labels:** Series names on outer edges only
- **Shared axes:** Within rows (Y) and columns (X) for efficient comparison

### 3.3 Implementation using matplotlib subplots

```python
import matplotlib.pyplot as plt
import seaborn as sns
import numpy as np

def create_correlation_matrix(...):
    apply_theme(theme)

    n = len(series)
    fig, axes = plt.subplots(n, n, figsize=fig_size,
                             sharex='col', sharey='row')

    point_data_list = []

    for i, (y_name, y_data) in enumerate(series):
        for j, (x_name, x_data) in enumerate(series):
            ax = axes[i, j]

            if i == j:
                # Diagonal: KDE plot
                values = list(x_data.values())
                sns.kdeplot(values, ax=ax, fill=True, alpha=0.5)
            else:
                # Off-diagonal: Scatter plot
                # Get matched student data
                common_ids = set(x_data.keys()) & set(y_data.keys())
                x_vals = [x_data[sid] for sid in common_ids]
                y_vals = [y_data[sid] for sid in common_ids]

                ax.scatter(x_vals, y_vals, s=dot_size**2, alpha=0.6)

                # Store point data for C# overlay
                for sid in common_ids:
                    point_data_list.append({
                        'cell_row': i,
                        'cell_col': j,
                        'x': ...,  # SVG coordinates calculated later
                        'y': ...,
                        'id': sid,
                        'x_series': x_name,
                        'y_series': y_name,
                        'x_value': x_data[sid],
                        'y_value': y_data[sid],
                        'color': get_color_for_series(x_name, y_name)
                    })

                # Add correlation coefficient
                if show_correlation_coefficients:
                    r = np.corrcoef(x_vals, y_vals)[0, 1]
                    ax.annotate(f'r={r:.2f}', xy=(0.95, 0.05),
                               xycoords='axes fraction', ha='right',
                               fontsize=8, color=stat_color)

            # Labels only on edges
            if j == 0:
                ax.set_ylabel(y_name, fontsize=8)
            if i == n - 1:
                ax.set_xlabel(x_name, fontsize=8)

    plt.tight_layout()
    # SVG export and point extraction (similar to violin_swarm.py)
    ...
```

---

## 4. C# Implementation

### 4.1 New Files Required

| File | Purpose |
|------|---------|
| `Models/CorrelationDataPoint.cs` | Data point record for correlation plot |
| `Services/CorrelationPlotService.cs` | Python integration service |
| `UI/CorrelationPlotViewModel.cs` | ViewModel for correlation plot |
| `UI/CorrelationPlotControl.axaml` | View (XAML) |
| `UI/CorrelationPlotControl.axaml.cs` | View code-behind |
| `UI/PlotTabContainerViewModel.cs` | Parent ViewModel for tabs |
| `UI/PlotTabContainer.axaml` | Tabbed container view |
| `UI/PlotTabContainer.axaml.cs` | Tab container code-behind |
| `Python/Correlation/correlation_matrix.py` | Python module |

### 4.2 CorrelationDataPoint Model

```csharp
namespace Dotsesses.Models;

/// <summary>
/// Represents a single point in the correlation matrix.
/// </summary>
public record CorrelationDataPoint(
    int CellRow,
    int CellCol,
    double X,
    double Y,
    int StudentId,
    string XSeries,
    string YSeries,
    double XValue,
    double YValue,
    string Color,
    string MuppetName = "");
```

### 4.3 CorrelationPlotService

```csharp
public class CorrelationPlotService
{
    private readonly ICorrelationMatrix _correlationModule;

    public (string SvgContent, List<CorrelationDataPoint> DataPoints) GeneratePlot(
        (double Width, double Height) figSize,
        List<(string SeriesName, Dictionary<string, double> Scores)> seriesData,
        Dictionary<int, string> muppetNameMap,
        double dotSize = 3.0,
        bool showCorrelationCoefficients = true,
        ThemeName theme = ThemeName.DarkMode)
    {
        // Similar pattern to ViolinPlotService
    }
}
```

### 4.4 CorrelationPlotViewModel

```csharp
public partial class CorrelationPlotViewModel : ViewModelBase
{
    private readonly CorrelationPlotService _correlationService;
    private readonly IMessenger _messenger;
    private readonly HoverDelayService _hoverDelayService;

    [ObservableProperty]
    private string? _svgContent;

    [ObservableProperty]
    private int? _hoveredStudentId;

    // Register for StudentHoverMessage (source != "correlation")
    // Generate hover events via HoverDelayService
    // Broadcast hover via StudentHoverMessage (source = "correlation")
}
```

### 4.5 Tab Container Design

The tabbed container will wrap both the violin plot and correlation plot:

```xml
<!-- PlotTabContainer.axaml -->
<UserControl>
    <Grid RowDefinitions="Auto,*">
        <!-- Tab Header Row -->
        <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="0">
            <Button Classes="tab-button"
                    Classes.selected="{Binding IsViolinSelected}"
                    Command="{Binding SelectViolinCommand}">
                <TextBlock Text="Distribution" />
            </Button>
            <Button Classes="tab-button"
                    Classes.selected="{Binding IsCorrelationSelected}"
                    Command="{Binding SelectCorrelationCommand}">
                <TextBlock Text="Correlation" />
            </Button>
        </StackPanel>

        <!-- Content Area -->
        <Grid Grid.Row="1">
            <local:ViolinPlotControl DataContext="{Binding ViolinPlotViewModel}"
                                     IsVisible="{Binding IsViolinSelected}" />
            <local:CorrelationPlotControl DataContext="{Binding CorrelationPlotViewModel}"
                                          IsVisible="{Binding IsCorrelationSelected}" />
        </Grid>
    </Grid>

    <UserControl.Styles>
        <Style Selector="Button.tab-button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="#666666"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="16,8"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="FontWeight" Value="Medium"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>
        <Style Selector="Button.tab-button:pointerover">
            <Setter Property="Foreground" Value="#888888"/>
        </Style>
        <Style Selector="Button.tab-button.selected">
            <Setter Property="Foreground" Value="#3B82F6"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
        </Style>
        <Style Selector="Button.tab-button.selected:pointerover">
            <Setter Property="Foreground" Value="#60A5FA"/>
        </Style>
    </UserControl.Styles>
</UserControl>
```

**Tab styling:**
- Unselected: Dim gray (#666666)
- Selected: Pretty blue (#3B82F6 - Tailwind blue-500)
- Hover: Slightly lighter variants
- No borders, minimal padding
- Modern/sleek appearance

---

## 5. Hover Implementation

### 5.1 Cell-Aware Hit Testing

Since the correlation plot has multiple cells, hover detection requires:

1. **Determine which cell** the mouse is in (based on grid layout)
2. **Find closest point** within that cell (similar to violin plot)
3. **Report to HoverDelayService** with student ID
4. **On hover activation:**
   - Highlight ALL points for that student across ALL cells
   - Show tooltip near the hovered cell
   - Broadcast `StudentHoverMessage(studentId, "correlation")`

### 5.2 Multi-Cell Hover Visualization

When a student is hovered in the correlation plot:
- **Same cell:** Show hover ring + tooltip with both values
- **Other cells:** Show smaller rings without tooltips (subtle highlight)
- **Violin plot:** Receives message, highlights corresponding points
- **Dotplot:** Receives message, highlights corresponding point

```csharp
private void UpdateHoverVisualization(CorrelationPlotViewModel vm)
{
    if (vm.HoveredStudentId.HasValue)
    {
        var studentPoints = vm.GetPointsForStudent(vm.HoveredStudentId.Value);

        foreach (var point in studentPoints)
        {
            var (displayX, displayY) = ConvertToDisplay(point, cell);

            // Add hover ring
            var ring = new Ellipse { ... };

            // Only show tooltip for the primary hovered cell
            if (IsPrimaryHoveredCell(point))
            {
                CreateTooltip(point, displayX, displayY);
            }
        }
    }
}
```

---

## 6. Theme Integration

### 6.1 Python Theme Application

```python
def apply_theme(theme: str = 'dark'):
    if theme == 'light':
        plt.style.use('default')
        plt.rcParams.update({
            'axes.facecolor': 'white',
            'axes.edgecolor': 'black',
            'text.color': 'black',
            # ... same as violin_swarm.py
        })
    else:
        plt.style.use('dark_background')
```

### 6.2 C# Theme Handling

- Subscribe to `RenderWithThemeMessage`
- Regenerate Python plot with new theme
- Re-render all overlays with theme-aware colors
- Use existing `ThemeColors` utility class

---

## 7. MainWindow Integration

### 7.1 Updated Layout

```xml
<!-- MainWindow.axaml - Bottom Row -->
<Border Grid.Column="2" Background="#101010" CornerRadius="8">
    <!-- Replace direct ViolinPlotControl with tabbed container -->
    <local:PlotTabContainer DataContext="{Binding PlotTabContainerViewModel}" />
</Border>
```

### 7.2 MainWindowViewModel Changes

```csharp
// Add new property
[ObservableProperty]
private PlotTabContainerViewModel _plotTabContainerViewModel;

// Initialize in constructor
PlotTabContainerViewModel = new PlotTabContainerViewModel(
    violinPlotService,
    correlationPlotService,
    messenger,
    hoverDelayService);
```

---

## 8. Implementation Checklist

### Phase 1: Python Module
- [ ] Create `Dotsesses/Python/Correlation/__init__.py`
- [ ] Create `Dotsesses/Python/Correlation/correlation_matrix.py`
- [ ] Implement `create_correlation_matrix()` function
- [ ] Add theme support
- [ ] Extract point coordinates from SVG
- [ ] Test standalone in Python

### Phase 2: C# Service & Model
- [ ] Create `CorrelationDataPoint.cs` record
- [ ] Create `CorrelationPlotService.cs`
- [ ] Add CSnakes interface generation
- [ ] Test Python integration

### Phase 3: ViewModel
- [ ] Create `CorrelationPlotViewModel.cs`
- [ ] Implement data loading and plot generation
- [ ] Implement hover detection with cell awareness
- [ ] Subscribe to `StudentHoverMessage`
- [ ] Broadcast hover events

### Phase 4: View
- [ ] Create `CorrelationPlotControl.axaml`
- [ ] Create `CorrelationPlotControl.axaml.cs`
- [ ] Implement SVG display
- [ ] Implement points overlay rendering
- [ ] Implement hover visualization
- [ ] Handle resize with debounced regeneration

### Phase 5: Tab Container
- [ ] Create `PlotTabContainerViewModel.cs`
- [ ] Create `PlotTabContainer.axaml`
- [ ] Create `PlotTabContainer.axaml.cs`
- [ ] Style tabs (modern/sleek appearance)
- [ ] Wire up tab switching

### Phase 6: Integration
- [ ] Update `MainWindowViewModel.cs`
- [ ] Update `MainWindow.axaml`
- [ ] Update `App.axaml.cs` for DI registration
- [ ] Test cross-view hover synchronization
- [ ] Test theme switching
- [ ] Test clipboard copy
- [ ] Test PPTX export

### Phase 7: Polish
- [ ] Performance optimization (lazy loading)
- [ ] Handle edge cases (missing data, single series)
- [ ] Add loading indicator during plot generation
- [ ] Final UI polish

---

## 9. Open Questions

### 9.1 User Decisions (January 1, 2026)

1. **Upper Triangle Display:** ✅ **Corner plot** - blank upper triangle for cleaner appearance

2. **Tab Labels:** ✅ **"Distribution" and "Correlation"** - approved as proposed

3. **Diagonal Display:** ✅ **Selectable** - user can toggle between KDE and histogram

4. **Point Coloring Strategy:** ✅ **Color by series** - points colored based on series color

5. **Correlation Coefficient Display:** Show r value in lower triangle cells

6. **Memory/Performance:** Implement as needed based on testing

7. **Copy/Export Behavior:** Copy current tab only

---

## 10. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Python performance with large grids | Medium | Medium | Limit to reasonable series count, add loading indicator |
| Complex hit testing across cells | Medium | Low | Use grid-based indexing before point search |
| SVG coordinate extraction failure | Low | High | Reuse proven approach from violin plot |
| Hover sync race conditions | Low | Medium | Use existing HoverDelayService |
| Theme inconsistency | Low | Low | Use existing ThemeColors utility |

---

## 11. Timeline Estimate

Not providing time estimates per project guidelines. The checklist in Section 8 outlines all required steps.

---

## Appendix A: Reference - Existing Hover Flow

```
Mouse Move on ViolinPlotControl
        │
        ▼
ViolinPlotViewModel.OnPointerMoved()
        │
        ├─── Calculate scale factors (SVG → Display)
        ├─── Find closest point within 15px tolerance
        └─── Call HoverDelayService.ReportHoverCandidate(studentId)
                │
                ▼
HoverDelayService
        │
        ├─── Calculate mouse velocity (weighted history)
        ├─── Map velocity to delay (200-2000ms)
        ├─── Start/restart DispatcherTimer
        └─── On timer tick: fire OnHoverActivated event
                │
                ▼
ViolinPlotViewModel.OnHoverActivated()
        │
        ├─── Set HoveredStudentId property
        └─── Send StudentHoverMessage(studentId, "violin")
                │
                ▼
IMessenger broadcasts to all subscribers
        │
        ├─── DotPlotViewModel receives (if source != "dotplot")
        │    └─── Updates its HoveredStudentId
        │
        └─── [NEW] CorrelationPlotViewModel receives (if source != "correlation")
             └─── Updates its HoveredStudentId
```

---

*Document prepared for design review. Please provide feedback on open questions before implementation begins.*
