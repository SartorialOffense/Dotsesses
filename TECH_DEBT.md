# Tech Debt

A flat numbered list of known debt items. Append-only — supersede or
strike through rather than renumber. Each item carries enough context
that a future agent (human or otherwise) can decide whether it's still
worth doing.

## Format

```
### TDNNN — Short title

**Why it's debt:** the constraint, surprise, or dropped-ball.
**Trigger:** when this becomes worth paying down.
**Touches:** rough scope of the change.
```

---

### TD001 — Rename `AggregateGrade` to `AggregateScore`

**Why it's debt:** The C# field is named `StudentAssessment.AggregateGrade`,
but in domain language (see `CONTEXT.md`) the value is an
**AggregateScore** — a numeric total, not a letter Grade. The misnomer
quietly trains every new reader to conflate Grade-the-letter with
the numeric input that *determines* a Grade.

**Trigger:** Any larger refactor in `StudentAssessment` or its
consumers; cheap to bundle.

**Touches:** `Models/StudentAssessment.cs`, all callers, possibly
`SavedState` field naming (with v-bump per ADR-0002 if the JSON key
is changing).

---

### TD002 — Retire MuppetName and emoji decoration

**Why it's debt:** MuppetName + emojis was charming with one Class and
no statistics. With multi-Class handling and statistical tests on the
roadmap, the whimsy stops paying for itself: the legend becomes
crowded, statistical output reads silly with Muppet labels, and
cross-Class comparison wants stable, professional identifiers.

**Trigger:** Multi-Class feature work, or the first statistics output
that needs a Student label.

**Touches:** `Services/MuppetNameGenerator.cs`, `Services/MuppetNames.cs`,
`Models/MuppetNameInfo.cs`, `ClassAssessment.MuppetNameMap`, every UI
binding that displays MuppetName, and the generation logic in
`SyntheticStudentGenerator`.

---

### TD003 — Initial cursors / Compliance grid duplicate on second load *(closed)*

**Resolved by issue #8 / slice 2 of #6 (2026-05-07).** The freshly
constructed `GradingSession` now provides the single seam from "no
grading state" to "valid grading state" on every load (Excel and
saved state), so the duplicate-append path no longer affects the
canonical state. The legacy `_cursors` mirror collection was removed
by issue #14; `InitializeComplianceGrid` remains until issue #10
extracts `ComplianceGridViewModel`.

---

### TD004 — `CursorPlacementCalculator.ResetToEvenSpacing` produces inverted Score-vs-Order ordering

**Why it's debt:** The private fallback used when `PlaceNewCursor`
detects an overlap sorts grades by `Order` ascending and then assigns
`minScore + spacing * (i + 1)` left-to-right. Because A has the
lowest `Order` (0) and F the highest (10), this gives A the *smallest*
score and F the *largest* — the opposite of the intended grade →
score relationship. `GradeAssigner.ValidateCutoffOrdering` would
throw on the result, so any caller that triggers the reseed path is
broken. The existing
`CursorPlacementCalculatorTests.PlaceNewCursor_CausesOverlap_ResetsAllToEvenSpacing`
only checks that spacing is roughly even and so does not catch this.

The public `ResetToEvenSpacingMonotonic` overload (used by
`SeedCursorsFromDefaults` per M002/S05) gets the orientation right —
it inverts the fraction so best grade lands at `maxScore`. The
private fallback should adopt the same orientation.

**Trigger:** Slice #3 of issue #6 (cursor drag migration). The
GradingSession's `EnableGrade` exercises this path; the related
"EnableGrade triggers reseed" test in `GradingSessionTests` is
deferred until this is fixed.

**Touches:** `Calculators/CursorPlacementCalculator.cs` (the private
`ResetToEvenSpacing`), and the existing test should be tightened to
assert orientation, not just even spacing.

---

### TD005 — Consolidate test fixture files into a dedicated folder

**Why it's debt:** Test fixtures (`IP exam scores 2025.xlsx`, the
v2 example `.dots`, etc.) live under `Dotsesses/example/`, which is
shared with documentation/demo material and lives inside the main
project, not the test project. Tests reach into the production
project's directory via a `ResolveRepoFile` helper and a hard-coded
relative path. A dedicated test data folder (e.g.
`Dotsesses.Tests/TestData/` or `tests/fixtures/`) would make the
boundary explicit and let `Dotsesses/example/` be purely
user-facing demo material.

**Trigger:** Any time a test fixture needs adding or moving;
include in the same change to amortise the move.

**Touches:** physical move of fixture files; update the
`ResolveRepoFile` callers in `MainWindowViewModelTests` and
`StateServiceTests` to point at the new path.

---

### TD006 — Compliance panel exposes no per-Grade enable/disable affordance

**Why it's debt:** `CutoffSlot.IsEnabled` and the
`GradingSession.EnableGrade` / `DisableGrade` mutators support
toggling individual Grades on and off, but the live UI has no
discoverable control to drive them — no checkbox or toggle in the
Compliance grid rows, no context menu on cursor visuals.
Surfaced during manual smoke-testing of slice 2 (issue #8): the
domain model says "user can disable a Grade" and the persistence
layer round-trips that state, but a user actually trying it has
nowhere to click.

**Trigger:** Slice #5 of issue #6 (extract
`ComplianceGridViewModel`) is the natural place to add the toggle
control, since that's when Compliance rows become the canonical
driver for `EnableGrade` / `DisableGrade`. If slice #5 lands without
addressing this, open a dedicated issue.

**Touches:** likely `Dotsesses/UI/MainWindow.axaml` (Compliance
section) and `ComplianceRowViewModel` (already has `IsEnabled`
shape).

---

### TD007 — Cursor drag accepts scores below the AggregateScore envelope *(closed)*

**Resolved by issue #9 / slice 3 of #6 (2026-05-07).** Both drag
handlers now commit through `GradingSession.MoveCutoff`, which
enforces `[min−1, max+5]` (issue #9 / Q3, asymmetric per
maintainer preference). Rejected moves are silently dropped, so
the cursor visually parks at its last valid position when the
mouse pushes past the boundary.
