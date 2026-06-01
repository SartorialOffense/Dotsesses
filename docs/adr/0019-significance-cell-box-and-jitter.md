# ADR-0019: Significance Matrix cell — box + jittered student points (not mean ± SEM)

## Status

Accepted. Supersedes the per-cell **visual** of ADR-0014 (mean ± SEM dot per
subgroup) and its hover model ("dots are subgroups; tooltip is the interaction").
The test families (ADR-0014/0015) and the effect-size-led annotation (ADR-0018)
are unchanged.

## Context

Each Significance-Matrix cell summarized a subgroup's scores as a **mean ± SEM
dot**. For the tab's actual question — *does subgroup membership shift this score,
and is it worth a closer look?* — SEM is the wrong mark:

- It measures how precisely the **mean** was estimated, not how spread the
  students are; the reader can't see overlap.
- It's the smallest of the usual bars (SD > 95% CI > SEM) and **shrinks as the
  class grows**, so badly-overlapping groups can look cleanly separated — actively
  misleading for a non-statistician.
- It collapses each group to a dot, discarding shape, outliers, and sample size.

## Decision

Render, per subgroup in each cell, a **box plot (median / IQR / whiskers) with
the individual student scores jittered on top**.

- **Show every student.** Class-sized groups make this feasible, and the visible
  point cloud doubles as the per-group sample-size cue.
- **Box** drawn in matplotlib (`ax.boxplot`, fliers off, translucent fill so
  points read through) — it stays in the SVG as non-interactive decoration.
- **Points** drawn via a single `ax.scatter` per cell with **deterministic**
  (fixed-seed) horizontal jitter so they don't jump on resize; extracted and
  removed from the SVG, then re-drawn on the C# Canvas overlay for hover (the
  established pattern). **Only the student scatter may emit SVG `<use>` markers** —
  box/whiskers/median/CI use lines/patches so point extraction stays unambiguous.
- **Per-row y-scale** now spans the actual student-value min/max (was mean ± SEM),
  so boxes, points, and whiskers fit.
- **Small-N / tiny cells:** N=0 omits the subgroup; N=1 shows the lone point (no
  box); N≥2 box + points; and a genuinely narrow cell degrades to **box-only**
  (jitter skipped) rather than forcing an unreadable cloud.

**Points now represent students, not subgroups.** Hovering a point shows that
student's **score + subgroup + subgroup N**. We deliberately **retain ADR-0014's
no-cross-view-sync stance** — hovering does not highlight the student in the
dotplot/violin. The cell's η²/ε² + p stay in the in-cell annotation (ADR-0018),
so per-point hover doesn't need to repeat them.

**Optional mean ± 95% CI overlay**, behind a **session-only toggle** ("Mean ±
95% CI" checkbox, mirroring the Correlation diagonal-toggle precedent — not
persisted, no SavedState bump). **Never SEM.** Drawn with plain lines (no
markers) so it doesn't pollute point extraction; skipped for N < 2.

## Consequences

- `SignificanceDataPoint` becomes **per-student** (`StudentId`, `Value`; `Mean`/
  `Sem` dropped — the box conveys them). `significance_matrix.py`'s
  `point_data_list` is one dict per student; `create_significance_matrix` gains a
  `show_ci` param; `SignificancePlotService.GeneratePlot` gains `showCi` and parses
  the per-student fields. The C# overlay re-draws smaller, denser dots.
- Unchanged: the omnibus test + families, p-values, η²/ε² and the annotation,
  the exploratory caveat, the ADR-0016 default-selection path
  (`compute_significance_pvalues`), and subgroup ordering/coloring (ADR-0017).
- No new Python dependencies (matplotlib `boxplot`/`scatter`/`vlines`; `scipy.stats.t`
  already imported); a local deterministic jitter rather than a cross-module
  beeswarm import, to keep the change self-contained.

## Considered alternatives

- **Keep SEM, or switch SEM→SD/95% CI bars.** Rejected — still collapses each
  group to a bar and hides the distribution the exploratory view needs.
- **Violin per subgroup.** Reasonable, but KDEs mislead at class-sized N and read
  poorly in tiny matrix cells; box + jittered points shows the actual data.
- **Cross-view sync** (hovering a point highlights the student elsewhere).
  Deferred — out of scope for a visual change; ADR-0014's no-sync stance stands.
- **CI always-on.** Rejected — extra ink per small cell; the box already answers
  spread, the annotation answers "do the means differ"; made it an opt-in toggle.
- **Persist the CI toggle.** Rejected — it's a transient view preference; follows
  the session-only Correlation diagonal-toggle precedent.
