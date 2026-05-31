# Dotsesses — UX Specification

Dotsesses visualizes a Class's exam Scores as a dotplot histogram with
hover drill-down and draggable GradeCutoff cursors. It shows score
distributions, individual Score components via a violin plot, and helps
assign letter Grades that match the school's GradeCurve.

The name is a play on the incorrect plural of "dot". The visual theme is
playful and reminiscent of early grade school.

> **Companion docs**
> - **Domain language** — `CONTEXT.md`
> - **Architectural decisions** — `docs/adr/`
> - **Tech debt** — `TECH_DEBT.md`

---

## Interaction Model

The application uses **hover-based interaction** for all Student data
viewing:

- Hovering over a dot (in the dotplot or violin plot) shows the
  Student's details in the Drill-down panel.
- Double-click or right-click a dot to open the comment editor for the
  Score under the cursor.
- Hover state is synchronized between dotplot and violin plot.
- No persistent selection — hover state changes as the mouse moves.

Statistics on the dotplot (mean, standard deviations) are calculated
from the current dataset and **do not change** when individual Grades
are enabled/disabled. The number of GradeBins / Region Bands varies
based on which Grades are enabled.

## Layout

Two-row layout with resizable splitters.

### Top section — fixed 175px height

Left to right:

1. **Color Selection panel** — collapsible side panel for coloring dots
   by StudentAttribute.
2. **Size panel** — collapsible side panel with dot-size slider.
3. **Three-part Dotplot** — the main visualization (Statistics Display,
   Dot Display, Grade Cursors).
4. **Curve Compliance panel** — collapsible side panel showing the
   GradeCurve target ranges and current counts.

Subtle horizontal splitter between top and bottom sections. Dragging it
adjusts only the Dot Display height — Statistics Display and Grade
Cursors stay fixed at ~30px each.

### Bottom section — variable height

Left to right with a vertical splitter:

1. **Drill-down panel** (left, 300px initial width) — currently-hovered
   Student's record.
2. **Plot tabs** (right, fills remaining) — tabbed surface containing
   the violin plot, the correlation matrix, and the significance
   matrix.

All collapsible panels use a hamburger icon and rotated text in the
collapsed state.

## Dotplot

The Dot Display is the middle area of the three-part dotplot. Scores
distribute horizontally; alternating Region Bands sit behind the dots.

### X-axis (AggregateScore)

AggregateScore drives the x position. Lowest Score on the left, highest
on the right, with padding for cursor labels on both ends.

### Y-axis (GradeBin stacking)

Students with the same AggregateScore stack into the same GradeBin,
ordered by Student Id for stable rendering across redraws. Dot spacing
is double the marker size.

**Bin offset:** GradeBins at odd AggregateScores receive a +0.1 y
offset, so adjacent bins read as visually distinct.

**Y padding:** top and bottom padding equals
`max_students_in_bin × 0.1`.

### Hover and clicks

All hit-testing uses screen-space (pixel) coordinates so behavior is
consistent regardless of data scale.

- Student hover: Euclidean pixel distance, hits within 10 px.
- Double-click or right-click: opens the comment editor for the Score
  under the cursor.
- Cursor drag: only initiates when the click lands within 3 data units
  horizontally and within the Grade Cursors y-band — preventing
  accidental drags from clicks on Region Band edges.

### Dot appearance

- **Size:** Size slider, range 2–10, default 2.
- **Shape:** filled circle if all of the Student's Scores are
  comment-free; hollow square if any Score has a Comment.
- **Color:** white by default; colored by StudentAttribute value when
  color-by-attribute is active.

### Color-by-attribute

The Color Selection panel chooses a StudentAttribute to color dots by.
Each distinct value gets a color; a small in-panel legend lists the
mapping. A handful of common attribute values have hard-coded colors
("Yes" → green, "No" → red, etc.); everything else falls back to a
default palette.

### Region Bands

Alternating shaded backgrounds, one per enabled Grade region. Resize
live as Grade Cursors drag.

### Axes

X-axis and Y-axis both hidden — no ticks, no labels, no titles. Thin
gray border around the plot rectangle.

## Statistics Display

Top ~30px row of the three-part dotplot. Displays "μ" centered at the
mean, plus "+1σ", "-1σ", "+2σ", "-2σ" labels for as many standard
deviations as fit within the AggregateScore range. Shares its x-axis
with the Dot Display and Grade Cursors. Statistics are calculated from
the full dataset and do not change with Grade enable/disable.

## Grade Cursors

Bottom ~30px row of the three-part dotplot. Draggable dashed vertical
lines mark GradeCutoffs, with the Grade letter labeled below.

- Cursors cannot overlap, and must stay at least 1 AggregateScore unit
  apart.
- The lowest Grade in the active set has no cursor — its region runs
  from the left boundary to the second-lowest Grade's cursor.
