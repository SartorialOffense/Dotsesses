# Dimensionality Reduction Visualizations

**Date:** 2026-01-01
**Status:** Planning

## Overview

Add three new dimensionality reduction visualizations to help understand student score patterns:

1. **2D PCA** - Principal Component Analysis projection
2. **UMAP** - Uniform Manifold Approximation and Projection
3. **t-SNE** - t-distributed Stochastic Neighbor Embedding

All three will use the same data (all numeric scores except Total) and continuous coloring by Total score.

---

## Architecture

### Following Existing Patterns

These visualizations will follow the same hybrid architecture as ViolinPlot and CorrelationPlot:

```
Python (sklearn/umap-learn) → SVG → C# Avalonia overlay for interactivity
```

### Tab Structure

Extend the existing `PlotTabContainer` to include new tabs:

```
[Distribution] [Correlation] [PCA] [UMAP] [t-SNE]
```

Or group them under a "Projections" parent tab with sub-tabs.

### Shared Components

- **DimensionalityReductionDataPoint** - Common model for all three
- **ProjectionPlotControl** - Shared base control (or three separate controls)
- **Parameter sliders** - UMAP and t-SNE need interactive parameter controls

---

## Visualization Details

### 1. 2D PCA

**Purpose:** Linear dimensionality reduction showing main variance directions.

**Python Implementation:**
```python
from sklearn.decomposition import PCA
from sklearn.preprocessing import StandardScaler

# Standardize features
scaler = StandardScaler()
X_scaled = scaler.fit_transform(X)

# Fit PCA
pca = PCA(n_components=2)
X_pca = pca.fit_transform(X_scaled)

# Return: coordinates, explained_variance_ratio_
```

**Features:**
- No parameters needed (deterministic)
- Show explained variance % for each axis in labels
- Continuous coloring by Total score (gradient)

**Axis Labels:** `PC1 (X.X% var)` / `PC2 (X.X% var)`

---

### 2. UMAP

**Purpose:** Non-linear dimensionality reduction preserving local and global structure.

**Python Implementation:**
```python
import umap

reducer = umap.UMAP(
    n_neighbors=15,      # Slider: 5-50
    min_dist=0.1,        # Slider: 0.0-1.0
    metric='euclidean',
    random_state=42
)
X_umap = reducer.fit_transform(X_scaled)
```

**Parameters (with sliders):**

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| n_neighbors | 5-50 | 15 | Local neighborhood size |
| min_dist | 0.0-1.0 | 0.1 | Minimum distance between points |

**Features:**
- Parameter sliders in control panel
- Regenerate on parameter change (with debounce)
- Show current parameter values
- Continuous coloring by Total score

---

### 3. t-SNE

**Purpose:** Non-linear dimensionality reduction optimizing local structure.

**Python Implementation:**
```python
from sklearn.manifold import TSNE

tsne = TSNE(
    n_components=2,
    perplexity=30,       # Slider: 5-50
    learning_rate=200,   # Slider: 10-1000
    n_iter=1000,
    random_state=42
)
X_tsne = tsne.fit_transform(X_scaled)
```

**Parameters (with sliders):**

| Parameter | Range | Default | Description |
|-----------|-------|---------|-------------|
| perplexity | 5-50 | 30 | Balance between local/global |
| learning_rate | 10-1000 | 200 | Step size for optimization |

**Features:**
- Parameter sliders in control panel
- Regenerate on parameter change (with debounce)
- Show current parameter values
- Continuous coloring by Total score

---

## Continuous Coloring Scheme

All three visualizations use the same color gradient based on Total score:

```
Low Score ──────────────────────────────► High Score
   Red      Orange     Yellow    Green      Blue
```

Or a perceptually uniform colormap like `viridis`:
```
Low Score ──────────────────────────────► High Score
  Purple     Blue      Teal     Green     Yellow
```

