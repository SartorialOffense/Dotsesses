# Understanding the Significance Matrix

*A plain-language guide for instructors — no statistics background assumed.*

The **Significance Matrix** helps you ask a simple question about your
class: *does some group of students tend to score differently from
another?* For example — do students who submitted an outline score
higher on the final than those who didn't? Do the three lab sections
end up with different averages?

Each **cell** of the matrix lines up one numeric column (say, *Final
Exam*) against one categorical column (say, *Submitted Outline:
Yes/No*). The dots show each subgroup's average score, and the little
number and stars in the corner tell you whether the difference between
those subgroups is large enough to take seriously.

This page explains what that number means, the two ways the app can
calculate it, when to prefer each, and how to cite the method in a
paper.

---

## What the p-value means

Every tested cell shows a **p-value** — a number between 0 and 1.

Here is the one-sentence version:

> The p-value answers: *if subgroup membership made no real difference
> to the average, how often would you see a gap at least this big just
> from the luck of who happened to land in each group?*

So a **small p-value means "this gap would be surprising by chance
alone"** — which is evidence that the difference is real and not just
noise. A **large p-value means "a gap this size happens all the time by
chance"** — so you have no real evidence of a difference.

By long-standing convention the app marks results with stars:

| Stars | p-value | Reading |
|-------|---------|---------|
| `***` | below 0.001 | Very strong evidence of a difference |
| `**`  | below 0.01  | Strong evidence |
| `*`   | below 0.05  | Moderate evidence |
| *(no star)* | 0.05 or above | No real evidence of a difference |

Significant cells are shown **bold**; non-significant cells are faint.
A cell that shows a dash (`—`) couldn't be tested — usually because a
subgroup had fewer than two students.

### Three things a p-value does *not* tell you

These trip up even experienced researchers, so they're worth stating
plainly:

1. **It is not the probability that there's no difference.** A p-value
   of 0.03 does *not* mean "there's a 3% chance the groups are the
   same." It's a statement about the data, not about the truth.
2. **It does not tell you how big or important the difference is.** A
   tiny, unimportant gap can earn three stars in a large class, and a
   large, interesting gap can go unstarred in a small one. Always look
   at the actual subgroup averages (the dots), not just the stars.
3. **It does not establish *why*.** A difference between subgroups may
   be caused by something else entirely (who chooses to submit outlines
   may differ in many ways). The matrix flags associations; it does not
   prove cause.

---

## The two ways the app calculates it

A toggle at the top of the plot lets you switch the whole matrix
between two well-established methods. Both answer the same question —
"is there a real difference between these subgroups?" — but they go
about it differently. You do **not** need to do anything different for
groups with two values (Yes/No) versus several (Section A/B/C/D); each
method handles both automatically.

### 1. Parametric — Welch's ANOVA (the default)

This method compares the **averages** of the subgroups directly. The
"Welch" part means it does *not* assume the subgroups are the same size
or equally spread out — which is important, because real class
subgroups rarely are. When a categorical column has just two values
(e.g. Yes/No), Welch's ANOVA is exactly the familiar **Welch's
t-test**.

Think of it as: *"Are the subgroup averages farther apart than the
scatter within each subgroup can comfortably explain?"*

### 2. Non-parametric — Kruskal–Wallis

This method ignores the exact scores and instead compares **ranks** —
it lines every student up from lowest to highest and asks whether one
subgroup tends to sit higher in the order. Because it works on rank
order rather than raw values, it is unfazed by skewed distributions or
a few extreme outliers. For a two-value column it is exactly the
**Mann–Whitney test**.

Think of it as: *"Does one subgroup tend to out-rank the other?"*

---

## Which one should I use?

A reasonable default and a few rules of thumb:

- **Start with Welch's ANOVA (parametric).** Exam and assignment scores
  are usually well-enough behaved for it, and "difference in averages"
  is the most natural thing to report.

- **Switch to Kruskal–Wallis (non-parametric) when:**
  - the scores are clearly **skewed** (e.g. a pile-up at the top or a
    long tail of low scores),
  - there are a **few extreme outliers** you don't want to dominate the
    result,
  - the subgroups are **small**, or
  - the column is really an **ordinal rating** (e.g. ✓ / ✓+ / ✓++)
    rather than a true measurement.

- **A useful cross-check:** run it both ways. If both methods agree,
  your conclusion is robust. If they disagree, that's usually a sign of
  skew or outliers — lean on the non-parametric result, and look at the
  data before drawing a conclusion.

### At a glance

