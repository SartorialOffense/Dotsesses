# Significance flag + Significance Matrix plot

A fourth flag — `Significance` — joins `Display`, `Aggregate`, and
`Correlation` on `ScoreSelection`. It controls inclusion in a new
**Significance Matrix** plot tab (matrix of small scatter cells with
one row per Numeric column and one column per Categorical column —
each cell shows one dot per **Subgroup** with mean ± SEM error bars).

`Significance` is unique among the four flags: it is meaningful for
both Numeric (the column becomes a matrix *row*) and Categorical (the
column becomes a matrix *column*). The other three flags ignore-when-
Categorical per ADR-0013 slice 2.

`SavedState.Version` bumps 3 → 4. v3 files default `Significance=true`
on load so existing project files open with a populated matrix on the
new tab (matches the ADR-0002 silent-migration pattern).

## Why a separate flag instead of reusing `Display`

`Display` filters the violin plot. `Correlation` already exists as an
example of "user wants this column in one matrix view but not another."
Significance Matrix is a third view with a different question — "does
this column's subgroups predict *that* column's mean?" — and users will
plausibly want independent control. Adding `Significance` from the
start avoids the eventual painful split that `Correlation` itself was
introduced to solve.

## Scope is descriptive only

This slice ships the **descriptive** mean + SEM matrix. A future slice
4 layers in a per-cell **inferential test** (Welch's ANOVA or Kruskal–
Wallis, TBD) as a small p-value annotation per cell. The data path is
designed to absorb that addition without restructuring: each Python-
returned point dict already has room for a `p_value` field, and
`SignificanceDataPoint` will gain a corresponding C# property when the
inferential layer lands.

The user's framing made this scope split natural: they want to *see
the subgroup means with error bars now, and add a significance
indicator eventually*.

## Plot layout decisions

- **Y-scale shared per row** — every cell in the "Q3" row uses
  `[min(mean−SEM), max(mean+SEM)] × 5% padding` across the row.
  Different rows have independent y-scales (different numeric ranges).
- **No fixed cell aspect ratio** — cells stretch to fill the available
  area.
- **Per-column color reset** — subgroups within `Hat` use
  `CYCLING_PALETTE[0..n]`; subgroups of `Submitted Outline` independently
  restart at `[0..m]`. The same subgroup string in different categorical
  columns can be different colors. Within a single categorical column,
  the same subgroup always gets the same color across all rows.
- **Subgroup ordering: alphabetical ascending** — predictable; users
  can rename to `"1-Low"` / `"2-Medium"` / `"3-High"` if alphabetical
  doesn't match their intent.
- **Small-N handling**: N=0 omits the dot entirely; N=1 renders dot
  with no error bar (tooltip shows `N = 1`); N≥2 renders dot + SEM
  error bar.
- **Hover semantics: no cross-view sync.** Dots are subgroups, not
  students. The tooltip is the entire interaction model in v1.

## Pipeline mirrors the correlation matrix

`SignificancePlotService` ↔ `CorrelationPlotService`,
`SignificancePlotViewModel` ↔ `CorrelationPlotViewModel`,
`SignificancePlotControl` ↔ `CorrelationPlotControl`,
`significance_matrix.py` ↔ `correlation_matrix.py`. The Python module
returns SVG + a per-point list of dicts; C# parses, hosts the SVG in
an `Image` control, and overlays per-dot `Ellipse` shapes on a
`Canvas` with Avalonia `ToolTip` attached for hover content.
Resize debounces 150 ms before re-invoking the Python renderer.

## Considered alternatives

- **Reuse `Display` for both violin and significance matrix inclusion.**
  Smaller diff but conflates two distinct user intents.
- **Layout: per-cell y-scale.** Maximises each cell's vertical
  resolution but breaks cross-categorical comparison within a row.
- **Layout: zero-anchored y-scale.** Wastes the vertical band where
  the interesting subgroup differences live (subgroup means typically
  cluster within a small percent of the full score range).
- **Global subgroup color indexing.** Would mean "Yes" in two
  different categoricals get adjacent palette colors, which doesn't
  match how users read each matrix-column independently.
- **Frequency-ordered subgroups within a column.** Adaptive but
  introduces order changes when the data shifts.

## Consequences

- `SavedState` v3 → v4. Old files silent-migrate.
- Settings dialog grows from 5 control columns to 6
  (`Score | Type | Display | Aggregate | Correlation | Significance`).
- `BuildCategoricalSeriesData` joins `BuildSeriesData` on
  `MainWindowViewModel` as the second canonical column-data builder.
- `ApplyScoreSelections` fires a third regeneration alongside
  violin + correlation. No new mutual ordering requirement.
- A future slice 4 will extend the per-cell point dict with `p_value`
  and the `SignificanceDataPoint` record with a `PValue` field.
  Decision on the actual test (Welch's ANOVA / Kruskal–Wallis / per-
  cell t-test) is deferred to that slice.