**Implementation:**
- Normalize Total scores to 0-1 range
- Map to colormap (matplotlib's viridis or custom gradient)
- Include color legend showing score range

---

## UI Layout

### Option A: Flat Tabs
```
┌─────────────┬─────────────┬─────┬──────┬───────┐
│Distribution │ Correlation │ PCA │ UMAP │ t-SNE │
└─────────────┴─────────────┴─────┴──────┴───────┘
```

### Option B: Grouped Tabs (Recommended)
```
┌─────────────┬─────────────┬─────────────┐
│Distribution │ Correlation │ Projections │
└─────────────┴─────────────┴──────┬──────┘
                                   │
                    ┌──────┬───────┼───────┐
                    │ PCA  │ UMAP  │ t-SNE │
                    └──────┴───────┴───────┘
```

### Control Panel (for UMAP/t-SNE)
```
┌─────────────────────────────────────────┐
│  n_neighbors: [====●=====] 15           │
│  min_dist:    [●=========] 0.1          │
│                          [Regenerate]   │
└─────────────────────────────────────────┘
│                                         │
│           [Scatter Plot Area]           │
│                                         │
└─────────────────────────────────────────┘
```

---

## File Structure

```
Dotsesses/
├── Python/Violin/
│   └── dimensionality_reduction.py    # PCA, UMAP, t-SNE functions
├── Models/
│   └── ProjectionDataPoint.cs         # Shared data model
├── Services/
│   └── DimensionalityReductionService.cs
├── UI/
│   ├── PcaPlotControl.axaml(.cs)
│   ├── PcaPlotViewModel.cs
│   ├── UmapPlotControl.axaml(.cs)
│   ├── UmapPlotViewModel.cs
│   ├── TsnePlotControl.axaml(.cs)
│   ├── TsnePlotViewModel.cs
│   └── PlotTabContainer.axaml(.cs)    # Update for new tabs
```

---

## Implementation Checklist

### Phase 1: Python Module & Infrastructure

- [ ] Create `dimensionality_reduction.py` with:
  - [ ] `create_pca_plot()` function
  - [ ] `create_umap_plot()` function
  - [ ] `create_tsne_plot()` function
  - [ ] Shared helper for continuous color mapping
  - [ ] Point data extraction for C# overlay
- [ ] Add umap-learn to `pyproject.toml` dependencies
- [ ] Create `ProjectionDataPoint.cs` model
- [ ] Create `DimensionalityReductionService.cs`
- [ ] Register service in `App.axaml.cs`
- [ ] Add Python module to `.csproj` AdditionalFiles

### Phase 2: PCA Implementation

- [ ] Create `PcaPlotViewModel.cs`
  - [ ] Data preparation (exclude Total)
  - [ ] Call Python PCA function
  - [ ] Store explained variance for axis labels
  - [ ] Point lookup for hover
- [ ] Create `PcaPlotControl.axaml`
  - [ ] SVG display area
  - [ ] Canvas overlay for points
  - [ ] Copy button
- [ ] Create `PcaPlotControl.axaml.cs`
  - [ ] Theme message handling
  - [ ] Hover visualization
  - [ ] Click handlers
- [ ] Update `PlotTabContainerViewModel` for PCA tab
- [ ] Update `PlotTabContainer.axaml` for PCA tab
- [ ] Test PCA rendering and interactions

### Phase 3: UMAP Implementation

- [ ] Create `UmapPlotViewModel.cs`
  - [ ] Parameter properties (n_neighbors, min_dist)
  - [ ] Debounced regeneration on parameter change
  - [ ] Call Python UMAP function
- [ ] Create `UmapPlotControl.axaml`
  - [ ] Parameter sliders panel
  - [ ] SVG display area
  - [ ] Canvas overlay
  - [ ] Regenerate button
- [ ] Create `UmapPlotControl.axaml.cs`
  - [ ] Slider change handlers
  - [ ] Theme handling
  - [ ] Hover/click
- [ ] Update tab container for UMAP
- [ ] Test UMAP with parameter variations

### Phase 4: t-SNE Implementation

- [ ] Create `TsnePlotViewModel.cs`
  - [ ] Parameter properties (perplexity, learning_rate)
  - [ ] Debounced regeneration
  - [ ] Call Python t-SNE function
- [ ] Create `TsnePlotControl.axaml`
  - [ ] Parameter sliders panel
  - [ ] SVG display area
  - [ ] Canvas overlay
- [ ] Create `TsnePlotControl.axaml.cs`
  - [ ] Slider change handlers
  - [ ] Theme handling
  - [ ] Hover/click
- [ ] Update tab container for t-SNE
- [ ] Test t-SNE with parameter variations

### Phase 5: Polish & Integration

- [ ] Add color legend to all three plots
- [ ] Add to PPT export (3 new slides)
- [ ] Add copy-to-clipboard for each plot
- [ ] Ensure consistent hover behavior across all plots
- [ ] Update `MainWindowViewModel` initialization
- [ ] Performance testing with large datasets
- [ ] Final UI polish and spacing

---

## Dependencies

**Python packages to add:**
```toml
[project.dependencies]
umap-learn = ">=0.5.0"
# sklearn already included via scipy/numpy
```

**Note:** umap-learn may require additional compilation on first install.

---

## Decisions Made

1. **Tab structure:** Flat tabs `[Distribution | Correlation | PCA | UMAP | t-SNE]`
2. **Color scheme:** Viridis gradient (purple → blue → teal → green → yellow)
3. **Loading indicator:** Yes, show spinner during computation
4. **Hover:** Participate in cross-view hover (rings only, no tooltips)
5. **PPT export:** Include all three as additional slides

---

## Timeline Estimate

| Phase | Complexity |
|-------|-----------|
| Phase 1: Infrastructure | Medium |
| Phase 2: PCA | Low (deterministic, no sliders) |
| Phase 3: UMAP | Medium (sliders, slower computation) |
| Phase 4: t-SNE | Medium (sliders, slower computation) |
| Phase 5: Polish | Low |

---

## References

- [scikit-learn PCA](https://scikit-learn.org/stable/modules/generated/sklearn.decomposition.PCA.html)
- [UMAP documentation](https://umap-learn.readthedocs.io/)
- [scikit-learn t-SNE](https://scikit-learn.org/stable/modules/generated/sklearn.manifold.TSNE.html)
- Existing correlation plot implementation for architecture reference
