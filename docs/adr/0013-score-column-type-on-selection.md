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
