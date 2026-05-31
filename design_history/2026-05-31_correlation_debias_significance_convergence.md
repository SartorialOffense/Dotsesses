# Correlation De-bias + Effect-Size-Led Significance — 2026-05-31

Design context behind converging the two exploratory-stats tabs
(**Correlation Matrix** and **Significance Matrix**) onto one coherent
"variance explained" frame. Formal decision record: **ADR-0018**. This
file keeps the *conversation* context — the framing, the rejected paths,
and the slice plan — that the ADR compresses.

Source handoff: `.conversations/correlation-debias-significance-handoff.md`
(gitignored). Status at time of writing: decisions locked, no code yet,
branch base `main` @ `9f324e3`.

---

## (a) The reframe that drives everything

The owner wants **exploratory statistics for evaluating class performance
against categorical and continuous variables**. The two tabs disagreed on
how they present inference:

- **Correlation** (score ↔ continuous): Pearson r + r² color. No
  significance signal, and it correlates each aggregate component against
  a `Total` that *contains* it (part–whole inflation).
- **Significance Matrix** (score ↔ categorical, ADR-0014/0015): raw p +
  stars, but **no effect size**.

Key insight: **for exploratory work the p-value is the wrong headline.**
At class-sized N it mostly reports sample size, not whether a relationship
*matters*. The headline should be the **effect size** — "what fraction of
the variation in the score does this variable explain" — on a 0–1 scale.
Both tabs can report the same *kind* of number:

| Tab | Relates | Headline | Support |
|---|---|---|---|
| Correlation | score ↔ continuous | **r²** (or **ρ²** Spearman) | r/ρ, p, N |
| Significance Matrix | score ↔ categorical | **η²** / **ε²** | p, N |

`r²` and `η²/ε²` are the same "% variance explained" idea, so the two tabs
become one toolkit.

## (b) Locked decisions (owner-confirmed)

1. **Effect-size-led** — both tabs lead with variance-explained; p + N are
   supporting detail in the tooltip, not the headline.
2. **No multiple-comparison correction** — raw p everywhere. BH-FDR was
   considered and **dropped**: at small class N it mostly deletes real
   leads and tells a non-statistician "nothing matters." Continues
   ADR-0015's raw-p stance.
3. **Show all cells** — never hide a cell for being non-significant; a
   low-p / high-effect-size cell is still a lead. `—` when untestable.
4. **Stars identical on both tabs** — `*` .05 / `**` .01 / `***` .001,
   faint when p≥.05, from **raw** p. Soft "look closer" flag, not a gate.
5. **Exploratory caveat** visible on both tabs ("uncorrected; treat as
   leads, not confirmation").
6. **Pearson / Spearman split** — Pearson for continuous×continuous;
   Spearman for any cell touching an **Ordinal** column (ADR-0017),
   reporting ρ / ρ².
7. **De-bias = corrected item-total / rest score** — correlate component
   `X` against `Total − X` per student.
8. **De-bias rendered transparently** — no cell marker; "corrected"
   surfaces only in tooltip + SPEC.
9. **Converge both tabs together** so they ship consistent. (Owner was
   split between this and "correlation first, backfill"; default taken is
   *together*, smaller-PR fallback acceptable since the decisions don't
   depend on sequencing.)

## (c) The part–whole problem (de-bias rationale)

`Total` = sum of selected component columns
(`StudentAssessment.RecalculateAggregate()`). Correlating a component `X`
against a `Total` that contains `X` correlates `X` partly with itself,
**inflating** r. The classical-test-theory fix is the **corrected
item-total correlation**: correlate `X` against the **rest score**
`Total − X`.

**Scope — narrow on purpose:**

- Correct **only** `Total × aggregate-component` cells
  (`Aggregate == true && Type == Numeric`).
- Component-vs-component: unchanged.
- `Total` vs a non-aggregate column (Ordinal / displayed-but-excluded):
  unchanged — no bias there.
- Diagonal: unchanged.
- **Guard:** single-component Total → `Total − X ≡ 0` → undefined → blank
  the cell.

When corrected, **all three outputs** use the corrected value: the `r=…`
text, the r² color, and the fitted line. When an *ordinal* component feeds
Total: de-bias first (`Total − X`), then Spearman.

## (d) Effect-size math added

- **Correlation:** r² already computed/shown; Spearman cells report ρ²
  identically. Make sure the corrected (rest-score) and Spearman paths
  produce matching r²/ρ².
- **Significance Matrix (new):**
  - Parametric (Welch ANOVA): **η²** = `SS_between / SS_total`.
  - Non-parametric (Kruskal–Wallis): **ε²** = `H / (n − 1)`.
  - Both 0–1, both reduce to the 2-group case, same prominence as r²
    (color + tooltip). η²/ε² are **not** interchangeable across families —
    one named estimator per path. η²'s mild upward bias accepted for an
    exploratory lead (ω²/ε²-on-parametric considered, dropped to keep the
    math legible beside the hand-rolled Welch F).

## (e) Implementation slices (TDD per project norm)

Independently committable. Shared hot spots: `CorrelationPlotService` /
`BuildSeriesData` — coordinate with anyone editing them.

- **Slice 0 — Shared star/format helper.** New
  `Dotsesses/Python/Violin/stats_common.py` with `significance_stars(p)`,
  extracted from `significance_matrix.py`'s inline
  `_format_pvalue_annotation`. No `statsmodels`, no `fdr_adjust`. Existing
  tests stay green (parity with old inline output).
- **Slice 1 — Carry column metadata across the C#→Python boundary.**
  Payload carries column **type**, **isAggregateComponent**, and an
  **explicit Total identity** (retire "Total is the last series"; red
  styling keys off the flag, not position). Plumbing only.
- **Slice 2 — Rest-score de-bias.** `Total × component` cells use
  `Total − X`. Guard the single-component degenerate case.
- **Slice 3 — Significance + N + Spearman.** `pearsonr` for
  Numeric×Numeric, `spearmanr` for Ordinal-touching (ρ p from
  `spearmanr`, **not** a Pearson t-formula). Stars from raw p. Tooltip:
  exact **p, N, method, corrected (y/n)**.
- **Slice 4 — Effect size + caveat on Significance Matrix.** η²/ε² as the
  headline (color + tooltip), p demoted. Exploratory caveat on both tabs.

## (f) Gotchas (record for the implementer)

- **Per-cell N varies** (missing data → different common students). Show N
  per cell; raw p already encodes its own N.
- **Single-component Total** → rest score ≡ 0 → undefined. Guard it.
- **Total positional assumption** is load-bearing today (red color) — keep
  red keyed off the new flag after Slice 1.
- **Spearman p** straight from `spearmanr`.
- **No `statsmodels`** — raw-p needs no correction lib; `scipy` already
  supplies everything (incl. the hand-rolled Welch F from ADR-0015).

## (g) Docs touched as work lands

- **ADR-0018** (this design's formal record) — written ahead of code.
  Supersedes ADR-0015's "effect size deferred" note; extends ADR-0017
  (Ordinal) into the correlation/Spearman path.
- `CONTEXT.md` — new vocabulary (rest score / corrected item-total
  correlation; variance-explained effect size r²/η²/ε²; the
  effect-size-led / raw-p / show-all-cells policy).
- `SPEC.md` — effect-size headline, stars, tooltip contents, the
  "corrected" fact (cells are unmarked so it lives here), exploratory
  caveat — both tabs.
- `TECH_DEBT.md` — inline-stars duplication resolved by Slice 0; any
  deferred slice noted.
