# GradeCurve targets are percentage ranges, not absolute counts

The school's GradeCurve is expressed as a list of `CutoffCountRange`
entries — a Grade plus `LowerBound`/`UpperBound` integer count targets
derived from a percentage range applied to the current class size.
This replaced an earlier shape that used a single absolute student
count per Grade.

## Why

A real curve policy isn't a fixed number per Grade — it's a permitted
range as a fraction of the class. With class sizes that vary year to
year, percentage ranges generalize cleanly; absolute counts do not.
The percentages live in `DefaultCurveGenerator` and are multiplied
through the actual class size at load.

## Consequences

- Compliance UI must render a range ("3–7"), not just a target.
- The Grade set was extended to 11 entries (A, A-, B+, B, B-, C+, C,
  C-, D+, D, F) to match the policy's vocabulary; some Grades
  legitimately have a `0–0` range.
- Any "default count" callers must pass the current class size.