|  | Welch's ANOVA (parametric) | Kruskal–Wallis (non-parametric) |
|---|---|---|
| **Best when** | scores are roughly symmetric and you care about averages | scores are skewed, have outliers, are ordinal ratings (✓ / ✓+ / ✓++), or subgroups are very small |
| **Compares** | subgroup *averages* | subgroup *rankings* |
| **Sensitivity** | higher — finds a real difference with fewer students | a bit lower — may miss small real differences |
| **Reads as** | "averages differ by about X" (intuitive) | "one subgroup tends to score higher" (less intuitive) |
| **Main risk** | a few extreme scores can distort the average | can miss real differences; a difference in *spread* can masquerade as a difference in *level* |

A common question is *"why not just always use the non-parametric one,
to be safe?"* Because it isn't free: when the scores are well-behaved
it's less sensitive (the "Sensitivity" row above), so you're more likely
to miss a real difference — and "non-parametric" does not mean
"assumption-free," since reading a Kruskal–Wallis result as *one group
scored higher* still assumes the subgroups have similar shapes. Match
the test to the data rather than defaulting to one for safety.

---

## Important caveats before you put this in a paper

The matrix is a **screening tool** — it's designed to help you *spot*
associations worth a closer look, not to serve as a finished
confirmatory analysis. Two points matter most:

- **The p-values are not corrected for multiple comparisons.** A full
  matrix runs many tests at once, and with many tests a few will look
  "significant" by chance alone (roughly 1 in 20 at the `*` level). If
  you report specific cells in a paper, say that the p-values are
  uncorrected and exploratory, or apply a correction (e.g.
  Benjamini–Hochberg or Bonferroni) yourself.
- **Significance is not size.** Report the subgroup means (and ideally
  the spread or sample sizes) alongside any p-value so readers can see
  whether a "significant" difference is actually meaningful.

---

## How to describe the method in a paper

You're welcome to adapt either template below. Replace the bracketed
parts; the citations are listed in the next section.

**If you used the parametric setting (Welch's ANOVA):**

> Differences in [outcome, e.g. final-exam score] across [attribute,
> e.g. outline-submission] subgroups were assessed using Welch's
> analysis of variance, which does not assume equal variances across
> groups (Welch, 1951); for two-group comparisons this reduces to
> Welch's *t*-test (Welch, 1947). Tests were computed in Python with
> SciPy (Virtanen et al., 2020). Reported p-values are uncorrected for
> multiple comparisons and are intended as exploratory.

**If you used the non-parametric setting (Kruskal–Wallis):**

> Differences in [outcome] across [attribute] subgroups were assessed
> using the Kruskal–Wallis rank-based test (Kruskal & Wallis, 1952);
> for two-group comparisons this reduces to the Mann–Whitney *U* test
> (Mann & Whitney, 1947). Tests were computed in Python with SciPy
> (Virtanen et al., 2020). Reported p-values are uncorrected for
> multiple comparisons and are intended as exploratory.

---

## Citations

These are the original sources for each method, plus the software the
app uses to compute the tests. The formatting below is APA-style;
adjust to your journal's required style (e.g. Bluebook, Chicago) as
needed.

**Welch's ANOVA (parametric setting):**

> Welch, B. L. (1951). On the comparison of several mean values: An
> alternative approach. *Biometrika, 38*(3/4), 330–336.
> https://doi.org/10.1093/biomet/38.3-4.330

**Welch's *t*-test (the two-group case of the parametric setting):**

> Welch, B. L. (1947). The generalization of "Student's" problem when
> several different population variances are involved. *Biometrika,
> 34*(1/2), 28–35. https://doi.org/10.2307/2332510

**Kruskal–Wallis test (non-parametric setting):**

> Kruskal, W. H., & Wallis, W. A. (1952). Use of ranks in one-criterion
> variance analysis. *Journal of the American Statistical Association,
> 47*(260), 583–621. https://doi.org/10.1080/01621459.1952.10483441

**Mann–Whitney *U* test (the two-group case of the non-parametric
setting):**

> Mann, H. B., & Whitney, D. R. (1947). On a test of whether one of two
> random variables is stochastically larger than the other. *The Annals
> of Mathematical Statistics, 18*(1), 50–60.
> https://doi.org/10.1214/aoms/1177730491

**SciPy (the software that computes the tests):**

> Virtanen, P., Gommers, R., Oliphant, T. E., Haberland, M., Reddy, T.,
> Cournapeau, D., … SciPy 1.0 Contributors. (2020). SciPy 1.0:
> Fundamental algorithms for scientific computing in Python. *Nature
> Methods, 17*(3), 261–272. https://doi.org/10.1038/s41592-019-0686-2
