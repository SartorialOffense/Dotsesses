# Comments live on Score, not on StudentAssessment

Free-text comments are a property of `Score` (one optional Comment per
Score), not a single Comment on `StudentAssessment`. The Excel ingest
pairs columns ending in `(Notes)` with their corresponding Score
column, semicolon-delimited entries become newlines. The Drill-down UI
renders an editable comment box per Score, not one global box per
Student.

## Why

In practice, instructor commentary is per-question / per-component
("they really screwed the pooch on that analysis!!!" attaches to a
specific Score, not the Student as a whole). One Comment per Student
forced unrelated remarks into a single text blob. Per-Score comments
also let the violin plot's per-series tooltip show the comment relevant
to *that* score component on hover.

## Consequences

- A Student with any non-empty Score Comment renders as a hollow square
  in the dotplot and violin (the "has comment" marker is now
  per-Student-aggregated from per-Score Comments).
- The violin plot's hover tooltip shows the Comment for the Score the
  user is hovering, not a generic Student comment.
- `StudentAssessment` has no `Comment` property — drift in any older
  doc referring to one should be corrected.

## Extension — comments on StudentAttribute (ADR-0013 slice 2)

`StudentAttribute` gained an optional `Comment` field so that
per-cell comments survive a `Numeric ↔ Categorical` type conversion in
Settings. The contract above is unchanged: comments remain
*per-column-per-student* data, never a single per-Student field. The
extension is symmetric — `Score.Comment` and `StudentAttribute.Comment`
both store the same kind of per-cell note, and they round-trip across
type conversions.
