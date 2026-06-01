import pandas as pd
import numpy as np
import matplotlib
matplotlib.use('Agg')  # Non-interactive backend
import matplotlib.pyplot as plt
import seaborn as sns
import xml.etree.ElementTree as ET
import time
from collections import defaultdict
import io
from typing import Tuple, List, Dict, Optional
from colorsys import rgb_to_hls, hls_to_rgb
import math
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


# Default color palette (same as violin plot for consistency)
CYCLING_PALETTE = [
    '#0066FF',  # Bright blue
    '#FF6600',  # Bright orange
    '#00CC00',  # Bright green
    '#FF00FF',  # Bright magenta
    '#9933FF',  # Bright purple
    '#00CCCC',  # Bright cyan
    '#FFCC00',  # Bright yellow
]
TOTAL_COLOR = '#FF3333'  # Bright red - reserved for Total


def get_series_color(idx: int, is_total: bool) -> str:
    """
    Color for a series by its position, with Total drawn red.

    ADR-0018 slice 1: Total identity is an explicit per-series flag, not the
    last-series positional assumption it used to be. Non-Total series cycle
    through CYCLING_PALETTE by index; the Total series is always red wherever
    it sits in the list (and if no series is flagged Total, none is red).
    """
    if is_total:
        return TOTAL_COLOR
    return CYCLING_PALETTE[idx % len(CYCLING_PALETTE)]


def get_r_squared_color(r_squared: float, theme: str = 'dark') -> str:
    """
    Get a color based on r² value using a gradient.

    r² ranges from 0 (no correlation) to 1 (perfect correlation).
    We use a gradient from gray/desaturated (low r²) to vibrant purple/blue (high r²).
    """
    # Clamp r_squared to valid range
    r_squared = max(0.0, min(1.0, r_squared))

    # Color scale from low to high r²:
    # Dark theme: gray -> cyan -> blue -> purple
    # Light theme: light gray -> light cyan -> medium blue -> dark purple

    if theme == 'light':
        # Light theme: darker colors for visibility on white background
        if r_squared < 0.25:
            # Very weak: gray
            gray = int(180 - r_squared * 4 * 40)  # 180 -> 140
            return f'#{gray:02x}{gray:02x}{gray:02x}'
        elif r_squared < 0.5:
            # Weak: gray-blue to cyan
            t = (r_squared - 0.25) / 0.25
            r = int(100 * (1 - t))
            g = int(100 + 80 * t)  # -> 180
            b = int(120 + 80 * t)  # -> 200
            return f'#{r:02x}{g:02x}{b:02x}'
        elif r_squared < 0.75:
            # Moderate: cyan to blue
            t = (r_squared - 0.5) / 0.25
            r = int(30 * (1 - t))
            g = int(180 - 100 * t)  # 180 -> 80
            b = int(200 + 55 * t)   # 200 -> 255
            return f'#{r:02x}{g:02x}{b:02x}'
        else:
            # Strong: blue to purple
            t = (r_squared - 0.75) / 0.25
            r = int(80 * t + 30 * (1 - t))   # 30 -> 80
            g = int(20 * t + 80 * (1 - t))   # 80 -> 20
            b = 255
            return f'#{r:02x}{g:02x}{b:02x}'
    else:
        # Dark theme: brighter, more saturated colors
        if r_squared < 0.25:
            # Very weak: dim gray
            gray = int(80 + r_squared * 4 * 30)  # 80 -> 110
            return f'#{gray:02x}{gray:02x}{gray:02x}'
        elif r_squared < 0.5:
            # Weak: gray-cyan transition
            t = (r_squared - 0.25) / 0.25
            r = int(110 * (1 - t) + 50 * t)
            g = int(110 + 90 * t)  # -> 200
            b = int(110 + 90 * t)  # -> 200
            return f'#{r:02x}{g:02x}{b:02x}'
        elif r_squared < 0.75:
            # Moderate: cyan to bright blue
            t = (r_squared - 0.5) / 0.25
            r = int(50 * (1 - t))
            g = int(200 - 80 * t)  # 200 -> 120
            b = int(200 + 55 * t)  # 200 -> 255
            return f'#{r:02x}{g:02x}{b:02x}'
        else:
            # Strong: blue to vibrant purple
            t = (r_squared - 0.75) / 0.25
            r = int(150 * t)           # 0 -> 150
            g = int(120 - 80 * t)      # 120 -> 40
            b = 255
            return f'#{r:02x}{g:02x}{b:02x}'


