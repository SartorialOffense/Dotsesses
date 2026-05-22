# Dotsesses Context

Dotsesses is a desktop tool for visualizing a Class's exam scores and
assigning letter Grades against the school's GradeCurve.

## Language

**Course**: An educational subject taught across multiple semesters or
instructors. The shared body of material — e.g. "Civil Procedure".
_Avoid_: Subject, topic.

**Class**: One specific offering of a Course — a particular semester
with particular Students under a particular instructor. The same Course
can have many Classes. *Each loaded Class lives in its own top-level
window with an independent DI scope; opening another file spawns a
fresh window that does not share state with any other (see ADR-0012).*
_Avoid_: Section, cohort, group.

**Student**: A person enrolled in a Class.

**StudentAssessment**: The grading record for one Student in one Class —
their numeric Scores, categorical StudentAttributes, AggregateScore, and
MuppetName.

**Score**: One numeric component of a Student's assessment, e.g.
`Quiz 1 = 17.5`. May carry a free-text Comment.

**StudentAttribute**: A non-numeric piece of Student data, e.g.
`Submitted Outline = "Yes"` or `Mid-Term = "✔✔+"`. Categorical.

**AggregateScore**: The sum of a Student's Scores whose `(Name, Index)`
is in the active ScoreSelection aggregate set. Used for x-axis position
in the dotplot. Cached on StudentAssessment.
_Avoid_: AggregateGrade (legacy code-side name; see `TECH_DEBT.md` TD001).

**Grade**: A letter grade (A, A-, B+, …, F) plus its Order. The thing
assigned to a Student based on where their AggregateScore falls
relative to the GradeCutoffs.

**GradeCutoff**: The numeric threshold for a Grade — "A starts at 285".
An ordered set of GradeCutoffs partitions the AggregateScore axis into
Grade regions.

**GradeCurve**: The school's policy on how many Students should fall
into each Grade. Expressed as a list of CutoffCountRange entries.

**CutoffCountRange**: One row of the GradeCurve — a Grade plus its
target lower/upper bound. Bounds are derived from a percentage of class
size, not absolute counts (see ADR-0006).

**Compliance**: How well the current GradeCutoffs satisfy the
GradeCurve's target ranges. The Compliance panel shows current count vs
target range per Grade.

**ScoreSelection**: A user-toggleable record per Score column with
three booleans — Display, Aggregate, Correlation — that determine
where that Score participates. Persisted on ClassAssessment.

**ClassAssessment**: The post-load dataset for one Class — the
StudentAssessments, the GradeCurve in use, ScoreSelections,
MuppetNameMap, SeriesColorMap, and named SavedCutoffs. The *live*
grading state (current GradeCutoffs, current CutoffCounts, EnabledGrades)
is not on ClassAssessment — it lives on the paired **GradingSession**.

**GradingState**: An immutable snapshot of a Class's grading at one
moment — the current GradeCutoffs, the per-Grade CutoffCounts derived
from them, and the set of EnabledGrades. Carried as the `State` member
of every `GradingStateChange` notification.

**GradingSession**: The live, observable counterpart to a GradingState
snapshot. Owns the current GradingState for one Class, validates
mutations (cursor moves, Grade enable/disable, reseed), and broadcasts
each accepted change via INPC with the new GradingState and the
originator object embedded in the payload. There is one GradingSession
per loaded Class.

A GradingSession is **immutable in shape**: the set of Grades it
manages — and therefore the set of CutoffSlots it exposes — is fixed
at construction time from the loaded ClassAssessment. Mutations
within that lifetime are limited to per-Slot Score, per-Grade
EnabledGrades membership, and the derived CutoffCounts. Any
structural change (different Grade set, different GradeCurve) means
constructing a fresh GradingSession and rebuilding the dependent
view models — never in-place restructuring.

**CutoffSlot**: The UI-binding adapter for one GradeCutoff inside a
GradingSession — a stable per-Grade observable handle exposing
`Score` and `IsEnabled`. Slots are owned by the GradingSession; the
session updates a slot's properties in place when a mutation is
accepted. The dotplot and violin plot bind their cursor visuals to
the slot collection so item identity is preserved across moves (no
ItemsControl rebuild churn). Slots are read-only from the binding
side — mutations go through the GradingSession's API.

The **structural catch-all** Grade — the lowest-Order Grade in the
session's `DefaultCurve`, regardless of whether that Grade has a
non-zero `CutoffCountRange` — has *no* CutoffSlot. It is always
implicitly enabled, never draggable, and acts purely as a fallback
for `GradeAssigner` (see ADR-0011). Calls to `MoveCutoff`,
`EnableGrade`, or `DisableGrade` against the catch-all throw
`ArgumentException`. Every other Grade in `DefaultCurve` gets a
Slot, including those that started with a zero target range — they
sit in a fallback band below the data envelope and are draggable
into the data range whenever the user wants to use them.

**GradeBin**: The vertical stack of dots in the dotplot at one
AggregateScore value. Students sharing an AggregateScore stack into the
same GradeBin.

**Drill-down**: The left-side panel that shows the currently-hovered
Student's full record — Scores with Comments, StudentAttributes, and
assigned Grade.

**Region Band**: The alternating shaded backgrounds in the dotplot, one
per enabled Grade, visualizing where each Grade region sits on the
AggregateScore axis.

**MuppetName**: A whimsical per-Student display identifier — a Muppet
character name plus 1–3 emojis. Used in lieu of a numeric Student Id in
the UI. *(See TD002 — likely retired when multi-Class and statistics
features land.)*
_Avoid_: real name, alias.

## Relationships

- A **Course** can have many **Classes** (different semesters or
  instructors).
- A **Class** has one **ClassAssessment** at a time (one grading session).
- A **ClassAssessment** has many **StudentAssessments**, one per
  **Student**.
- A **StudentAssessment** has many **Scores** and many
  **StudentAttributes**.
- A **ClassAssessment** has one current set of **GradeCutoffs** and one
  **GradeCurve**.
- A **Compliance** row exists per **Grade** in the active **GradeCurve**.
- A **ScoreSelection** exists per distinct `(Name, Index)`
  **Score** column in the **ClassAssessment**.

## Test data shape

Synthetic data assumes a tri-modal AggregateScore distribution (5% high
/ 75% middle / 20% low). StudentAttributes are 60% correlated with the
performance group and 40% rolled independently. Plots and Compliance
are designed to read this shape well.

## Flagged ambiguities

- **"Class" the educational entity vs C# `class`**: when ambiguous,
  qualify with "the Class" or "a C# class".
- **"AggregateGrade" in code = AggregateScore in domain**. The code-side
  rename is queued as TD001.
- **"Curve" in the UI = GradeCurve formally**.
- **A StudentAssessment is the grading record, not the Student**. There
  is no separate Student entity in code today; Student identity is the
  `Id` field on StudentAssessment.

## Example dialogue

> **Dev:** "When the user toggles a Score's Aggregate flag in Settings
> and presses Apply, what happens to the GradeCutoffs?"
>
> **Designer:** "If the **AggregateScore** composition actually changed
> — i.e. the set of Scores summed into AggregateScore is different —
> we re-seed the **GradeCutoffs** from the **GradeCurve**, because the
> old cutoff positions may now sit outside the new score range. If only
> Display or Correlation flags changed, GradeCutoffs stay put. See
> ADR-0005."
