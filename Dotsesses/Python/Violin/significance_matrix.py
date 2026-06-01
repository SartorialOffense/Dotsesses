"""
Significance Matrix plot.

Matrix of small cells: one row per Numeric column, one column per Categorical
column. Each cell shows, per Subgroup (distinct value of the categorical
column), a **box plot** (median / IQR / whiskers) with the **individual student
scores jittered on top**, at x = subgroup_index (ADR-0019). This shows spread
and overlap — what an exploratory "do these groups differ?" view needs — rather
than the old mean ± SEM dot, which hid the spread and overstated separation.

Coloring: per-subgroup, indexed positionally within each categorical column
using CYCLING_PALETTE (the same palette violin/correlation use). The index
resets per categorical matrix-column, so "Yes" in `Hat` and "Yes" in
`Submitted Outline` may be different colors.

Subgroup ordering within a column: caller-supplied via `ordered_subgroups`
(ADR-0017) — suffixed labels by their ~N rank, then unsuffixed alphabetical.

Y-axis scale: shared per row, spanning the actual student-value min/max across
all cells in the row, padded by 5% (so every box/point/whisker fits).

Small-N handling:
- N=0 → subgroup omitted entirely.
- N=1 → the single student point, no box.
- N≥2 → box + jittered points.
Narrow cells degrade to box-only (the jitter is skipped) so a point cloud is
never forced into an unreadable space.

An optional mean ± 95% CI overlay (`show_ci`) can be drawn on top — never SEM.

Inferential layer (ADR-0014, "slice 4"): each cell runs ONE omnibus test
over its subgroups and prints the resulting p-value + tiered significance
stars in the cell corner. The `test_family` argument switches all cells
between two robust families:
- 'parametric'    → Welch's ANOVA (unequal-variance-safe; reduces to
                    Welch's t for 2 groups).
- 'nonparametric' → Kruskal–Wallis (rank-based; reduces to Mann–Whitney
                    for 2 groups).
Subgroups with N<2 are dropped from the test (no within-group variance);
the cell is testable only if ≥2 valid groups remain, otherwise it shows
an em-dash. p-values are RAW (uncorrected) — the matrix is an exploratory
screening view, not a confirmatory multiple-comparison procedure.

Returns: (timing_dict, svg_string, point_data_list). Each point dict is now ONE
PER STUDENT (ADR-0019): {cell_row, cell_col, x, y, cat_col, num_col, subgroup,
student_id, value, n, color, p_value, effect_size, test_family, excluded}.
`value` is that student's score; `n` is the student's subgroup size. `p_value`
is the cell's omnibus p (NaN when untestable) and `effect_size` is the cell's
variance-explained effect size (η² parametric / ε² non-parametric, NaN when
untestable, ADR-0018); both are cell-level and repeated on every point.
`excluded` flags a point whose subgroup was dropped from the test (N<2). The box
itself stays in the SVG (decoration); only the student points are extracted for
the C# hover overlay.
"""
import math
import time
import io
import xml.etree.ElementTree as ET
from typing import Tuple, List, Dict, Optional
import matplotlib
matplotlib.use('Agg')  # Non-interactive backend
import matplotlib.pyplot as plt
import numpy as np
from scipy import stats as _stats

from stats_common import significance_stars


def apply_theme(theme: str = 'dark'):
    """Apply matplotlib theme based on theme name."""
    if theme == 'light':
        plt.style.use('default')
        plt.rcParams.update({
            'axes.facecolor': 'white',
            'axes.edgecolor': 'black',
            'axes.labelcolor': 'black',
            'text.color': 'black',
            'xtick.color': 'black',
            'ytick.color': 'black',
            'figure.facecolor': 'white',
        })
    else:
        plt.style.use('dark_background')


# Default color palette (matches violin_swarm.py / correlation_matrix.py).
CYCLING_PALETTE = [
    '#0066FF',  # Bright blue
    '#FF6600',  # Bright orange
    '#00CC00',  # Bright green
    '#FF00FF',  # Bright magenta
    '#9933FF',  # Bright purple
    '#00CCCC',  # Bright cyan
    '#FFCC00',  # Bright yellow
]


