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

### TD003 — Initial cursors / Compliance grid duplicate on second load

**Why it's debt:**
`MainWindowViewModel.InitializeCursors()` and
`InitializeComplianceGrid()` append to their `ObservableCollection`s
without clearing first. Calling `LoadFromExcelFile` then
`LoadStateAsync` on the same ViewModel duplicates the cursor and
Compliance rows. Production user flow (one fresh-app + one load)
doesn't hit this; tests work around it via the parameterless
`CreateForTesting()` overload.

**Trigger:** Multi-Class handling will exercise repeated load paths
on the same ViewModel and surface this immediately. Fix as part of
that work.

**Touches:** `MainWindowViewModel.cs` (the two `Initialize*` methods
and their callers).
