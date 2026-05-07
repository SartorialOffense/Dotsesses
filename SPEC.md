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
   the violin plot and the correlation matrix.

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
- Hollow square for any Score with a Comment; filled circle otherwise.
- Hover: highlights the same Student in every series and in the
  dotplot. Tooltip shows Score value and that Score's Comment.
- Resize debounce: 300 ms before regenerating the Python plot.
- Click / double-click / right-click — same comment-editor behavior as
  the dotplot.

## Correlation matrix

Sibling tab in the bottom-right surface. Pearson correlation between
every pair of Scores whose **Correlation** ScoreSelection flag is on.
The user-defined AggregateScore deliberately does **not** appear in
the matrix — correlating an aggregate against its own components would
be misleading.

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
Selection** — a table with one row per Score column and three
checkbox columns: **Display**, **Aggregate**, **Correlation**, plus
per-column All/None toggles in the header.

Dialog buttons: **Apply** (commit pending toggles, recompute, dialog
stays open) and a second button whose label is **Cancel** while there
are unapplied changes and **Close** when the dialog is in sync. See
ADR-0003 (recompute timing) and the broader Settings decisions in
`docs/adr/`.

Structural guards on the Score Selection table:

- The "Total" Score (if present in the loaded data) appears as a row
  with its **Aggregate** checkbox locked off — Total is a derived
  output, not an input.
- The user cannot un-check the last remaining **Aggregate** checkbox —
  AggregateScore must always be defined by at least one Score.

## Persistence

State (current GradeCutoffs, ScoreSelections, Score Comments, named
SavedCutoffs) saves to a `.dots` JSON file. Older `.dots` files load
cleanly and silently migrate on first re-save. See ADR-0002.

## Export

- **Excel** export with columns for Student Id, AggregateScore, individual
  Scores, StudentAttributes, and assigned letter Grade.
- **PowerPoint** export of the current plots.
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