- Enabling or disabling a Grade triggers Compliance recalculation and
  Region Band redraw.

Initial placement and re-seeding behavior live in the calculators
(`InitialCutoffCalculator`, `CursorPlacementCalculator`,
`CursorValidation`). See ADR-0005 for when ScoreSelection changes
trigger a full re-seed.

## Drill-down panel

Left panel, 300 px initial width. Displays the hovered Student's full
record:

1. **Header** — MuppetName plus assigned Grade.
2. **Score list** — one row per Score in the active Display selection,
   each with an editable comment box. The comment is a property of the
   Score, not of the Student (see ADR-0007).
3. **Attribute list** — Student's StudentAttributes (Name → Value).
4. Thin separator between Scores and Attributes.

**Background:** RGB(0, 0, 0). **Border:** blue (#007ACC), 2 px when a
Student is hovered.

## Violin plot

One of the tabs in the bottom-right surface. Multi-series violin plot
with swarm overlay.

- Generated by Python (matplotlib + seaborn) via CSnakes; SVG returned
  to the C# side and rendered with an Avalonia shape overlay for
  hit-testing and hover effects.
- One series per Score whose **Display** ScoreSelection flag is on.
  **Default selection** (fresh load, see ADR-0016): the **Total** column
  plus the first **10** non-Total numeric columns in left-to-right order
  (≤ 11 series). Adjustable in Settings.
- Hollow square for any Score with a Comment; filled circle otherwise.
- Hover: highlights the same Student in every series and in the
  dotplot. Tooltip shows Score value and that Score's Comment. Each
  hovered Student gets one tooltip per series; these normally float
  beside their dots, but when **more than 10** score series are shown
  they crowd the dots, so they relocate to the very top of the plot
  (each still horizontally aligned to its series column). Odd-indexed
  series are staggered down by one tooltip height into a second row so
  neighbouring tooltips stay legible.
- Resize debounce: 300 ms before regenerating the Python plot.
- Click / double-click / right-click — same comment-editor behavior as
  the dotplot.
- A series whose Scores are all the same value (e.g. an extra-credit
  column where every Student earned the same point) renders as a flat
  midline rather than a violin — the per-series 0-1 normalization has
  no range to spread.
- If Python plot generation throws (bad data shape, missing
  dependency, etc.) the failure is shown in a dialog naming the plot
  and the underlying error; the rest of the UI stays usable.

## Correlation matrix

Sibling tab in the bottom-right surface. Pearson correlation between
every pair of Scores whose **Correlation** ScoreSelection flag is on.
No synthetic AggregateScore series is added to the matrix — correlating
an aggregate against its own components would be misleading.

**Default selection** (fresh load, see ADR-0016): the columns appearing
in the **4 highest-r² pairs** among non-Total numerics, plus the **Total**
column. (Total is excluded from the pair ranking — it tracks its
components and would otherwise win every slot — but is included so the
matrix opens with the overall-score column present.) The user can change
this in Settings.

## Significance matrix

Third tab in the bottom-right surface, after Distribution and
Correlation. A matrix of small scatter cells — one row per Numeric
column with **Significance**=true, one column per StudentAttribute
(Categorical) column with **Significance**=true. Each cell answers:
*does subgroup membership in this categorical column materially shift
the average of this numeric column?*

**Default selection** (fresh load, see ADR-0016): using Welch's ANOVA
p-values, a categorical column qualifies when its smallest p across the
numerics is **≤ 0.2**; each qualifier brings in its **3 numerics with the
smallest p**. The matrix opens on the union of qualifying categoricals and
those numerics (empty matrix if none qualify). A **Grade**/**Grades**
column is never auto-selected — it's derived from the scores and would
test as trivially significant. Adjustable in Settings.

A plain-language guide for instructors — what the p-value means, the two
test families, when to prefer each, and citations for papers — lives at
[`docs/significance-guide.md`](docs/significance-guide.md).

- Each cell plots one dot per **Subgroup** of its categorical column.
  Dot x-position: subgroup label (alphabetical ascending). Dot
  y-position: mean of the cell's numeric column within that subgroup.
  Vertical line: ±SEM error bar (N ≥ 2 only; N=1 dots render with no
  whisker).
- Coloring: per-subgroup, from the same `CYCLING_PALETTE` as the
  violin plot, indexed positionally within each categorical column
  (the index resets per column, so "Yes" in *Hat* and "Yes" in
  *Submitted Outline* may be different colors).
- Y-scale: shared per row using
  `[min(mean − SEM), max(mean + SEM)] × 5% padding` across the row.
- Hover tooltip on each dot:

  ```
  Hat = Yes   (N = 42)
  Q1: mean 78.3 ± 1.2 (SEM)
  ```

  N=1 collapses to `Q1: 78.3 (N = 1)` (no ± since SEM is undefined).
  The tooltip also carries the cell's test result line (see below) —
  e.g. `Welch ANOVA: p = 0.003`, `Kruskal–Wallis: not testable
  (< 2 groups with N ≥ 2)`, or `excluded from test: N < 2` on a dot
  whose subgroup was dropped.
- **Significance annotation** (top-right corner of each cell): the cell's
  omnibus p-value plus tiered stars — `*` p<.05, `**` p<.01,
  `***` p<.001. Significant cells render **bold** in a strong color;
  non-significant cells render their p faint/grey with no star; cells that
  can't be tested show an em-dash (`—`). Cells with no dots get no
  annotation. p-values are **raw / uncorrected** — the matrix is an
  exploratory screening view, not a confirmatory multiple-comparison
  procedure.
- **Test family selector** (top-right combo box): switches every cell between
  *Parametric* (Welch's ANOVA — reduces to Welch's t for 2 subgroups) and
  *Non-parametric* (Kruskal–Wallis — reduces to Mann–Whitney for 2). One
  family is applied matrix-wide; the choice is **persisted** with the
  workspace (SavedState v5; v4 files open as Parametric). The same family
  handles categorical columns with 2 or 2+ values — no group-count
  branching. Default: Parametric.
- **Small-N test policy**: subgroups with N<2 are dropped from the test
  (their dots still render); the cell is tested only if ≥2 valid subgroups
  remain, otherwise the annotation is `—`.
- **No cross-view sync** — the dots represent subgroups, not students,
  so hovering a matrix dot does not highlight anything in the dotplot
  or violin, and vice versa.
- Generated by Python (matplotlib) via CSnakes (same pipeline shape as
  the correlation matrix); SVG returned to the C# side and rendered
  with an Avalonia shape overlay for per-dot hover.
- Resize debounce: 150 ms before regenerating the Python plot.
- If every Numeric column has Significance=false or every Categorical
  has Significance=false, the tab renders the empty-state hint
  "No Significant rows or columns to plot."

A future revision may add an **effect-size** measure (η²/ε²) alongside the
p-value so cells convey *magnitude*, not just detectability — see
ADR-0015.

## Curve Compliance

Collapsible panel listing the active GradeCurve. Per Grade:

- Letter (rendered as "C-" not "CMinus" etc.).
- **Target range** — lower/upper bound from the GradeCurve's
  CutoffCountRange (see ADR-0006).
- Current count under the present GradeCutoffs.
- Absolute deviation when the current count is outside the target
  range — light blue if under, red if over.

Per-Grade enable checkboxes to the left toggle whether that Grade
participates in cursor placement and Region Band rendering.

## Color Selection panel

Dropdown of StudentAttribute names plus a value→color legend.
Collapses to a rotated "Color by" label.

## Size panel

Single slider (2–10, default 2) bound to dot marker size in both the
dotplot and the violin plot. Collapses to a rotated "Size" label.

## Settings dialog

Modeless dialog (single-instance, owner = MainWindow) launched from
the toolbar, the application menu, or `Cmd+,`. Contains a sectioned
surface designed to grow; the only category today is **Score
Selection** — a table with one row per column and five control
columns: **Type** (Numeric / Categorical combobox), **Display**,
**Aggregate**, **Correlation**, and **Significance**. The four
checkbox columns have per-column All/None toggles in the header; Type
does not (bulk-flipping column kinds is almost certainly user error).

When a row's Type is **Categorical**, Display / Aggregate / Correlation
disable — those flags are meaningless for categorical columns (the
data lives in StudentAttributes and bypasses the violin / correlation /
aggregate paths). The Display / Aggregate / Correlation bulk All/None
commands skip Categorical rows for the same reason.

**Significance is special**: it is meaningful for *both* Numeric and
Categorical rows. A Numeric column with Significance=true becomes a
row in the Significance Matrix; a Categorical column with
Significance=true becomes a column in the Significance Matrix. The
Significance bulk All/None commands therefore apply to every row,
regardless of Type. There is no locked-Total or last-row guard — an
empty Significance Matrix is a fine degenerate state (the tab shows
an empty-state hint).

Switching a column's Type on Apply moves its per-student data:

- **Numeric → Categorical**: each `Score.Value` is stringified
  (invariant-culture round-trip format) into a new
  `StudentAttribute.Value`; any existing `Score.Comment` is preserved
  into `StudentAttribute.Comment`. If the column was part of the
  AggregateScore, the cursors are re-seeded per ADR-0005.
- **Categorical → Numeric**: each `StudentAttribute.Value` is parsed
  as a `double`; `Comment` is preserved. Display / Aggregate /
  Correlation default off until the user re-enables them.

Dialog buttons: **Apply** (commit pending toggles, recompute, dialog
stays open) and a second button whose label is **Cancel** while there
are unapplied changes and **Close** when the dialog is in sync. See
ADR-0003 (recompute timing), ADR-0013 (Type discriminator + Apply-time
conversion), and the broader Settings decisions in `docs/adr/`.

Structural guards on the Score Selection table:

- The "Total" Score (if present in the loaded data) appears as a row
  with its **Aggregate** checkbox locked off — Total is a derived
  output, not an input. **Total's Type is also locked** to Numeric.
- The user cannot un-check the last remaining **Aggregate** checkbox —
  AggregateScore must always be defined by at least one Score.
- The user cannot flip the last Aggregate-on Numeric row to
  Categorical (parallels the last-Aggregate-clear guard).
- The Categorical → Numeric Type switch is rejected when not every
  stored `StudentAttribute.Value` parses as a number.

## Persistence

State (current GradeCutoffs, ScoreSelections, Score Comments, named
SavedCutoffs) saves to a `.dots` JSON file. Older `.dots` files load
cleanly and silently migrate on first re-save. See ADR-0002.

## Loading score files

On launch, the splash window prompts for a score file (`.xlsx`/`.xls`)
or a saved state file (`.dots`). Pick one to open the main window with
that Class loaded.

Once a window is open, the **📂 Open Another File** button in the
drill-down toolbar prompts for a second file and opens it in a **new
top-level window**. Each window is fully independent — drags, hover,
comment edits, and Settings in one window do not affect any other (see
ADR-0012). Repeat the action to open as many files as needed; each
gets its own window. Closing one window leaves siblings untouched;
the app exits only when the **last** window is closed. Each window's
DI scope is disposed deterministically on close, releasing the
loaded Class for GC.

Loading an `.xlsx` score file routes through `ScoreReader`. The reader
tolerates messy spreadsheets — blank rows are skipped, missing cells
in a score column drop only that one cell, and rows without a parseable
ID in column A are skipped. Hard errors (no ID column, no readable
rows at all) abort the load and surface the existing error dialog.

After a successful load, if the reader detected non-fatal issues, a
single combined dialog lists each one. The categories it flags:

- **No `Total` column** — one was synthesized as the sum of every
  numeric column. If the spreadsheet already has an aggregate column
  named something else (e.g. `ALL`, `Sum`, `Final`), rename it to
  `Total` so it isn't double-counted in the default aggregate.
- **Duplicate column headers** — only one of each duplicate is
  readable.
- **Orphan `(Notes)` column** — its base name doesn't match any score
  column, so its comments aren't read.
- **Duplicate student IDs** — downstream lookups may pick the wrong
  row.
- **Skipped rows** — N rows had data but no valid ID in column A.
- **Sparse column** — values present for only some students.
- **Constant column** — every student has the same value; the violin
  for that column renders as a flat midline. Only emitted for numeric
  columns — an all-`Yes` categorical column is a meaningful pattern,
  not a flat-violin concern.
- **Categorical column detected** — a column with any non-numeric,
  non-empty cell is loaded as a categorical StudentAttribute rather
  than a numeric Score. It appears in the Drill-down panel's
  Attributes list but is excluded from the violin plot and correlation
  matrix.

## Export

- **Excel** export with columns for Student Id, AggregateScore, individual
  Scores, StudentAttributes, and assigned letter Grade.
- **PowerPoint** export — one slide each for the dotplot, violin plot,
  correlation matrix, and significance matrix, plus a grade-breakdown
  table. Plots render in the light theme (dark text on white) while the
  bright `CYCLING_PALETTE` dot colors are preserved. The significance
  slide carries a footer documenting the per-cell test used (Welch's
  ANOVA / Kruskal–Wallis), the star tiers, and that the p-values are raw
  (exploratory).
- **Copy plot** to clipboard for individual plots.

## Snapshot mode (for automated UI verification)

The application can render itself to a PNG and exit, for use by Claude
Code or other agents:

```bash
dotnet run --project Dotsesses/Dotsesses.csproj -- --snapshot
# optionally: -- --snapshot --output /path/to/file.png
```

It prints the snapshot path to stdout. Implementation is in
`MainWindow.SaveSnapshotAsync`: 200 ms render wait, layout pass,
RenderTargetBitmap at 96 DPI, PNG quality 100.

## Tech stack

- .NET 9, Avalonia 11, CommunityToolkit.Mvvm, OxyPlot.Avalonia,
  CSnakes (Python integration for violin / correlation), ClosedXML
  (Excel), Serilog, xUnit. Dark theme variant.
- MVVM via convention-based `ViewLocator`
  (`*ViewModel` → `*View`).
- Python (matplotlib / seaborn) lives in `Dotsesses/Python/Violin/`.

Architectural decisions that shape this stack and the messaging /
recompute / persistence patterns are recorded in `docs/adr/`.
