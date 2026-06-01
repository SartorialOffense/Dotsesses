# Significance Matrix: SEM → box + jittered points — 2026-05-31

Design context behind replacing the Significance-Matrix per-cell visual (mean ±
SEM dot) with a box plot + jittered individual student points. Formal record:
**ADR-0019**. Source handoff (gitignored):
`.conversations/significance-visual-sem-to-box-handoff.md`.

## (a) Why SEM was the wrong mark

The tab is exploratory — "which categorical variables are associated with score
differences, worth a closer look?" SEM answers the wrong question: it measures
how precisely the *mean* was estimated, not how spread the students are; it's the
smallest of the usual bars (SD > 95% CI > SEM) and **shrinks as the class grows**,
so overlapping groups can look cleanly separated — misleading for a
non-statistician; and it collapses each group to a dot, hiding shape, outliers,
and sample size. The owner asked to **show the data**.

## (b) What landed

Per subgroup, per cell: a **box** (median + IQR, translucent fill; whiskers
dropped — every point is shown, so the cloud conveys the range) with the
**individual student scores jittered on top**. Acceptance criteria from the
handoff — show every student (the cloud is also the N cue), show spread/overlap,
stay readable in small cells, fit the app's look — drove the choices.

## (c) Owner-confirmed decisions

- **Hover = per-student, no cross-view sync.** Points now represent students; the
  tooltip shows the student's score + subgroup + subgroup N. ADR-0014's
  no-cross-view-sync stance is retained. Cell stats (η²/ε² + p) stay in the
  existing in-cell annotation, so the tooltip needn't repeat them.
- **Mean ± 95% CI behind a toggle** ("Mean ± 95% CI" checkbox), **never SEM**,
  off by default, **session-only** (not persisted — mirrors the Correlation
  diagonal-toggle precedent, no SavedState bump).

## (d) How it's built (the load-bearing details)

- The tab already drew scatter dots, stripped their SVG `<use>` elements, and
  shipped point coords to C# to re-draw on a hit-testable Canvas overlay. Reused:
  the **box stays in the SVG** (decoration); the **student points** go through that
  extract-and-redraw path. **Only the student scatter may emit `<use>` markers** —
  the box/median and the CI overlay use lines/patches (CI via `vlines`/`hlines`,
  no markers) so point extraction stays unambiguous.
- **Jitter is deterministic** (fixed-seed `RandomState`) so points don't jump on
  resize. A local jitter, not a cross-module beeswarm import, to keep the change
  self-contained.
- **Per-row y-scale** now spans the actual student-value min/max (was mean ± SEM).
- Small-N: N=0 omit; N=1 lone point (no box); N≥2 box + points; narrow cells
  degrade to **box-only**.
- `point_data_list` and `SignificanceDataPoint` became **per-student** (`StudentId`,
  `Value`; `Mean`/`Sem` dropped). `create_significance_matrix` + the service gained
  a `show_ci` / `showCi` param.

## (e) Untouched

The omnibus test + families (ADR-0014/0015), p-values, η²/ε² and the
effect-size-led annotation (ADR-0018), the exploratory caveat, subgroup
ordering/coloring (ADR-0017), and the ADR-0016 default-selection path. This was a
visual change only.

## (f) Verification

355 tests green. CSnakes integration pins: one point per student (count =
students, not subgroups), each carrying its own score + subgroup + N; the box
doesn't corrupt extraction; the CI toggle renders either way; cell-level
η²/ε² + p still ride every point. Manual smoke (owner): box + jitter render, the
CI checkbox toggles, hover shows the student's score, tiny cells degrade to
box-only, resize keeps points stable.
