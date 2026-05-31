# ScoreSelection carries a column-Type discriminator

`ScoreSelection` gains a `Type` field
(`ScoreColumnType.Numeric / Categorical`) that tells the rest of the
app where the column's data lives and whether it participates in the
plots. Numeric columns populate `StudentAssessment.Scores` and feed the
violin plot, correlation matrix, and AggregateScore. Categorical
columns populate `StudentAssessment.Attributes` and appear only in the
drill-down's Attributes section (and the planned color-by-attribute
selector). `Display`, `Aggregate`, and `Correlation` are still on the
record but are meaningless when `Type == Categorical`.

`ScoreReader` decides per column at load time: any non-empty cell that
fails `double.TryParse` (under invariant culture) flips the whole
column to Categorical. Categorical columns produce a new
`ReadWarningKind.CategoricalColumnDetected` warning so the user sees
which columns were auto-classified.

The type-inference pre-scan considers only **Student data rows** — rows
whose first cell holds a numeric Id — using the same `TryReadStudentId`
gate as the row-parsing loop. Trailer rows that real gradebooks carry
below the roster (summary statistics like `AVG`/`Stdev`, repeated header
rows whose cells echo the column names, max-points rows) lack a numeric
Id and are skipped by both. Without this, the literal `"Q1"` in a
repeated-header row would flip the `Q1` column to Categorical even
though every actual student's `Q1` is numeric. Detection and population
must apply the identical row-eligibility rule.

Slice 2 (a follow-up branch) makes `Type` user-toggleable via a
combobox column in the Settings dialog, with `Numeric ↔ Categorical`
both supported. `Categorical → Numeric` is gated on every value
parsing as a `double`; `Numeric → Categorical` is blocked on `Total`
and when it would empty the Aggregate set (parallels to the existing
G2 and G1 guards on `Aggregate`).

## Why on `ScoreSelection` rather than a side dictionary

Every site that consumes a column today is already keyed by
`(Name, Index)`, and `ScoreSelection` is where the Settings UI iterates
columns. Putting `Type` on the same record avoids a parallel list that
can drift, keeps persistence one-to-one with `SavedScoreSelection`, and
follows the same consolidation pattern ADR-0001 set when
`ScoreSelections` was moved onto `ClassAssessment`.

The trade-off is that `ScoreSelection`'s purpose widens slightly from
"three boolean flags" to "type + three flags". The other three flags
ignore-when-Categorical contract is documented on the record and
enforced defensively in `BuildAggregateSet` /
`BuildAggregateKeySet`.

## SavedState migration (extends ADR-0002)

`SavedScoreSelection` gains a `Type` property defaulting to `Numeric`.
`SavedState.Version` bumps from 2 → 3, but the load-rejection gate
stays at `< 2` — v2 files load cleanly and gain `Type = Numeric` via
the JSON default, then silent-rewrite at v3 on first save. This is the
forward-compatible pattern ADR-0002 endorses; no new migration code is
needed.

ADR-0017 follows the same pattern: `SavedAttribute` gains a nullable
`SortOrder` and `Type` gains the `Ordinal` member, bumping
`SavedState.Version` 5 → 6. The rejection gate stays at `< 2`; pre-v6
files load with `SortOrder = null` (no suffix) and silent-rewrite at v6
on first save. `Ordinal` is re-derived from the data at load anyway, so
nothing is lost when an older file omits it.

## Considered alternatives

- **Side dictionary on `ClassAssessment`** — keeps `ScoreSelection` as
  a pure "flags" record but introduces a second per-column list that
  the Settings UI, persistence, and `BuildSeriesData` must all keep in
  sync. Two sources of truth for one fact.
- **Boolean `IsCategorical`** — reads worse at every site
  (`!sel.IsCategorical` everywhere), names only one branch, and makes
  the "the other three flags don't apply" contract less obvious.
- **No `ScoreSelection` entry for categorical columns** — would force
  the Settings UI to merge `ScoreSelections + Attribute column names`
  and lose the natural place for the user to flip a column's type.

