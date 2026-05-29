# Significance Matrix inferential layer (Welch ANOVA / Kruskal–Wallis)

ADR-0014 shipped the descriptive Significance Matrix (mean ± SEM dots per
Subgroup) and explicitly deferred the **inferential** layer — a per-cell
statistical test — leaving the test choice open ("Welch's ANOVA or
Kruskal–Wallis, TBD; … decision deferred to that slice"). This ADR makes
that decision.

Each cell now runs **one omnibus test** over its Subgroups and annotates
the cell with the resulting p-value and tiered significance stars
(`*` p<.05, `**` p<.01, `***` p<.001). A top-of-plot radio switches the
whole matrix between two **Test Families**:

- **Parametric — Welch's ANOVA** (default). Unequal-variance-safe;
  reduces exactly to Welch's two-sided t-test for 2 groups (F = t²).
- **Non-parametric — Kruskal–Wallis**. Rank-based; reduces to the
  Mann–Whitney U test for 2 groups.

## Why an omnibus test instead of branching on group count

The user's framing ("some categories have 2 values, others have more, I
assume there are multiple ways to do this") suggests a per-cell branch on
the number of Subgroups. There is no need: an omnibus test answers the
matrix's own question — *"does subgroup membership shift this mean,
anywhere?"* — for any group count ≥ 2, and each family reduces to its
two-group special case automatically. One code path covers a 2-value
`Hat` column and a 4-value `Section` column identically.

## Why Welch / Kruskal rather than classic ANOVA / t-test

Grade subgroups are routinely unequal in size and variance and often
non-normal. Classic one-way ANOVA assumes equal variances; Welch's does
not, and is the modern default for real-world group comparisons.
Kruskal–Wallis is the rank-based companion for users who distrust the
parametric assumptions. Offering both as a radio honors the user's
"switch between reasonable metrics" request while keeping the choice to a
single, meaningful axis.

scipy has **no built-in Welch ANOVA** (`scipy.stats.f_oneway` assumes
equal variance), so the Welch F is computed by hand from the closed form
(Welch 1951). This avoids adding a `pingouin`/`statsmodels` dependency for
~15 lines of arithmetic; `scipy` (already a declared dependency) supplies
`f.sf` for the p-value and `kruskal` for the non-parametric side. A
verification check confirms the 2-group Welch ANOVA p equals
`ttest_ind(equal_var=False)`.

## Raw p-values — exploratory, not corrected

A populated matrix runs many simultaneous tests, so ~1-in-20 cells will
read "significant" by chance at α=.05. We deliberately show **raw,
uncorrected** p-values and frame the matrix as an *exploratory screening*
view (documented in SPEC and surfaced in tooltips), rather than applying
FDR/Bonferroni. This matches how tools like GraphPad Prism present a grid
of comparisons and keeps the stars stable as columns are added/removed.
Correction can be layered in later as an opt-in without restructuring.

## Marker design

The p-value + stars print in each cell's top-right corner. Significant
cells render bold in a strong (theme-aware) color; non-significant cells
print their p faint/grey with no star; untestable cells show an em-dash
(`—`). Every cell carries a value (no blank cells), which removes the
"did the test run, or was it just not significant?" ambiguity while the
bold/faint contrast still draws the eye to real hits. Tiers are the fixed
universal convention (.05/.01/.001) — no adjustable α.

## Small-N policy

Welch's ANOVA needs within-group variance (each group N ≥ 2) and ≥ 2
groups; Kruskal needs ≥ 2 groups. Subgroups with N < 2 are **dropped from
the test** but their dots still render (they remain descriptively real);
the dropped dot's tooltip says so. If fewer than 2 valid groups remain
(or a parametric group has zero variance), the cell is untestable and
shows `—`.

## Persistence: SavedState v4 → v5

The chosen Test Family persists with the workspace. `SavedState.Version`
bumps 4 → 5; the new `SignificanceTestFamily` field defaults to
`Parametric`, so v4 files silently migrate (the ADR-0002
first-save-rewrites pattern, as with the v3 → v4 `Significance` flag).
This is the third version bump in this feature area and was chosen over a
session-only toggle (the precedent set by the Correlation diagonal toggle)
because the test family is an analytic decision the user will want to
survive a reload.

## Data path

`SignificanceDataPoint` gains the `PValue` (nullable; null when
untestable), `TestFamily`, and `Excluded` fields ADR-0014 reserved. The
Python point dict gains `p_value` (NaN ⇒ untestable), `test_family`, and
`excluded`. The cell-level p is repeated on every dot in the cell (dots
are the only things C# hit-tests), which the tooltip then surfaces.

## Considered alternatives

- **Branch per cell on group count** (t-test for 2, ANOVA for 3+).
  Rejected — the omnibus reduction makes this redundant complexity.
- **Classic equal-variance ANOVA.** Rejected — fragile for uneven grade
  subgroups.
- **FDR/Bonferroni correction now.** Deferred — adds a concept for a
  non-statistician user and makes stars shift as the matrix resizes;
  exploratory framing is honest and simpler.
- **Adjustable α.** Rejected — the tiered stars already encode the
  conventional cutoffs; an α control is extra UI/state for little gain.
- **Effect size in this slice.** Deferred to a future revision — keeps
  cells uncluttered; η²/ε² is the natural next enhancement.
- **Session-only toggle** (no persistence). Rejected — the family is a
  decision worth saving.

## Consequences

- `SavedState` v4 → v5; old files silent-migrate to Parametric.
- The Significance tab gains a top-right test-family radio (mirrors the
  Correlation tab's top-right control pattern).
- `scipy` is now exercised at runtime (previously only declared); the
  Welch F closed form lives in `significance_matrix.py`.
- A future slice will add an effect-size measure (η²/ε²) — see SPEC.
