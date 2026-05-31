"""
Significance Matrix plot.

Matrix of small scatter cells: one row per Numeric column, one column per
Categorical column. Each cell plots one dot per Subgroup (distinct value of
the categorical column), positioned at x = subgroup_index, y = mean of the
numeric values within that subgroup, with a vertical SEM error bar.

Coloring: per-subgroup, indexed positionally within each categorical column
using CYCLING_PALETTE (the same palette violin/correlation use). The index
resets per categorical matrix-column, so "Yes" in `Hat` and "Yes" in
`Submitted Outline` may be different colors.

Subgroup ordering within a column: caller-supplied via `ordered_subgroups`
(ADR-0017) — suffixed labels by their ~N rank, then unsuffixed alphabetical.

Y-axis scale: shared per row using [min(mean - SEM), max(mean + SEM)] across
all cells in the row, padded by 5%.

Small-N handling:
- N=0 → omit the dot entirely.
- N=1 → render dot, no error bar.
- N≥2 → render dot + SEM error bar.

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

Returns: (timing_dict, svg_string, point_data_list). Each point dict has
{cell_row, cell_col, x, y, cat_col, num_col, subgroup, mean, sem, n, color,
p_value, effect_size, test_family, excluded}. `p_value` is the cell's omnibus
p (NaN when untestable) and `effect_size` is the cell's variance-explained
effect size (η² parametric / ε² non-parametric, NaN when untestable, ADR-0018);
both are repeated on every dot in the cell. `excluded` flags a dot whose
subgroup was dropped from the test (N<2).
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
) -> Tuple[Dict[str, int], str, List[Dict]]:
    """
    Build the significance matrix.

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
    # cell_stats[(i, j)] = list of (subgroup_label, mean, sem, n) in canonical
    # (caller-supplied) subgroup order — ADR-0017.
    cell_stats: Dict[Tuple[int, int], List[Tuple[str, float, float, int]]] = {}
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
            subgroup_to_values: Dict[str, List[float]] = {}
            # Join: student must have both a categorical value and a numeric value
            common_ids = set(cat_data.keys()) & set(num_data.keys())
            for sid in common_ids:
                subgroup_to_values.setdefault(cat_data[sid], []).append(num_data[sid])

            row_stats = []
            # Walk subgroups in the column's canonical order (ADR-0017) rather
            # than alphabetically, so dot/SVG point order matches the x layout.
            col_idx = column_subgroup_index[j]
            for sg in sorted(subgroup_to_values.keys(),
                             key=lambda s: col_idx.get(s, len(col_idx))):
                values = subgroup_to_values[sg]
                n = len(values)
                if n == 0:
                    continue
                mean_val = float(np.mean(values))
                if n >= 2:
                    sem = float(np.std(values, ddof=1) / math.sqrt(n))
                else:
                    sem = float('nan')  # undefined for N=1
                row_stats.append((sg, mean_val, sem, n))
            cell_stats[(i, j)] = row_stats
            cell_pvalues[(i, j)] = compute_cell_pvalue(subgroup_to_values, test_family)
            cell_effects[(i, j)] = compute_cell_effect_size(subgroup_to_values, test_family)

    # ----- Per-row y-limits: [min(m-SEM), max(m+SEM)] +/- 5% padding -----
    row_ylims: Dict[int, Tuple[float, float]] = {}
    for i in range(n_rows):
        lows: List[float] = []
        highs: List[float] = []
        for j in range(n_cols):
            for (_sg, mean_val, sem, n) in cell_stats.get((i, j), []):
                if n >= 2 and not math.isnan(sem):
                    lows.append(mean_val - sem)
                    highs.append(mean_val + sem)
                else:
                    lows.append(mean_val)
                    highs.append(mean_val)
        if not lows or not highs:
            row_ylims[i] = (-1.0, 1.0)  # arbitrary fallback for an empty row
            continue
        y_lo, y_hi = min(lows), max(highs)
        if y_hi == y_lo:
            # Single dot or perfectly tied values — give a small visual span.
            pad = max(abs(y_hi) * 0.05, 0.5)
        else:
            pad = (y_hi - y_lo) * 0.05
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
    anno_bbox_color = '#FFFFFF' if theme == 'light' else '#202020'

    # Track which (i, j) cells actually got a scatter call so we can map
    # SVG PathCollection groups back to them in order.
    scatter_cells: List[Tuple[int, int]] = []

    for i, (num_name, _) in enumerate(numeric_series):
        for j, (cat_name, _) in enumerate(categorical_series):
            ax = axes[i, j]
            stats = cell_stats.get((i, j), [])
            sg_to_idx = column_subgroup_index[j]

            # X positions: integer ticks per subgroup that appears in this cell
            xs: List[int] = []
            ys: List[float] = []
            colors: List[str] = []
            err_lower: List[float] = []
            err_upper: List[float] = []
            subgroup_labels: List[str] = []

            for (sg, mean_val, sem, n) in stats:
                color_idx = sg_to_idx.get(sg, 0)
                # Use the column-canonical index (ADR-0017 caller order) so
                # identical subgroups keep the same x slot/color across rows.
                x_pos = color_idx
                xs.append(x_pos)
                ys.append(mean_val)
                colors.append(get_subgroup_color(color_idx))
                subgroup_labels.append(sg)
                if n >= 2 and not math.isnan(sem):
                    err_lower.append(sem)
                    err_upper.append(sem)
                else:
                    err_lower.append(0.0)
                    err_upper.append(0.0)

            # Error bars via ax.errorbar so capsize lives in *points* (sized to
            # match the dot), not axis units (which scale with cell width). One
            # call per subgroup so each bar can get its own color.
            for k, (x_pos, y_val, e_lo, e_hi, n_val) in enumerate(
                    zip(xs, ys, err_lower, err_upper,
                        [s[3] for s in stats])):
                if n_val >= 2 and (e_lo > 0 or e_hi > 0):
                    ax.errorbar(
                        [x_pos], [y_val],
                        yerr=[[e_lo], [e_hi]],
                        fmt='none',
                        ecolor=colors[k],
                        elinewidth=1.2,
                        capsize=dot_size,
                        capthick=1.0,
                        alpha=0.8,
                        zorder=2,
                    )

            # Scatter dots — always issue the call even with empty arrays so
            # the SVG PathCollection ordering stays predictable per scatter_cells.
            ax.scatter(xs, ys, s=dot_size ** 2, c=colors,
                       edgecolors='none', alpha=0.9, zorder=3)
            scatter_cells.append((i, j))

            # In-cell significance annotation (top-right corner). Significant
            # cells render bold + tiered stars in a strong color; non-sig /
            # untestable cells render faint. Skipped entirely for cells with no
            # dots at all (nothing was tested and nothing is shown).
            p_val, _excluded = cell_pvalues.get((i, j), (None, set()))
            eff_val = cell_effects.get((i, j))
            if stats:
                anno_label, anno_sig = _format_cell_annotation(p_val, eff_val, test_family)
                ax.text(
                    0.97, 0.95, anno_label,
                    transform=ax.transAxes,
                    ha='right', va='top',
                    fontsize=6.5,
                    fontweight='bold' if anno_sig else 'normal',
                    color=sig_text_color if anno_sig else faint_text_color,
                    zorder=5,
                    bbox=dict(facecolor=anno_bbox_color, alpha=0.55,
                              edgecolor='none', pad=1.0),
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

    # Each ax.scatter call adds one PathCollection (even if empty). Their order
    # is the order of the scatter calls we issued (one per cell).
    for cell_idx, (i, j) in enumerate(scatter_cells):
        stats = cell_stats.get((i, j), [])
        if not stats:
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
        if len(svg_points) == len(stats):
            for k, (sg, mean_val, sem, n) in enumerate(stats):
                color_idx = sg_to_idx.get(sg, 0)
                point_data_list.append({
                    'cell_row': i,
                    'cell_col': j,
                    'x': svg_points[k]['x'],
                    'y': svg_points[k]['y'],
                    'cat_col': cat_name,
                    'num_col': num_name,
                    'subgroup': sg,
                    'mean': float(mean_val),
                    'sem': float(sem) if not math.isnan(sem) else float('nan'),
                    'n': int(n),
                    'color': get_subgroup_color(color_idx),
                    # Cell-level omnibus p, repeated on every dot in the cell
                    # (NaN when untestable); `excluded` flags a dot dropped
                    # from the test for N<2. See ADR-0014 slice 4.
                    'p_value': float(cell_p) if cell_p is not None else float('nan'),
                    # Variance-explained effect size (η²/ε²) — the headline; NaN
                    # when untestable. Repeated on every dot (ADR-0018 slice 4).
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
