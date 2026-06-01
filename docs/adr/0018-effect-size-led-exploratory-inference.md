# ADR-0018: Effect-size-led exploratory inference across both stats tabs

## Status

Accepted

## Context

Two plot tabs evaluate class performance against other variables, and
they currently disagree on how they present inference:

- **Correlation Matrix** (score ↔ *continuous*) shows Pearson r and an
  r² color, but no significance signal — and it correlates each
  aggregate component against a `Total` that *contains* that component,
  inflating r (the classical part–whole problem).
- **Significance Matrix** (score ↔ *categorical*, ADR-0014 / ADR-0015)
  shows raw p-values and tiered stars, but **no effect size** — so at
  class-sized N the headline is a number that mostly reports sample
  size, not whether a relationship *matters*.

For exploratory work the p-value is the wrong headline. The question the
user is actually asking both tabs is the same: *"how much of the
variation in the score does this variable explain?"* That is a
**variance-explained effect size on a 0–1 scale**, and both tabs can
report it — `r²` for continuous pairs, `η²/ε²` for categorical grouping
— giving one comparable number everywhere. This ADR converges the two
tabs onto that shared frame and records the supporting statistical
decisions. It amends ADR-0015's "effect size deferred" note and extends
ADR-0017's Ordinal handling into the correlation path.

## Decision

**1. Effect size is the headline; p + N are support.** Both tabs lead
with a variance-explained effect size on a 0–1 scale (cell color +
tooltip). The p-value and per-cell N move to supporting detail in the
tooltip. The effect sizes are deliberately the same *kind* of quantity
across tabs:

| Tab | Relates | Headline effect size | Support |
|---|---|---|---|
| Correlation | score ↔ continuous | **r²** (or **ρ²** for Spearman) | r/ρ, p, N |
| Significance Matrix | score ↔ categorical | **η²** (parametric) / **ε²** (non-parametric) | p, N |

**2. No multiple-comparison correction — raw p everywhere.** Stars stay
the existing universal tiers (`*` p<.05, `**` p<.01, `***` p<.001),
computed from **raw** p on both tabs, and act as a soft "worth a closer
look" flag — never a gate. We considered and rejected Benjamini–Hochberg
FDR (and Bonferroni): at small class N a correction mostly deletes real
leads and misleads a non-statistician into "nothing matters," which is
the opposite of an exploratory screening view's job. This continues
ADR-0015's raw-p stance and applies it to the correlation tab too.

**3. Show all cells.** No cell is ever hidden for being
non-significant — a high-effect-size / low-p cell is still a lead.
Untestable cells render `—` (mirrors ADR-0015).

**4. Pearson / Spearman split by column type.** Numeric × Numeric uses
`scipy.stats.pearsonr` (r and raw p together). Any cell touching an
**Ordinal** column (ADR-0017) uses `scipy.stats.spearmanr` and reports
ρ / ρ² identically. Spearman's p comes straight from `spearmanr` — not a
reused Pearson t-formula.

**5. Rest-score de-bias driven by an explicit `BiasCorrect` flag.** For
any cell where one axis is `Total` and the other carries the per-column
**`BiasCorrect`** flag ("Bias Correct" in Settings; Numeric, non-Total),
correlate that column `X` against the **rest score** `Total − X` per
student (the corrected item-total correlation from classical test theory)
before computing r, r², and the fitted line.

The trigger is the explicit flag — **not** aggregate membership.
*(Amendment, 2026: this slice originally keyed the correction on
`Aggregate == true && Type == Numeric`. That was too narrow — a
**composite column** like `Q1-Q4 = Q1+Q2+Q3+Q4` overlaps Total and needs
de-biasing, but the user must keep it **out** of the aggregate or its
parts are double-counted. Tying de-bias to `Aggregate` made that
impossible. `BiasCorrect` decouples the two: it **seeds** from the old
rule (`Numeric && Aggregate && !Total`) so existing projects correct the
same cells, then is independently toggleable — a composite gets
`Aggregate` off + `BiasCorrect` on.)* Scope:

- Only `Total × BiasCorrect` cells are corrected; every other cell
  (component-vs-component, `Total` vs an un-flagged column, ordinal, the
  diagonal) is unchanged.
- **Guard:** a single-component Total (or any flagged column equal to
  Total) makes `Total − X ≡ 0` → undefined → the cell is blanked.
- We do **not** validate that a `BiasCorrect` column's value is actually
  contained in Total — `Total − X` trusts the user's assertion.

