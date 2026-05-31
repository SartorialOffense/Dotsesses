# Data-driven default column selection per plot

`ScoreSelectionDefaults.GenerateDefaults` historically turned **every**
column on for **every** plot (`Display` / `Correlation` / `Significance`
all `true`). Real gradebooks carry 30–40+ columns, so each plot opened
unreadably crowded. At fresh load we now pare the initial selection down
to a sensible, data-driven subset per plot. The user can still change any
of it in Settings; this only affects the *initial* seed, and only when
selections are empty (`SeedDefaultSelectionsIfEmpty` already no-ops for
loaded `.dots` files).

## The three rules

- **Distribution (`Display`)** — `Total` plus the first **10** non-Total
  numeric columns in left-to-right (column) order. ≤ 11 series.
- **Correlation (`Correlation`)** — Pearson **r²** for every pair of
  non-Total numeric columns; the columns in the **4 highest-r² pairs**,
  plus **Total**.
- **Significance (`Significance`)** — Welch's ANOVA p-values per
  (numeric, categorical) cell; a categorical *qualifies* if its smallest
  p across numerics is **≤ 0.2**; each qualifier contributes its **3
  smallest-p numerics**; the union of qualifying categoricals and those
  numerics is selected. A **Grade**/**Grades** categorical is excluded
  from auto-selection (it's derived from the scores, so it tests as
  trivially significant — same spirit as excluding Total from the
  correlation ranking). The user can still enable it in Settings.

`Aggregate` is untouched (still every non-Total numeric; it drives the
dotplot AggregateScore, independent of which plots show which columns).

## Why these specific heuristics

- **Total always in** Distribution and Correlation — it's the column
  professors care about most; anchoring both plots on it is predictable.
- **Total excluded from the r² ranking** — Total correlates strongly with
  its own components, so including it would make all 4 top pairs
  `Total × something` and crowd out the interesting inter-component
  structure. Ranking the non-Total pairs surfaces that structure; Total is
  then added back explicitly.
- **p ≤ 0.2, top-3** for Significance — 0.2 is deliberately loose (this is
  an *exploratory screening* default, not a significance claim — see
  ADR-0015), wide enough to surface attributes worth a look; top-3 keeps
  each qualifying categorical's column to a readable handful.
- **Welch (parametric)** matches the matrix's default test family
  (ADR-0015). The selection is computed once at load; the user can still
  toggle the matrix's test family afterward without re-seeding.

## Implementation notes

- `Calculators/PlotSelectionCalculator.cs` is pure and keyed by *series
  name* (the join key the plots already use), so the caller applies
  results by recomputing each `ScoreSelection`'s series name. Pearson r²
  is computed in C# (closed form, no new dependency).
- Significance p-values reuse the existing `compute_cell_pvalue`
  (ADR-0015) via a new one-shot Python helper
  `compute_significance_pvalues` (surfaced as
  `SignificancePlotService.ComputeCellPValues`) — one CSnakes call for the
  whole grid instead of one per cell.
- When the Python service is unavailable (the unit-test factory), the
  Significance refinement is skipped and the base flags are kept; the pure
  Distribution/Correlation rules still apply.

## Considered alternatives

- **Keep all-on defaults.** Simplest, but the crowding is the whole
  problem.
- **Include Total in the r² ranking.** Rejected — Total dominates.
- **Return r² from the correlation render to C#** instead of computing
  Pearson in C#. Rejected — couples selection to render and changes a
  return contract; C# Pearson is trivial.
- **One `ComputeCellPvalue` interop call per cell.** Rejected — hundreds
  of boundary crossings at load; the batch helper is one call.

## Consequences

- Correlation now shows `Total` by default (it did not, in spirit,
  before). SPEC's correlation note is reworded: no *synthetic*
  AggregateScore series is added, but the Total column is selected by
  default.
- `MainWindowViewModel` gains a `SignificancePlotService` dependency (used
  only at seed time).
- Selection is now data-dependent, so default-selection tests assert
  structural invariants (Total-always-in, ≤ 11 display, ≤ 9 correlation)
  rather than all-true.

## Addendum: the Significance "Optimize" button

An interactive **Optimize** button on the Significance plot reuses this
machinery. Holding the currently-displayed categorical columns fixed, it
ranks every (numeric × shown-categorical) cell by p — using the matrix's
**current** test family (not always Welch) — takes the 10 lowest-p cells,
and sets the numeric rows to the numerics in them (replacing the current
rows). Shared code: `SignificancePlotService.ComputeCellPValues` (the
p-grid), a private `TestableCells` cell-enumerator in
`PlotSelectionCalculator` (used by both `SelectSignificance` and the new
`SelectTopCellNumerics`), and a `BuildSignificancePLookup` helper +
`ApplyScoreSelections` on `MainWindowViewModel`. The difference is the
selection rule: auto-select qualifies categoricals (min p ≤ 0.2) and
takes top-3 numerics each; Optimize takes a global top-10 cells over a
fixed categorical set and returns only numerics. The button reaches
`MainWindowViewModel` through a direct callback
(`SignificancePlotViewModel.OptimizeRequested`) per ADR-0004
(single consumer).
