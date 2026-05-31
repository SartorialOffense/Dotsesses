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
by issue #14; `InitializeComplianceGrid` was deleted by issue #10
(slice 4 of #6) — the new `ComplianceGridViewModel` builds rows
once at construction and tracks state via its own
`GradingSession.PropertyChanged` subscription, so there is no
append-on-reload path to duplicate.

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

### TD005 — Consolidate test fixture files into a dedicated folder *(closed)*

**Resolved 2026-05-08.** `IP exam scores 2025.xlsx` (the only fixture
the test suite actually consumed) moved to
`Dotsesses.Tests/TestData/`. The `ResolveRepoFile` helper now lives
on `Dotsesses.Tests.Fixtures.TestFixtures` as a private detail behind
the public `IpExamScoresXlsx()` accessor — no more duplicated walk-up
logic in test files. The dead `ResolveRepoFile` in `StateServiceTests`
was deleted. `Dotsesses/example/` retains the demo `.xlsx`, `.dots`,
and `.pptx` outputs that ship as user-facing example material.

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

**Trigger:** The toggle wiring is now in place — issue #10 routed
`ComplianceRowViewModel.IsEnabled` writes through `ComplianceGridViewModel`
into `GradingSession.EnableGrade` / `DisableGrade`, so adding a
checkbox in the AXAML row template is a single-binding change. Open a
dedicated issue when prioritised.

**Touches:** `Dotsesses/UI/MainWindow.axaml` (Compliance row template)
— add a `CheckBox IsChecked="{Binding IsEnabled}"`. The catch-all row
should not show the toggle (its state is structurally fixed).

---

### TD007 — Cursor drag accepts scores below the AggregateScore envelope *(closed)*

**Resolved by issue #9 / slice 3 of #6 (2026-05-07).** Both drag
handlers now commit through `GradingSession.MoveCutoff`, which
enforces `[min−1, max+5]` (issue #9 / Q3, asymmetric per
maintainer preference). Rejected moves are silently dropped, so
the cursor visually parks at its last valid position when the
mouse pushes past the boundary.

---

### TD008 — Significance-star tier thresholds were inline in one module *(closed)*

**Resolved by ADR-0018 slice 0 (2026-05-31).** The `.05/.01/.001`
star tiers lived inline in `significance_matrix.py`'s
`_format_pvalue_annotation`. ADR-0018 makes both stats tabs star
identically from raw p, so the thresholds moved to a single
`significance_stars(p)` helper in `Dotsesses/Python/Violin/stats_common.py`
before the Correlation tab grew its own copy. `_format_pvalue_annotation`
now delegates to it; the correlation path (slice 3) will reuse the same
helper. Exercised through C# via CSnakes (`StatsCommon().SignificanceStars`)
in `SignificanceStarsTests`.

---

### TD009 — Instructor significance guide predates the effect-size headline

**Why it's debt:** `docs/significance-guide.md` (linked from `SPEC.md`)
explains the Significance Matrix to instructors in terms of the p-value
and the two test families. ADR-0018 made the **effect size** (η²/ε²,
"variance explained") the headline and demoted raw p to support, and
added the same effect-size frame (r²) to the Correlation tab. The guide
no longer matches what the UI leads with, so a non-statistician reading
it will over-index on p.

**Trigger:** next time the guide is touched, or when an instructor asks
what the η²/ε² number means. Cheap to fold in.

**Touches:** `docs/significance-guide.md` — add an effect-size section
(what η²/ε²/r² mean, the 0–1 "variance explained" reading, why it leads
over p at class N); cross-reference ADR-0018.
