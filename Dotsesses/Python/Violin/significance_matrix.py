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

Subgroup ordering within a column: alphabetical ascending.

Y-axis scale: shared per row using [min(mean - SEM), max(mean + SEM)] across
all cells in the row, padded by 5%.

Small-N handling:
- N=0 → omit the dot entirely.
- N=1 → render dot, no error bar.
- N≥2 → render dot + SEM error bar.

Returns: (timing_dict, svg_string, point_data_list). Each point dict has
{cell_row, cell_col, x, y, cat_col, num_col, subgroup, mean, sem, n, color}.
The shape leaves room for a future `p_value` field per cell when slice 4
adds the inferential test (see ADR-0014).
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


def create_significance_matrix(
    fig_size: Tuple[float, float],
    numeric_series: List[Tuple[str, Dict[str, float]]],
    categorical_series: List[Tuple[str, Dict[str, str]]],
    theme: str = 'dark',
    dot_size: float = 5.0,
) -> Tuple[Dict[str, int], str, List[Dict]]:
    """
    Build the significance matrix.

    Parameters
    ----------
    fig_size : (width_inches, height_inches)
    numeric_series : list of (column_name, {student_id_str: numeric_value})
    categorical_series : list of (column_name, {student_id_str: subgroup_string})
    theme : 'dark' or 'light'
    dot_size : marker radius in points (squared internally for scatter size)

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
    # cell_stats[(i, j)] = list of (subgroup_label, mean, sem, n) in alpha order
    cell_stats: Dict[Tuple[int, int], List[Tuple[str, float, float, int]]] = {}
    # Also collect per categorical column the canonical subgroup → color-index
    # map so the same subgroup gets the same color across all rows in a column.
    column_subgroup_index: Dict[int, Dict[str, int]] = {}

    for j, (cat_name, cat_data) in enumerate(categorical_series):
        # Canonical ordering for this column — alphabetical ascending across
        # ALL distinct subgroup values seen in the data (even ones that
        # collapse to N=0 in some cells, so they keep their color).
        distinct = sorted(set(cat_data.values()))
        column_subgroup_index[j] = {sg: idx for idx, sg in enumerate(distinct)}

    t_data_prep = time.perf_counter()

    for i, (num_name, num_data) in enumerate(numeric_series):
        for j, (cat_name, cat_data) in enumerate(categorical_series):
            subgroup_to_values: Dict[str, List[float]] = {}
            # Join: student must have both a categorical value and a numeric value
            common_ids = set(cat_data.keys()) & set(num_data.keys())
            for sid in common_ids:
                subgroup_to_values.setdefault(cat_data[sid], []).append(num_data[sid])

            row_stats = []
            for sg in sorted(subgroup_to_values.keys()):
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
                # Use column-canonical alphabetical index so identical
                # subgroup positions across all rows keep the same x slot.
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

            # Cell axes
            y_lo, y_hi = row_ylims[i]
            ax.set_ylim(y_lo, y_hi)

            # X ticks: the full subgroup list for this column (alpha order).
            # Labels AND tick marks only on the bottom row — upper rows hide
            # both so the cells sit flush vertically (hspace=0 above).
            distinct_sgs = sorted(sg_to_idx.keys())
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