def _rest_score_debias(
    x_data: Dict[str, float],
    y_data: Dict[str, float],
    x_is_total: bool,
    y_is_total: bool,
    x_is_bias_correct: bool,
    y_is_bias_correct: bool,
) -> Tuple[Dict[str, float], Dict[str, float], bool]:
    """
    Apply the corrected item-total (rest-score) de-bias to a Total × bias-correct
    cell (ADR-0018).

    Correlating a column X against a Total that *contains* X correlates X partly
    with itself, inflating r. The classical-test-theory fix is to correlate X
    against the rest score Total − X (per common student). We correct ONLY when
    exactly one axis is Total and the other is flagged for bias correction (its
    BiasCorrect flag — e.g. an aggregate component, or a composite column whose
    value is contained in Total); every other cell is returned unchanged.

    Returns (eff_x_data, eff_y_data, corrected).
    """
    if x_is_total and y_is_bias_correct:
        common = set(x_data) & set(y_data)
        rest = {sid: x_data[sid] - y_data[sid] for sid in common}
        return (rest, y_data, True)
    if y_is_total and x_is_bias_correct:
        common = set(x_data) & set(y_data)
        rest = {sid: y_data[sid] - x_data[sid] for sid in common}
        return (x_data, rest, True)
    return (x_data, y_data, False)


def _cell_correlation(
    x_vals: List[float],
    y_vals: List[float],
    method: str,
) -> Tuple[float, Optional[float]]:
    """
    Correlation coefficient and RAW p-value for a cell (ADR-0018 slice 3).

    `method` is 'spearman' for any cell touching an Ordinal column (rank-based
    ρ) and 'pearson' otherwise (r). scipy returns the coefficient and its raw
    two-sided p in one call. Returns (coefficient, p) — p is None when the test
    is undefined (fewer than 2 points, or a constant input that makes the
    coefficient NaN). The coefficient is 0.0 when undefined so the r² color
    falls back to the low end.
    """
    if len(x_vals) < 2:
        return (0.0, None)
    try:
        if method == 'spearman':
            res = _stats.spearmanr(x_vals, y_vals)
            coef, p = float(res.statistic), float(res.pvalue)
        else:
            res = _stats.pearsonr(x_vals, y_vals)
            coef, p = float(res.statistic), float(res.pvalue)
    except (ValueError, FloatingPointError):
        return (0.0, None)
    if math.isnan(coef) or (p is not None and math.isnan(p)):
        return (0.0, None)
    return (coef, p)