def get_subgroup_color(idx: int) -> str:
    """Color for a subgroup by its position within its categorical column."""
    return CYCLING_PALETTE[idx % len(CYCLING_PALETTE)]


def _welch_anova_pvalue(groups: List[List[float]]) -> Optional[float]:
    """
    Welch's ANOVA p-value (unequal-variance one-way ANOVA, Welch 1951).

    scipy has no built-in Welch ANOVA (`f_oneway` assumes equal variance),
    so we compute the closed form directly. For exactly 2 groups this equals
    the two-sided Welch's t-test p-value (F = t²). Returns None when the test
    is undefined — fewer than 2 groups, or any included group has zero
    within-group variance (all-tied values make the Welch weight infinite).
    """
    k = len(groups)
    if k < 2:
        return None
    ns = [len(g) for g in groups]
    means = [float(np.mean(g)) for g in groups]
    variances = [float(np.var(g, ddof=1)) for g in groups]
    if any(v <= 0.0 for v in variances):
        return None

    weights = [n / v for n, v in zip(ns, variances)]
    w_total = sum(weights)
    grand_mean = sum(w * m for w, m in zip(weights, means)) / w_total

    numer = sum(w * (m - grand_mean) ** 2 for w, m in zip(weights, means)) / (k - 1)
    # Σ (1 - w_i/W)² / (n_i - 1)
    tail = sum((1.0 - w / w_total) ** 2 / (n - 1) for w, n in zip(weights, ns))
    denom = 1.0 + (2.0 * (k - 2) / (k ** 2 - 1)) * tail
    f_stat = numer / denom

    df1 = k - 1
    df2 = 1.0 / ((3.0 / (k ** 2 - 1)) * tail)
    return float(_stats.f.sf(f_stat, df1, df2))


def _kruskal_pvalue(groups: List[List[float]]) -> Optional[float]:
    """
    Kruskal–Wallis H-test p-value. For 2 groups this reduces to the
    Mann–Whitney U test. Returns None when undefined (fewer than 2 groups,
    or scipy rejects the input, e.g. every value identical across groups).
    """
    if len(groups) < 2:
        return None
    try:
        _, p = _stats.kruskal(*groups)
    except ValueError:
        return None
    return None if (p is None or math.isnan(p)) else float(p)


def _eta_squared(groups: List[List[float]]) -> Optional[float]:
    """
    Eta-squared (η²) = SS_between / SS_total — the fraction of the numeric
    variance explained by subgroup membership (ADR-0018, parametric path).
    Computed over the same valid groups the Welch test uses. Returns None when
    undefined (fewer than 2 groups, <2 total values, or zero total variance —
    all values identical). Mildly upward-biased as a population estimator, which
    we accept for an exploratory headline.
    """
    if len(groups) < 2:
        return None
    all_vals = [v for g in groups for v in g]
    n = len(all_vals)
    if n < 2:
        return None
    grand_mean = sum(all_vals) / n
    ss_total = sum((v - grand_mean) ** 2 for v in all_vals)
    if ss_total <= 0.0:
        return None
    ss_between = sum(
        len(g) * (float(np.mean(g)) - grand_mean) ** 2 for g in groups)
    return float(ss_between / ss_total)


def _epsilon_squared(groups: List[List[float]]) -> Optional[float]:
    """
    Epsilon-squared (ε²) = H / (n − 1), the rank-based "variance explained" for
    Kruskal–Wallis (ADR-0018, non-parametric path), where H is the KW statistic
    and n the total sample. 0–1, directly comparable to η²/r². Returns None when
    undefined (fewer than 2 groups, or scipy rejects the input).
    """
    if len(groups) < 2:
        return None
    n = sum(len(g) for g in groups)
    if n < 2:
        return None
    try:
        h, _p = _stats.kruskal(*groups)
    except ValueError:
        return None
    if h is None or math.isnan(h):
        return None
    return float(h / (n - 1))


def _partition_valid_groups(
    subgroup_to_values: Dict[str, List[float]],
) -> Tuple[List[List[float]], set]:
    """
    Split a cell's subgroups into the groups eligible for testing (N≥2, the
    only ones with within-group variance) and the labels excluded for being too
    small (N==1). Shared by the p-value and effect-size computations so they
    always test the same groups.
    """
    valid_groups: List[List[float]] = []
    excluded: set = set()
    for sg, values in subgroup_to_values.items():
        if len(values) >= 2:
            valid_groups.append(values)
        elif len(values) >= 1:
            excluded.add(sg)
    return valid_groups, excluded


