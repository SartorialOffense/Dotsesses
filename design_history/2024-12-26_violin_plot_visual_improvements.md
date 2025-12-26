# Violin Plot Visual Improvements Plan

**Date**: 2024-12-26
**Status**: Implemented

## Overview

Improve violin plot visual clarity by making dots more prominent and reducing visual clutter from axes/grid elements.

## User Requirements

1. **Dots**: Increase saturation to 100% (currently using seaborn "bright" palette)
2. **Violin areas**: Increase transparency by 10% (from alpha=0.5 to alpha=0.4)
3. **Grid**: Remove horizontal grid lines (currently dashed gray at alpha=0.2)
4. **Y-axis**: Show only min (0) and max (1) tick labels, remove intermediate ticks

## Current Implementation

**File**: `Dotsesses/Python/Violin/violin_swarm.py`

| Element | Current Setting | Line |
|---------|-----------------|------|
| Color palette | `sns.color_palette("bright", len(series))` | 73 |
| Violin alpha | `alpha=0.5` | 80 |
| Dot alpha | `alpha=0.9` | 88 |
| Grid | `ax.grid(axis='y', alpha=0.2, linestyle='--', color='gray')` | 96 |
| Y-axis ticks | Default (0.0, 0.2, 0.4, 0.6, 0.8, 1.0) | (default) |

## Planned Changes

### 1. Increase Dot Color Saturation to 100%

**Problem**: Seaborn's "bright" palette has good saturation but we want maximum saturation.

**Solution**: Create a custom high-saturation palette based on the bright palette or use explicit saturated colors.

```python
# Option A: Use explicit saturated colors
saturated_colors = ['#FF0000', '#00FF00', '#0000FF', '#FFFF00', '#FF00FF', '#00FFFF', '#FF8000', '#8000FF']

# Option B: Increase saturation programmatically
from colorsys import rgb_to_hls, hls_to_rgb

def saturate_color(rgb):
    """Convert RGB (0-1) to fully saturated version."""
    r, g, b = rgb
    h, l, s = rgb_to_hls(r, g, b)
    # Set saturation to maximum
    return hls_to_rgb(h, l, 1.0)
```

**Recommendation**: Option B - programmatically saturate existing palette to preserve color identity.

### 2. Increase Violin Transparency by 10%

**Current**: `alpha=0.5` (50% opaque)
**Target**: `alpha=0.4` (40% opaque, 10% more transparent)

**Change** (line 80):
```python
# Before
sns.violinplot(..., alpha=0.5, ...)

# After
sns.violinplot(..., alpha=0.4, ...)
```

### 3. Remove Horizontal Grid Lines

**Current**: Dashed gray horizontal grid lines at alpha=0.2
**Target**: No grid lines

**Change** (line 96):
```python
# Before
ax.grid(axis='y', alpha=0.2, linestyle='--', color='gray')

# After - remove or comment out
# ax.grid(axis='y', alpha=0.2, linestyle='--', color='gray')
```

### 4. Simplify Y-Axis to Show Only 0 and 1

**Current**: Default matplotlib ticks (0.0, 0.2, 0.4, 0.6, 0.8, 1.0)
**Target**: Only show 0 and 1 at the min/max range

**Change** (add after line 97):
```python
# Set y-axis to show only min and max
ax.set_yticks([0, 1])
ax.set_yticklabels(['0', '1'])
```

## Implementation Checklist

### Phase 1: Python Code Changes
- [x] Add color saturation helper function
- [x] Replace palette creation with saturated version
- [x] Change violin alpha from 0.5 to 0.4
- [x] Remove grid line (comment out or delete `ax.grid(...)`)
- [x] Add `ax.set_yticks([0, 1])` and `ax.set_yticklabels(['0', '1'])`

### Phase 2: Testing
- [ ] Run application and verify violin plot renders correctly
- [ ] Verify dots are more visible with saturated colors
- [ ] Verify violin areas are more transparent
- [ ] Verify no horizontal grid lines appear
- [ ] Verify y-axis shows only 0 and 1

### Phase 3: Optional Refinements
- [ ] Adjust dot alpha if colors are too intense (currently 0.9)
- [ ] Consider whether y-axis label "Normalized Score (0-1)" should be removed since axis shows the range

## Files to Modify

| File | Action |
|------|--------|
| `Dotsesses/Python/Violin/violin_swarm.py` | Modify - all visual changes |

## Notes

- The dots are rendered twice: once in Python (then removed from SVG) and again in C# `ViolinPlotControl.axaml.cs`. The Python rendering determines the *position* and *color*; the C# rendering determines the *appearance* on screen.
- Dot colors are passed from Python to C# as hex strings, so saturation changes in Python will affect C# rendering.
- The C# overlay uses `Opacity=0.8` for filled dots - this may need adjustment if Python colors become too intense.

## Risk Assessment

- **Low risk**: All changes are cosmetic/visual only
- **Reversible**: All changes can be easily reverted
- **No API changes**: Python function signature unchanged