## Consequences

- Plot-side code is unaffected: `BuildSeriesData` and `BuildDisplayScores`
  iterate `Scores`, which now never contain categorical data, so
  categorical columns are excluded from violin/correlation by
  construction.
- `BuildAggregateSet` / `BuildAggregateKeySet` filter on
  `Type == Numeric` defensively — a stray `Aggregate=true,
  Type=Categorical` row never enters the aggregate sum.
- `ScoreSelectionDefaults.GenerateDefaults` now takes both `Scores`
  and `Attributes`. A convenience overload preserves the
  `Scores`-only call for tests and pre-categorical code paths.
- The drill-down's `Assessment.Attributes` rendering (already in
  `MainWindow.axaml`) is the load-bearing surface for showing
  categorical data — no view change needed in slice 1.

## Slice 2 — user-driven Numeric ↔ Categorical switching

The Settings dialog grows a per-row `Type` combobox alongside the
existing Display / Aggregate / Correlation checkboxes. On Apply, each
column whose Type changed has its per-student data moved between
`StudentAssessment.Scores` and `StudentAssessment.Attributes`:

- **Numeric → Categorical**: `Score.Value` is stringified via
  `InvariantCulture` `"R"` formatting; `Score.Comment` is preserved into
  the new `StudentAttribute.Comment` (slice 2 also extends
  `StudentAttribute` with an optional `Comment` field — ADR-0007 still
  applies, comments remain per-column-per-student data).
- **Categorical → Numeric**: `StudentAttribute.Value` is parsed via
  `InvariantCulture` `double.TryParse`; `StudentAttribute.Comment` is
  preserved into the new `Score.Comment`.

`ApplyTypeTransitions` runs inside `ApplyScoreSelections` after the
aggregate-set-change detection and before the new selections are stored,
so the subsequent `RecalculateAggregate` operates on the post-move
shape. Because `BuildAggregateKeySet` filters on `Type == Numeric`, a
Numeric column dropping to Categorical triggers `aggregateSetChanged`
and the existing ADR-0005 reseed path fires.

### Guards on the Type setter

The row VM rejects three categories of Type writes silently (no
`SetProperty`, no `IsDirty` flip — matching the existing aggregate
guards' contract):

- **Locked rows** — `Total` stays `Numeric` (it's either the
  spreadsheet's pre-summed Total or our synthesized aggregate mirror,
  numeric by definition).
- **Last-Aggregate Numeric** — a Numeric → Categorical flip on an
  `Aggregate=true` row that is the only Numeric row with `Aggregate`
  enabled is rejected; this would empty the aggregate set as a
  side-effect of a type switch, parallel to the existing G1 last-
  Aggregate-clear guard. Re-uses the same `canClearAggregate` closure.
- **Unparseable Categorical** — a Categorical → Numeric flip is
  rejected when the parent VM's injected `canSwitchToNumeric`
  predicate returns false (some `StudentAttribute.Value` strings for
  the column don't parse as `double`). The Settings dialog injects a
  predicate that walks `ClassAssessment.Assessments[*].Attributes`.

### Bulk-toggle commands

`DisplayAll/None`, `AggregateAll`, and `CorrelationAll/None` skip
Categorical rows entirely — the three flags have no effect on
Categorical columns and bulk-flipping invisible values would surprise
users. `LastAggregateGuard` counts only Numeric Aggregate rows.

## Considered alternatives for slice 2

- **Drop `Score.Comment` silently on conversion** — smallest diff but
  surprising data loss; rejected.
- **Block conversion when any comment exists** — safest but pushy;
  user would have to clear comments before flipping type.
- **Confirmation dialog on conversion when comments exist** —
  acceptable but adds modal friction; chose data preservation instead.
- **Read-only `Type` discriminator (slice 1 only)** — defers the
  reverse-direction question forever; categorical-by-accident becomes
  impossible to undo without reloading the file.
