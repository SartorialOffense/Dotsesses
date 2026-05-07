# `GradingSession.Slots` excludes the structural catch-all Grade

The `Slots` collection on a `GradingSession` exposes a `CutoffSlot`
for every Grade in the session's `DefaultCurve` **except** the
structural catch-all — the lowest-Order Grade in `DefaultCurve` at
construction time, regardless of whether that Grade has a non-zero
`CutoffCountRange`. The catch-all has no slot, is always present
in `GradingState.EnabledGrades`, and cannot be moved, disabled, or
re-enabled. Its score lives on a private field inside the session
and is only mutated by `LoadCutoffs` / `ReseedFromDefaults` / a
reseed inside `EnableGrade`.

> **2026-05-07 (issue #18)**: Earlier wording said "lowest-Order in
> the *initial cutoffs*." That implementation post-filtered the
> curve and dropped zero-range grades before picking the catch-all,
> so production (where CMinus, DPlus, D, F all have zero range)
> ended up with `C` as the catch-all and four grades structurally
> absent from `Slots`. The intended structure has always been
> "lowest-Order in `DefaultCurve`" — issue #18 corrected the code
> and this paragraph to match.

## Why

There is no draggable cursor for the catch-all in the UI today, and
exposing a slot for it would invite consumers to bind a cursor visual
to something that has no defined "minimum score" semantics
(GradeAssigner uses the catch-all as a fallback only — its score
isn't a threshold). Returning `Failure(NotDraggable)` from
`MoveCutoff` for the catch-all would also force callers to handle a
runtime-only failure mode for what is really a programmer error
(asking the API to do something it doesn't support).

By excluding the catch-all from `Slots` entirely:

- UI bindings to `Slots` automatically skip the catch-all without a
  filter.
- Calling `MoveCutoff(catchAll, …)` is a programmer error and throws
  `ArgumentException`, surfacing the misuse at the call site rather
  than threading a special failure variant through the type.
- `EnableGrade` / `DisableGrade` are scoped to slot-bearing grades
  only — the catch-all is always implicitly enabled.
- `CutoffMoveFailure` stays at the four PRD-listed variants; no new
  variant for "this isn't a draggable slot."

## Considered alternatives

- **A — slot for the catch-all, new `NotDraggable` failure variant.**
  Adds an enum variant that every caller must handle just to receive
  "you should not have called this" — programmer error masquerading
  as runtime failure.
- **B — slot for the catch-all, `MoveCutoff` returns
  `Failure(GradeNotEnabled)`.** Overloads `GradeNotEnabled` to mean
  two different things (the user disabled it vs. the catch-all is
  not draggable by design), making the failure mode less informative.

Both options also leak the catch-all into UI binding scenarios that
have no place for it.

## Consequences

- `Slots.Count == initial cutoffs count − 1` for the lifetime of the
  session.
- `MoveCutoff(catchAll, …)`, `EnableGrade(catchAll, …)`,
  `DisableGrade(catchAll, …)` all throw `ArgumentException`.
- The catch-all's score is mutated only via batch operations
  (`LoadCutoffs`, `ReseedFromDefaults`) or as a side-effect of a
  reseed inside `EnableGrade`. There is no per-cursor-move path that
  changes it.
- This decision is independent of ADR-0008's structural-immutability
  rule but reinforces it: the slot collection is fixed at
  construction in both length *and* membership.
