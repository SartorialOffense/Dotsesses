# Changing the AggregateScore composition reseeds GradeCutoffs

When `ApplyScoreSelections` detects that the **Aggregate** subset of
ScoreSelections has changed (i.e. the set of Scores summed into
AggregateScore is different — compared via `SetEquals` on the
pre-mutation selections), the GradeCutoffs are re-seeded from the
GradeCurve at the new AggregateScore range — the same path
`LoadFromExcelFile` uses on first load. **Display-only and
Correlation-only changes do not trigger a reset.**

## Why

When the AggregateScore composition changes, every Student's
AggregateScore value shifts, which can leave previously-placed
GradeCutoffs stranded above the max, below the min, or clustered
awkwardly. Re-seeding from defaults gives a predictable, sensible
starting point — the same behavior the user already accepts on a fresh
file load. Display and Correlation toggles don't change AggregateScore
values, so existing GradeCutoffs remain meaningful and a reset would
be surprising.