def create_correlation_matrix(
    fig_size: Tuple[float, float],
    series: List[Tuple[str, Dict[str, float]]],
    column_types: List[str],
    is_bias_correct: List[bool],
    is_total: List[bool],
    theme: str = 'dark',
    dot_size: float = 3.0,
    show_correlation_coefficients: bool = True,
    diagonal_type: str = 'kde'  # 'kde' or 'hist'
) -> Tuple[Dict[str, int], str, List[Dict]]:
    """
    Creates an NxN corner plot (lower triangle + diagonal) for score correlations.

    Parameters:
    - fig_size: tuple of (width, height) in inches as floats
    - series: list of tuples (series_name, {id: value})
    - column_types: per-series column kind aligned with `series`, one of
        'numeric' / 'categorical' / 'ordinal'. Drives the Pearson/Spearman
        split (an Ordinal-touching cell uses Spearman).
    - is_bias_correct: per-series flag aligned with `series`, True iff the
        column should be rest-score de-biased against Total (its BiasCorrect
        flag). Marks the cells the rest-score de-bias corrects (ADR-0018),
        decoupled from aggregate membership.
    - is_total: per-series flag aligned with `series`, True for the Total
        column. Replaces the old 'Total is the last series' assumption — the
        red Total styling keys off this flag (ADR-0018 slice 1).
    - theme: 'dark' or 'light' for visual theme
    - dot_size: size for scatter dots
    - show_correlation_coefficients: whether to display r values
    - diagonal_type: 'kde' for kernel density, 'hist' for histogram

    Returns:
    - tuple of (timing_dict, svg_string, point_data_list)
    """
    t_start = time.perf_counter()

    # Apply the requested theme
    apply_theme(theme)

    n = len(series)
    if n == 0:
        return ({'TOTAL': 0}, '', [])

    # Normalize the per-series metadata lists to length n so positional indexing
    # is safe even if a caller under-supplies (defensive — C# sends aligned
    # lists of length n).
    is_total = [bool(is_total[k]) if k < len(is_total) else False for k in range(n)]
    is_bias_correct = [
        bool(is_bias_correct[k]) if k < len(is_bias_correct) else False
        for k in range(n)
    ]
    column_types = [
        str(column_types[k]) if k < len(column_types) else 'numeric' for k in range(n)
    ]

    # Create figure with subplots - corner plot (lower triangle only)
    fig, axes = plt.subplots(n, n, figsize=fig_size)

    # Handle single series case
    if n == 1:
        axes = np.array([[axes]])

    # Stat label color based on theme
    stat_label_color = '#555555' if theme == 'light' else '#B4B4B4'

    # Point data for C# overlay
    point_data_list = []

    t_data_prep = time.perf_counter()

    # Collect all student IDs across all series
    all_ids = set()
    for _, scores in series:
        all_ids.update(scores.keys())

    # Per lower-triangle cell, compute the EFFECTIVE data both axes correlate
    # over. For a Total × aggregate-component cell this is the rest-score
    # de-bias (Total − X); every other cell is unchanged (ADR-0018 slice 2).
    # `blank` flags a corrected cell whose rest score is degenerate — a single-
    # component Total makes Total − X ≡ 0 (zero variance → r undefined), so the
    # cell is rendered empty. All downstream passes (r², scatter, regression,
    # point extraction) read this one dict so they never disagree.
    cell_data: Dict[Tuple[int, int], Tuple[Dict[str, float], Dict[str, float], bool, bool]] = {}
    # Per-cell inference (ADR-0018 slice 3): method (pearson/spearman), signed
    # coefficient r/ρ, raw p, N, and stars. A cell touching an Ordinal column
    # uses Spearman. Repeated onto every dot in the cell for the hover tooltip.
    cell_stats: Dict[Tuple[int, int], Dict] = {}
    r_squared_matrix = {}
    for i in range(n):
        _, y_data = series[i]
        for j in range(i):  # Only lower triangle
            _, x_data = series[j]
            eff_x, eff_y, corrected = _rest_score_debias(
                x_data, y_data,
                is_total[j], is_total[i],
                is_bias_correct[j], is_bias_correct[i])

            # Spearman for any Ordinal-touching cell; Pearson otherwise. De-bias
            # already happened above, so an ordinal component feeding Total is
            # de-biased first (Total − X) then ranked (slice 3 precedence).
            method = 'spearman' if ('ordinal' in (column_types[i], column_types[j])) else 'pearson'

            common_ids = set(eff_x.keys()) & set(eff_y.keys())
            blank = False
            coef, p, n_cell = 0.0, None, len(common_ids)
            if len(common_ids) > 1:
                x_vals = [eff_x[sid] for sid in common_ids]
                y_vals = [eff_y[sid] for sid in common_ids]
                if corrected and (np.std(x_vals) == 0 or np.std(y_vals) == 0):
                    # Degenerate rest score (single-component Total). Blank it.
                    blank = True
                else:
                    coef, p = _cell_correlation(x_vals, y_vals, method)
            elif corrected and len(common_ids) < 2:
                blank = True

            cell_data[(i, j)] = (eff_x, eff_y, corrected, blank)
            cell_stats[(i, j)] = {
                'method': method,
                'r': coef,
                'p': p,
                'n': n_cell,
                'corrected': corrected,
                'stars': significance_stars(p),
            }
            r_squared_matrix[(i, j)] = coef ** 2

    # Process each cell
    for i in range(n):
        y_name, y_data = series[i]
        # Use series color for diagonal KDE/histogram. Total identity is an
        # explicit per-series flag now (ADR-0018 slice 1), not last-position.
        y_series_color = get_series_color(i, is_total[i] if i < len(is_total) else False)

        for j in range(n):
            x_name, x_data = series[j]
            x_series_color = get_series_color(j, is_total[j] if j < len(is_total) else False)
            ax = axes[i, j]

            if i < j:
                # Upper triangle: remove axes (corner plot)
                ax.axis('off')
            elif i == j:
                # Diagonal: KDE or histogram - use series color
                values = list(x_data.values())
                if len(values) > 1:
                    if diagonal_type == 'kde':
                        try:
                            sns.kdeplot(values, ax=ax, fill=True, alpha=0.5,
                                       color=x_series_color, linewidth=1.5)
                        except Exception:
                            # Fallback to histogram if KDE fails
                            ax.hist(values, bins='auto', alpha=0.5,
                                   color=x_series_color, edgecolor=x_series_color)
                    else:
                        ax.hist(values, bins='auto', alpha=0.5,
                               color=x_series_color, edgecolor=x_series_color)

                # Add series name in diagonal
                ax.text(0.5, 0.5, x_name, transform=ax.transAxes,
                       ha='center', va='center', fontsize=9, fontweight='bold',
                       color=x_series_color, alpha=0.7)
            else:
                # Lower triangle: scatter plot - color by r². Use the cell's
                # EFFECTIVE data (rest-score de-biased when corrected) so the
                # dots, fitted line, and r annotation are all consistent
                # (ADR-0018 slice 2).
                eff_x, eff_y, corrected, blank = cell_data.get(
                    (i, j), (x_data, y_data, False, False))
                common_ids = sorted(set(eff_x.keys()) & set(eff_y.keys()))

                # Get r²-based color for this cell
                r_sq = r_squared_matrix.get((i, j), 0.0)
                cell_color = get_r_squared_color(r_sq, theme)

                if not blank and len(common_ids) > 0:
                    x_vals = [eff_x[sid] for sid in common_ids]
                    y_vals = [eff_y[sid] for sid in common_ids]

                    # Draw scatter with r²-based color
                    scatter = ax.scatter(x_vals, y_vals, s=dot_size**2,
                                        alpha=0.6, c=cell_color, edgecolors='none')

                    # Draw fitted line (linear regression)
                    if len(x_vals) > 1:
                        try:
                            # Fit line using numpy polyfit
                            slope, intercept = np.polyfit(x_vals, y_vals, 1)
                            x_line = np.array([min(x_vals), max(x_vals)])
                            y_line = slope * x_line + intercept
                            # Draw line with same color but slightly darker/more opaque
                            ax.plot(x_line, y_line, color=cell_color, linewidth=1.5,
                                   alpha=0.8, linestyle='-')
                        except Exception:
                            pass

                    # Effect-size-led annotation in the upper-left corner
                    # (ADR-0018): the variance-explained headline r² (Pearson)
                    # or ρ² (Spearman) — matching the cell color and the
                    # Significance tab's η²/ε² — plus the raw-p significance
                    # stars (soft "worth a closer look" flag; all cells shown).
                    # The sign of the relationship is still read from the fitted
                    # line and the hover tooltip's signed coefficient.
                    if show_correlation_coefficients and len(x_vals) > 1:
                        stats = cell_stats.get((i, j), {})
                        coef = stats.get('r', 0.0)
                        stars = stats.get('stars', '')
                        symbol = 'ρ²' if stats.get('method') == 'spearman' else 'r²'
                        label = symbol + '=' + f'{coef ** 2:.2f}'.lstrip('0')  # '.64' style
                        if stars:
                            label += stars
                        ax.annotate(label, xy=(0.05, 0.95),
                                   xycoords='axes fraction', ha='left', va='top',
                                   fontsize=8, color=stat_label_color)

            # Labels only on edges
            if j == 0 and i > 0:
                ax.set_ylabel(y_name, fontsize=8)
            else:
                ax.set_ylabel('')

            if i == n - 1 and j < n - 1:
                ax.set_xlabel(x_name, fontsize=8)
            else:
                ax.set_xlabel('')

            # Remove tick labels except on edges for cleaner look
            if j != 0:
                ax.set_yticklabels([])
            if i != n - 1:
                ax.set_xticklabels([])

    plt.tight_layout()

    t_rendering = time.perf_counter()

    # Save as SVG to in-memory buffer
    svg_buffer = io.BytesIO()
    plt.savefig(svg_buffer, format='svg', dpi=300, transparent=True)
    svg_buffer.seek(0)
    svg_content = svg_buffer.read().decode('utf-8')
    svg_buffer.close()
    plt.close(fig)

    t_svg_save = time.perf_counter()

    # Parse SVG to extract point positions and build point data
    ET.register_namespace('', 'http://www.w3.org/2000/svg')
    root = ET.fromstring(svg_content)

    # Extract viewBox dimensions
    viewbox = root.get('viewBox', '0 0 800 600')
    vb_parts = viewbox.split()
    svg_width = float(vb_parts[2]) if len(vb_parts) >= 3 else 800
    svg_height = float(vb_parts[3]) if len(vb_parts) >= 4 else 600

    # Calculate cell dimensions
    cell_width = svg_width / n
    cell_height = svg_height / n

    # Find all PathCollection groups (scatter points)
    path_collections = []
    for elem in root.iter():
        if elem.tag.endswith('g') and elem.get('id', '').startswith('PathCollection'):
            path_collections.append(elem)

    # Build point data from scatter plots
    # We need to map SVG positions back to data
    collection_idx = 0
    for i in range(n):
        y_name, y_data = series[i]

        for j in range(n):
            x_name, x_data = series[j]

            if i <= j:
                # Skip upper triangle and diagonal (no scatter points)
                continue

            # Lower triangle. Use the cell's EFFECTIVE (rest-score de-biased)
            # data so the stored x_value/y_value and the SVG point matching line
            # up with what was actually scattered (ADR-0018 slice 2). Blanked
            # cells drew no scatter, so they produced no PathCollection — skip
            # them here too or the collection indices desync.
            eff_x, eff_y, corrected, blank = cell_data.get(
                (i, j), (x_data, y_data, False, False))
            common_ids = sorted(set(eff_x.keys()) & set(eff_y.keys()))

            if blank or len(common_ids) == 0:
                continue

            # Get r²-based color for this cell (same as used for matplotlib scatter)
            r_sq = r_squared_matrix.get((i, j), 0.0)
            cell_color = get_r_squared_color(r_sq, theme)

            # Cell-level inference repeated on every dot for the hover tooltip
            # (ADR-0018 slice 3). p is NaN when undefined (maps back to null C#).
            cs = cell_stats.get((i, j), {})
            cell_p = cs.get('p')
            cell_extra = {
                'r': float(cs.get('r', 0.0)),
                'p_value': float(cell_p) if cell_p is not None else float('nan'),
                'n': int(cs.get('n', len(common_ids))),
                'method': cs.get('method', 'pearson'),
                'corrected': bool(corrected),
            }

            # Get the corresponding PathCollection
            if collection_idx < len(path_collections):
                pc = path_collections[collection_idx]
                collection_idx += 1

                # Extract point positions from <use> elements
                svg_points = []
                for child in pc.iter():
                    if child.tag.endswith('use'):
                        x = child.get('x')
                        y = child.get('y')
                        if x and y:
                            svg_points.append({'x': float(x), 'y': float(y)})

                # Match SVG points to data points by sorting
                # SVG points should be in the same order as data
                x_vals = [eff_x[sid] for sid in common_ids]
                y_vals = [eff_y[sid] for sid in common_ids]

                # Calculate expected positions and match
                if len(svg_points) == len(common_ids):
                    for k, sid in enumerate(common_ids):
                        point_data_list.append({
                            'cell_row': i,
                            'cell_col': j,
                            'x': svg_points[k]['x'],
                            'y': svg_points[k]['y'],
                            'id': sid,
                            'x_series': x_name,
                            'y_series': y_name,
                            'x_value': eff_x[sid],
                            'y_value': eff_y[sid],
                            'color': cell_color,  # Color by r² value
                            **cell_extra,
                        })

                # Mark points for removal from SVG (will render in C#)
                for child in list(pc.iter()):
                    if child.tag.endswith('use'):
                        # Find parent and remove
                        for p in pc.iter():
                            if child in list(p):
                                p.remove(child)
                                break
            else:
                # No SVG points found, calculate positions manually
                # This happens if SVG extraction fails. Effective data again
                # (ADR-0018 slice 2) so manual positions match the de-biased axis.
                x_vals = [eff_x[sid] for sid in common_ids]
                y_vals = [eff_y[sid] for sid in common_ids]

                # Estimate cell bounds
                cell_left = j * cell_width + cell_width * 0.1
                cell_right = (j + 1) * cell_width - cell_width * 0.1
                cell_top = i * cell_height + cell_height * 0.1
                cell_bottom = (i + 1) * cell_height - cell_height * 0.1

                x_min, x_max = min(x_vals), max(x_vals)
                y_min, y_max = min(y_vals), max(y_vals)

                for k, sid in enumerate(common_ids):
                    # Normalize to cell coordinates
                    x_norm = (x_vals[k] - x_min) / (x_max - x_min) if x_max != x_min else 0.5
                    y_norm = (y_vals[k] - y_min) / (y_max - y_min) if y_max != y_min else 0.5

                    px = cell_left + x_norm * (cell_right - cell_left)
                    py = cell_bottom - y_norm * (cell_bottom - cell_top)  # Invert Y

                    point_data_list.append({
                        'cell_row': i,
                        'cell_col': j,
                        'x': px,
                        'y': py,
                        'id': sid,
                        'x_series': x_name,
                        'y_series': y_name,
                        'x_value': eff_x[sid],
                        'y_value': eff_y[sid],
                        'color': cell_color,  # Color by r² value
                        **cell_extra,
                    })

    # Convert back to string
    svg_string = ET.tostring(root, encoding='unicode', method='xml')
    svg_output = '<?xml version=\'1.0\' encoding=\'utf-8\'?>\n' + svg_string

    t_annotations = time.perf_counter()

    # Calculate elapsed times in milliseconds
    timing = {
        'Data Preparation': int((t_data_prep - t_start) * 1000),
        'Rendering': int((t_rendering - t_data_prep) * 1000),
        'SVG Conversion': int((t_svg_save - t_rendering) * 1000),
        'Point Extraction': int((t_annotations - t_svg_save) * 1000),
        'TOTAL': int((t_annotations - t_start) * 1000)
    }

    return (timing, svg_output, point_data_list)