def compute_cell_pvalue(
    subgroup_to_values: Dict[str, List[float]],
    test_family: str,
) -> Tuple[Optional[float], set]:
    """
    Run the per-cell omnibus test over a cell's subgroups.

    Subgroups with N<2 are dropped (no within-group variance to test); the
    cell is testable only if ≥2 valid groups remain. Returns
    (p_value_or_None, excluded_subgroup_labels).
    """
    valid_groups, excluded = _partition_valid_groups(subgroup_to_values)

    if len(valid_groups) < 2:
        return (None, excluded)

    if test_family == 'nonparametric':
        return (_kruskal_pvalue(valid_groups), excluded)
    return (_welch_anova_pvalue(valid_groups), excluded)


def compute_cell_effect_size(
    subgroup_to_values: Dict[str, List[float]],
    test_family: str,
) -> Optional[float]:
    """
    Variance-explained effect size for a cell on a 0–1 scale (ADR-0018): η²
    (parametric / Welch path) or ε² (non-parametric / Kruskal path). This is the
    headline both stats tabs lead with — comparable to r² on the correlation
    tab. Uses the same valid groups as compute_cell_pvalue; returns None when
    the cell is untestable.
    """
    valid_groups, _excluded = _partition_valid_groups(subgroup_to_values)
    if len(valid_groups) < 2:
        return None
    if test_family == 'nonparametric':
        return _epsilon_squared(valid_groups)
    return _eta_squared(valid_groups)


def compute_significance_pvalues(
    numeric_series: List[Tuple[str, Dict[str, float]]],
    categorical_series: List[Tuple[str, Dict[str, str]]],
    test_family: str = 'parametric',
) -> List[Dict]:
    """
    Per (numeric, categorical) cell, group the numeric values by the
    categorical subgroup over their common students and run the omnibus test
    (same grouping as the matrix; see compute_cell_pvalue). Returns a flat list
    of {'num', 'cat', 'p'} where 'p' is NaN when the cell is untestable.

    A single call computes every cell, so the C# side can drive default
    Significance selection at load time without rendering the matrix or making
    one interop call per cell (ADR-0016).
    """
    results: List[Dict] = []
    for num_name, num_data in numeric_series:
        for cat_name, cat_data in categorical_series:
            subgroup_to_values: Dict[str, List[float]] = {}
            common_ids = set(cat_data.keys()) & set(num_data.keys())
            for sid in common_ids:
                subgroup_to_values.setdefault(cat_data[sid], []).append(num_data[sid])
            p, _excluded = compute_cell_pvalue(subgroup_to_values, test_family)
            results.append({
                'num': num_name,
                'cat': cat_name,
                'p': float(p) if p is not None else float('nan'),
            })
    return results


def _format_pvalue_annotation(p: Optional[float]) -> Tuple[str, bool]:
    """
    Build the in-cell annotation label and whether it is significant.

    Tiers follow the universal convention: * p<.05, ** p<.01, *** p<.001.
    Untestable cells (p is None) render an em-dash. Returns (label, is_sig).
    """
    if p is None:
        return ('—', False)
    stars = significance_stars(p)  # shared tier thresholds (ADR-0018)
    if p < 0.001:
        p_text = 'p<.001'
    else:
        p_text = 'p=' + f'{p:.3f}'.lstrip('0')  # e.g. 'p=.003'
    label = (p_text + ' ' + stars).strip()
    return (label, bool(stars))


def _format_cell_annotation(
    p: Optional[float],
    effect: Optional[float],
    test_family: str,
) -> Tuple[str, bool]:
    """
    Build the in-cell annotation, effect-size-led (ADR-0018). The headline is
    the variance-explained effect size (η² parametric / ε² non-parametric); the
    raw p + stars sit beneath as supporting detail. Untestable cells (p is None)
    render a lone em-dash. Returns (label, is_sig) where is_sig drives the bold
    / strong-color styling.
    """
    if p is None:
        return ('—', False)
    stars = significance_stars(p)
    symbol = 'ε²' if test_family == 'nonparametric' else 'η²'
    if effect is None:
        headline = f'{symbol}=—'
    else:
        headline = symbol + '=' + f'{effect:.2f}'.lstrip('0')  # e.g. 'η²=.42'
    if p < 0.001:
        p_text = 'p<.001'
    else:
        p_text = 'p=' + f'{p:.3f}'.lstrip('0')
    support = (p_text + ' ' + stars).strip()
    return (headline + '\n' + support, bool(stars))