Correction is rendered **transparently** — no special cell marker. The
"corrected" fact surfaces only in the tooltip (and SPEC), because the
unmarked r is the honest value to read. When an *ordinal* flagged column
feeds Total, de-bias the axis first (`Total − X`), then Spearman the
pair.

**6. η² / ε² estimator choice (Significance Matrix).** The parametric
(Welch ANOVA) path reports **η²** = `SS_between / SS_total`; the
non-parametric (Kruskal–Wallis) path reports **ε²** = `H / (n − 1)`
where `H` is the KW statistic and `n` the total sample. Both are 0–1
"variance explained," both reduce sensibly to the 2-group case, and each
is the natural companion to its existing test family. η² is mildly
upward-biased as a population estimator; we accept that for an
exploratory headline rather than switch to ω²/ε²-on-the-parametric-side,
to keep one estimator per test family and the math legible beside the
hand-rolled Welch F. ε² is *not* interchangeable with η² across
families — hence one named estimator per path.

**7. Exploratory caveat on both tabs.** A short visible note —
"Exploratory — uncorrected; treat as leads, not confirmation" — appears
on both tabs (formalizes the framing ADR-0015 put only in SPEC/tooltips).

**8. Convergence ships together.** The two tabs are changed in one
coordinated effort so they present consistent inference rather than
drifting through an interim where one tab leads with effect size and the
other with p. (Slices remain independently committable; smaller
correlation-first PRs are an acceptable fallback if PR size becomes a
problem — the decisions above don't depend on the sequencing.)

## Consequences

- The correlation C#→Python payload must carry **column type**,
  **is_bias_correct**, and an **explicit Total identity**. This
  retires `correlation_matrix.py`'s fragile "Total is the last series"
  positional assumption — the red Total styling keys off the new flag,
  not position. `BuildSeriesData`, `CorrelationPlotService.GeneratePlot`,
  and the CSnakes call all change. (Coordinate: shared hot spot.)
- A new per-column **`BiasCorrect`** flag joins `Display` / `Aggregate` /
  `Correlation` / `Significance` on `ScoreSelection` (see decision 5), with
  a "Bias Correct" column in the Settings dialog (Numeric non-Total rows
  only). `SavedState.Version` bumps **6 → 7**; `SavedScoreSelection.BiasCorrect`
  is **nullable** so pre-v7 files migrate by deriving the old aggregate-based
  behavior on load (a plain `false` would silently disable de-bias
  everywhere). Seeded from `Numeric && Aggregate && !Total` at fresh load.
- A shared `stats_common.py` extracts `significance_stars(p)` from
  `significance_matrix.py`'s inline `_format_pvalue_annotation`, so both
  tabs star identically (resolves that inline duplication —
  see `TECH_DEBT.md`). **No `statsmodels`** is introduced; decision (2)
  needs no correction library, and `scipy` already supplies everything.
- The correlation tab gains a real per-cell stats tooltip (built from
  scratch — today only hover *detection* exists for cross-view
  highlight): exact **p, N, method (Pearson/Spearman), corrected (y/n)**.
- Per-cell N varies with missing data (different common students). N is
  shown per cell; raw p already encodes its own N.
- `CONTEXT.md` gains the vocabulary (rest score / corrected item-total
  correlation; variance-explained effect size r²/η²/ε²; the
  effect-size-led / raw-p / show-all-cells policy). `SPEC.md` records the
  effect-size headline, stars, tooltip contents, the "corrected" fact,
  and the exploratory caveat on both tabs.
- Supersedes ADR-0015's "effect size deferred" note (alternatives list
  item "Effect size in this slice — deferred"); extends ADR-0017
  (Ordinal) into the correlation/Spearman path.

## Considered alternatives

- **Keep the p-value as the headline.** Rejected — at class N it reports
  sample size, not whether the relationship matters; the whole reframe is
  to lead with effect size.
- **Benjamini–Hochberg FDR / Bonferroni correction.** Rejected — at
  small class N it deletes real leads and tells a non-statistician
  "nothing matters." Can still be layered in later as an opt-in without
  restructuring (as ADR-0015 noted).
- **Mark corrected cells visually.** Rejected — the corrected r is the
  honest value; a marker invites the user to distrust the number they
  should be reading. Tooltip + SPEC disclosure is enough.
- **ω² / less-biased estimators on the parametric side.** Reasonable, but
  η²/ε² keep one estimator per family and read straight off the
  quantities already computed; the upward bias is tolerable for a lead,
  not a confirmation.
- **Pearson everywhere (ignore Ordinal rank).** Rejected — Ordinals carry
  rank, not interval, value (ADR-0017); Spearman is the correct measure
  and ρ² stays comparable to r².