def create_significance_matrix(
    fig_size: Tuple[float, float],
    numeric_series: List[Tuple[str, Dict[str, float]]],
    categorical_series: List[Tuple[str, Dict[str, str]]],
    ordered_subgroups: List[List[str]],
    theme: str = 'dark',
    dot_size: float = 5.0,
    test_family: str = 'parametric',
    show_ci: bool = False,
) -> Tuple[Dict[str, int], str, List[Dict]]:
    """
    Build the significance matrix.

    Each cell shows, per subgroup, a box plot (median / IQR / whiskers) with the
    individual student scores jittered on top — so a reader sees spread and
    overlap, not just central tendency (ADR-0019, replacing the old mean ± SEM
    dot). The per-cell η²/ε² + p annotation and the exploratory caveat are
    unchanged.

    Parameters
    ----------
    fig_size : (width_inches, height_inches)
    numeric_series : list of (column_name, {student_id_str: numeric_value})
    categorical_series : list of (column_name, {student_id_str: subgroup_string})
    ordered_subgroups : list aligned with categorical_series; entry j is the
        caller-supplied canonical left-to-right order of column j's subgroup
        labels (ADR-0017 — ownership of ordering lives in C#). Any present
        label not listed is appended in alphabetical order as a fallback; an
        empty entry falls back to fully alphabetical for that column.
    theme : 'dark' or 'light'
    dot_size : marker radius in points (squared internally for scatter size)
    test_family : 'parametric' (Welch's ANOVA) or 'nonparametric'
        (Kruskal–Wallis) — the omnibus test run per cell for the in-cell
        p-value annotation.
    show_ci : when True, overlay each subgroup's mean ± 95% CI (never SEM).
        Drawn as plain lines (no markers) so it doesn't pollute point extraction.

    Returns
    -------
    (timing_dict, svg_string, point_data_list)
    """
    t_start = time.perf_counter()
    apply_theme(theme)

    n_rows = len(numeric_series)
    n_cols = len(categorical_series)

    if n_rows == 0 or n_cols == 0:
        # Degenerate: render a tiny empty figure with a hint label so the
        # control still has something to display when filters wipe everything.
        fig, ax = plt.subplots(figsize=fig_size)
        ax.set_axis_off()
        ax.text(0.5, 0.5,
                'No Significant rows or columns to plot.',
                transform=ax.transAxes,
                ha='center', va='center',
                fontsize=10,
                color='#888888')
        svg_buffer = io.BytesIO()
        plt.savefig(svg_buffer, format='svg', dpi=300, transparent=True)
        svg_buffer.seek(0)
        svg_output = svg_buffer.read().decode('utf-8')
        svg_buffer.close()
        plt.close(fig)
        return ({'TOTAL': int((time.perf_counter() - t_start) * 1000)}, svg_output, [])

    # ----- Subgroup stats per cell -----
    # cell_stats[(i, j)] = list of (subgroup_label, mean, sem, n, values) in
    # canonical (caller-supplied) subgroup order — ADR-0017. `values` is the
    # subgroup's raw scores, used to draw the box (ADR-0019).
    cell_stats: Dict[Tuple[int, int], List[Tuple[str, float, float, int, List[float]]]] = {}
    # cell_points[(i, j)] = flat list of (subgroup, student_id, value) for every
    # student in the cell, in canonical-subgroup then student-id order — the
    # exact order the jittered scatter is drawn and the SVG <use> elements are
    # extracted (ADR-0019).
    cell_points: Dict[Tuple[int, int], List[Tuple[str, str, float]]] = {}
    # cell_pvalues[(i, j)] = (p_value_or_None, excluded_subgroup_labels) from
    # the per-cell omnibus test (see compute_cell_pvalue / ADR-0014 slice 4).
    cell_pvalues: Dict[Tuple[int, int], Tuple[Optional[float], set]] = {}
    # cell_effects[(i, j)] = variance-explained effect size (η²/ε², ADR-0018) —
    # the headline; None when untestable. Repeated on every dot in the cell.
    cell_effects: Dict[Tuple[int, int], Optional[float]] = {}
    # Also collect per categorical column the canonical subgroup → color-index
    # map so the same subgroup gets the same color across all rows in a column.
    column_subgroup_index: Dict[int, Dict[str, int]] = {}

    for j, (cat_name, cat_data) in enumerate(categorical_series):
        # Canonical ordering for this column comes from the caller (ADR-0017):
        # ordered_subgroups[j] lists the labels in left-to-right order across
        # ALL distinct subgroup values (even ones that collapse to N=0 in some
        # cells, so they keep their color). Present-but-unlisted labels are
        # appended alphabetically; a missing/empty entry falls back to alpha.
        present = set(cat_data.values())
        provided = ordered_subgroups[j] if j < len(ordered_subgroups) else []
        ordered = [sg for sg in provided if sg in present]
        ordered += sorted(present - set(ordered))
        column_subgroup_index[j] = {sg: idx for idx, sg in enumerate(ordered)}

    t_data_prep = time.perf_counter()

    for i, (num_name, num_data) in enumerate(numeric_series):
        for j, (cat_name, cat_data) in enumerate(categorical_series):
            # Join: student must have both a categorical value and a numeric
            # value. Keep the student id paired with the value so each point is
            # identifiable on hover (ADR-0019).
            subgroup_to_pairs: Dict[str, List[Tuple[str, float]]] = {}
            common_ids = set(cat_data.keys()) & set(num_data.keys())
            for sid in common_ids:
                subgroup_to_pairs.setdefault(cat_data[sid], []).append((sid, num_data[sid]))

            row_stats = []
            flat_points: List[Tuple[str, str, float]] = []
            # Walk subgroups in the column's canonical order (ADR-0017) rather
            # than alphabetically; within a subgroup order by student id, so the
            # scatter draw order matches the SVG <use> extraction order.
            col_idx = column_subgroup_index[j]
            for sg in sorted(subgroup_to_pairs.keys(),
                             key=lambda s: col_idx.get(s, len(col_idx))):
                pairs = sorted(subgroup_to_pairs[sg], key=lambda p: p[0])
                values = [v for _, v in pairs]
                n = len(values)
                if n == 0:
                    continue
                mean_val = float(np.mean(values))
                if n >= 2:
                    sem = float(np.std(values, ddof=1) / math.sqrt(n))
                else:
                    sem = float('nan')  # undefined for N=1
                row_stats.append((sg, mean_val, sem, n, values))
                for sid, v in pairs:
                    flat_points.append((sg, sid, v))
            cell_stats[(i, j)] = row_stats
            cell_points[(i, j)] = flat_points

            subgroup_to_values = {sg: [v for _, v in pairs]
                                  for sg, pairs in subgroup_to_pairs.items()}
            cell_pvalues[(i, j)] = compute_cell_pvalue(subgroup_to_values, test_family)
            cell_effects[(i, j)] = compute_cell_effect_size(subgroup_to_values, test_family)

    # ----- Per-row y-limits: span the actual student values +/- padding -----
    # The box + jittered points must all fit, so the row scale is driven by the
    # real data range (not mean ± SEM as before) — ADR-0019. The padding is
    # generous (Y_PAD_FRACTION) so the symbols get vertical breathing room and
    # extreme points aren't jammed against the cell frame.
    Y_PAD_FRACTION = 0.15
    row_ylims: Dict[int, Tuple[float, float]] = {}
    for i in range(n_rows):
        vals: List[float] = []
        for j in range(n_cols):
            for (_sg, _mean, _sem, _n, values) in cell_stats.get((i, j), []):
                vals.extend(values)
        if not vals:
            row_ylims[i] = (-1.0, 1.0)  # arbitrary fallback for an empty row
            continue
        y_lo, y_hi = min(vals), max(vals)
        if y_hi == y_lo:
            # Single dot or perfectly tied values — give a small visual span.
            pad = max(abs(y_hi) * Y_PAD_FRACTION, 0.5)
        else:
            pad = (y_hi - y_lo) * Y_PAD_FRACTION
        row_ylims[i] = (y_lo - pad, y_hi + pad)

    # ----- Render -----
    # sharex='col' so cells in a column share x range (subgroup positions are
    # already aligned per column anyway); hspace=0 so the bottoms of upper-row
    # cells sit flush against the tops of the next row — no gap for x-tick
    # room (we hide ticks on non-bottom rows below).
    fig, axes = plt.subplots(n_rows, n_cols, figsize=fig_size,
                             squeeze=False,
                             sharex='col',
                             gridspec_kw={'hspace': 0})
    label_color = '#555555' if theme == 'light' else '#B4B4B4'
    # Significance annotation colors: strong/dark for significant cells (drawn
    # bold), faint grey for non-significant / untestable cells.
    sig_text_color = '#111111' if theme == 'light' else '#FFFFFF'
    faint_text_color = '#999999'

    # Annotation gutter knobs (ADR-0018): the effect-size + p annotation is
    # drawn just outside each cell's right edge so it never overlaps the dots.
    # ANNO_GUTTER_PAD = how far past the right edge the text starts (axes
    # fraction). COL_GUTTER_WSPACE / RIGHT_MARGIN reserve the horizontal space
    # for it between columns and after the last column. Tune these together if
    # the text crowds the next column or clips at the figure edge.
    ANNO_GUTTER_PAD = 0.04
    COL_GUTTER_WSPACE = 0.40
    RIGHT_MARGIN = 0.88

    # Box + jitter knobs (ADR-0019). JITTER_HALF_WIDTH spreads the per-student
    # points horizontally within their subgroup's x-slot (slot width 1.0).
    # Box-only fallback: when columns get too narrow to read a point cloud,
    # draw the box alone. cell_w_in is the approximate per-cell width in inches.
    JITTER_HALF_WIDTH = 0.11
    MIN_CELL_WIDTH_IN_FOR_POINTS = 0.8
    cell_w_in = fig_size[0] / max(n_cols, 1)
    draw_points = cell_w_in >= MIN_CELL_WIDTH_IN_FOR_POINTS

    # Track which (i, j) cells actually got a scatter call so we can map
    # SVG PathCollection groups back to them in order.
    scatter_cells: List[Tuple[int, int]] = []

    for i, (num_name, _) in enumerate(numeric_series):
        for j, (cat_name, _) in enumerate(categorical_series):
            ax = axes[i, j]
            stats = cell_stats.get((i, j), [])
            points = cell_points.get((i, j), [])
            sg_to_idx = column_subgroup_index[j]

            # Box per subgroup (median / IQR / whiskers), N>=2 (ADR-0019). The
            # box is non-interactive SVG decoration; fliers off (we show every
            # point) and means off (the optional CI overlay covers that). Fill
            # is translucent so the jittered points read through it.
            for (sg, mean_val, sem, n, values) in stats:
                if n < 2:
                    continue
                slot = sg_to_idx.get(sg, 0)
                color = get_subgroup_color(slot)
                bp = ax.boxplot(
                    [values], positions=[slot], widths=0.25,
                    showfliers=False, showmeans=False, patch_artist=True, zorder=2,
                    medianprops=dict(color=color, linewidth=1.4),
                    whiskerprops=dict(color=color, linewidth=1.0, alpha=0.8),
                    capprops=dict(color=color, linewidth=1.0, alpha=0.8),
                    boxprops=dict(edgecolor=color, linewidth=1.0),
                )
                for patch in bp['boxes']:
                    patch.set_facecolor(color)
                    patch.set_alpha(0.25)

            # Jittered student points — ONE scatter call per cell over all
            # students, so there's exactly one PathCollection to extract back.
            # Deterministic jitter (fixed seed) keeps points stable across
            # re-renders/resizes. Skipped for genuinely narrow cells (box-only
            # fallback) so we never force an unreadable cloud.
            if draw_points and points:
                rng = np.random.RandomState(0)
                xs: List[float] = []
                ys: List[float] = []
                pt_colors: List[str] = []
                for (sg, _sid, value) in points:
                    slot = sg_to_idx.get(sg, 0)
                    xs.append(slot + float(rng.uniform(-JITTER_HALF_WIDTH, JITTER_HALF_WIDTH)))
                    ys.append(value)
                    pt_colors.append(get_subgroup_color(slot))
                ax.scatter(xs, ys, s=(dot_size * 0.8) ** 2, c=pt_colors,
                           edgecolors='none', alpha=0.65, zorder=3)
                scatter_cells.append((i, j))

            # Optional mean ± 95% CI overlay (never SEM). Drawn with vlines /
            # hlines only — NO markers — so it produces no <use> elements that
            # would pollute the per-student point extraction below (ADR-0019).
            if show_ci:
                for (sg, mean_val, sem, n, values) in stats:
                    if n < 2 or math.isnan(sem):
                        continue
                    slot = sg_to_idx.get(sg, 0)
                    half = float(_stats.t.ppf(0.975, n - 1)) * sem
                    ax.vlines(slot, mean_val - half, mean_val + half,
                              color=sig_text_color, linewidth=1.3, alpha=0.9, zorder=4)
                    for yy in (mean_val - half, mean_val, mean_val + half):
                        ax.hlines(yy, slot - 0.06, slot + 0.06,
                                  color=sig_text_color, linewidth=1.3, alpha=0.9, zorder=4)

            # In-cell significance annotation (top-right corner). Significant
            # cells render bold + tiered stars in a strong color; non-sig /
            # untestable cells render faint. Skipped entirely for cells with no
            # dots at all (nothing was tested and nothing is shown).
            p_val, _excluded = cell_pvalues.get((i, j), (None, set()))
            eff_val = cell_effects.get((i, j))
            if stats:
                anno_label, anno_sig = _format_cell_annotation(p_val, eff_val, test_family)
                # Draw the annotation in the gutter just OUTSIDE the cell's
                # right edge (x > 1 in axes coords, clip_on=False) so it never
                # overlaps the dots. The widened wspace / right margin below
                # reserves the space for it. Left-justified stack (ma='left').
                ax.text(
                    1.0 + ANNO_GUTTER_PAD, 0.98, anno_label,
                    transform=ax.transAxes,
                    ha='left', va='top', ma='left',
                    fontsize=6.5,
                    fontweight='bold' if anno_sig else 'normal',
                    color=sig_text_color if anno_sig else faint_text_color,
                    zorder=5,
                    clip_on=False,
                )

            # Cell axes
            y_lo, y_hi = row_ylims[i]
            ax.set_ylim(y_lo, y_hi)

            # X ticks: the full subgroup list for this column in canonical
            # (caller-supplied) order — ADR-0017. Labels AND tick marks only on
            # the bottom row — upper rows hide both so the cells sit flush
            # vertically (hspace=0 above).
            distinct_sgs = [sg for sg, _ in sorted(sg_to_idx.items(), key=lambda kv: kv[1])]
            ax.set_xticks(list(range(len(distinct_sgs))))
            if i == n_rows - 1:
                ax.set_xticklabels(distinct_sgs, fontsize=7, rotation=30, ha='right')
            else:
                ax.set_xticklabels([])
                ax.tick_params(axis='x', bottom=False, top=False, labelbottom=False)
            ax.set_xlim(-0.5, max(len(distinct_sgs) - 0.5, 0.5))

            # Perimeter labels
            if j == 0:
                ax.set_ylabel(num_name, fontsize=8)
            else:
                ax.set_ylabel('')
                ax.set_yticklabels([])
            if i == 0:
                ax.set_title(cat_name, fontsize=8, color=label_color)

            # Remove ytick labels on inner columns (we already share scale per row)
            if j != 0:
                ax.tick_params(axis='y', left=False, labelleft=False)

    # tight_layout with h_pad=0 keeps the perimeter trim but preserves the
    # zero inter-row gap we asked gridspec for above.
    plt.tight_layout(h_pad=0)
    # Then force horizontal gutters between columns and a right margin so the
    # per-cell annotations (drawn just outside each cell's right edge) have
    # room and never overlap the next column or clip at the figure edge
    # (ADR-0018). Applied after tight_layout, which would otherwise reset
    # wspace. hspace stays 0 to keep rows flush.
    fig.subplots_adjust(wspace=COL_GUTTER_WSPACE, hspace=0, right=RIGHT_MARGIN)
    t_rendering = time.perf_counter()

    # ----- Save SVG -----
    svg_buffer = io.BytesIO()
    plt.savefig(svg_buffer, format='svg', dpi=300, transparent=True)
    svg_buffer.seek(0)
    svg_content = svg_buffer.read().decode('utf-8')
    svg_buffer.close()
    plt.close(fig)

    t_svg_save = time.perf_counter()

    # ----- Parse SVG to extract dot positions -----
    ET.register_namespace('', 'http://www.w3.org/2000/svg')
    root = ET.fromstring(svg_content)

    path_collections = []
    for elem in root.iter():
        if elem.tag.endswith('g') and elem.get('id', '').startswith('PathCollection'):
            path_collections.append(elem)

    point_data_list: List[Dict] = []

    # Each ax.scatter call adds one PathCollection. Their document order matches
    # the order of the scatter calls we issued (one per cell that drew points;
    # box-only cells aren't in scatter_cells). Within a cell, the <use> order
    # matches cell_points (canonical subgroup, then student id) — ADR-0019.
    for cell_idx, (i, j) in enumerate(scatter_cells):
        points = cell_points.get((i, j), [])
        if not points:
            continue
        if cell_idx >= len(path_collections):
            continue

        pc = path_collections[cell_idx]
        svg_points = []
        for child in pc.iter():
            if child.tag.endswith('use'):
                xs = child.get('x')
                ys = child.get('y')
                if xs is not None and ys is not None:
                    svg_points.append({'x': float(xs), 'y': float(ys)})

        cat_name, _ = categorical_series[j]
        num_name, _ = numeric_series[i]
        sg_to_idx = column_subgroup_index[j]

        cell_p, cell_excluded = cell_pvalues.get((i, j), (None, set()))
        cell_eff = cell_effects.get((i, j))
        # Subgroup sizes for the per-point N (the subgroup the student is in).
        sg_sizes = {sg: n for (sg, _m, _s, n, _v) in cell_stats.get((i, j), [])}
        if len(svg_points) == len(points):
            for k, (sg, sid, value) in enumerate(points):
                color_idx = sg_to_idx.get(sg, 0)
                point_data_list.append({
                    'cell_row': i,
                    'cell_col': j,
                    'x': svg_points[k]['x'],
                    'y': svg_points[k]['y'],
                    'cat_col': cat_name,
                    'num_col': num_name,
                    'subgroup': sg,
                    'student_id': sid,
                    'value': float(value),
                    'n': int(sg_sizes.get(sg, 0)),
                    'color': get_subgroup_color(color_idx),
                    # Cell-level omnibus p, repeated on every point in the cell
                    # (NaN when untestable); `excluded` flags a point whose
                    # subgroup was dropped from the test for N<2. See ADR-0014.
                    'p_value': float(cell_p) if cell_p is not None else float('nan'),
                    # Variance-explained effect size (η²/ε²) — the headline; NaN
                    # when untestable. Repeated on every point (ADR-0018).
                    'effect_size': float(cell_eff) if cell_eff is not None else float('nan'),
                    'test_family': test_family,
                    'excluded': bool(sg in cell_excluded),
                })

        # Remove the rendered <use> elements from the SVG; the dots will be
        # re-drawn in C# on a Canvas overlay so they remain hit-testable.
        for child in list(pc.iter()):
            if child.tag.endswith('use'):
                for parent in pc.iter():
                    if child in list(parent):
                        parent.remove(child)
                        break

    svg_string = ET.tostring(root, encoding='unicode', method='xml')
    svg_output = '<?xml version=\'1.0\' encoding=\'utf-8\'?>\n' + svg_string

    t_annotations = time.perf_counter()

    timing = {
        'Data Preparation': int((t_data_prep - t_start) * 1000),
        'Rendering': int((t_rendering - t_data_prep) * 1000),
        'SVG Conversion': int((t_svg_save - t_rendering) * 1000),
        'Point Extraction': int((t_annotations - t_svg_save) * 1000),
        'TOTAL': int((t_annotations - t_start) * 1000),
    }
    return (timing, svg_output, point_data_list)
