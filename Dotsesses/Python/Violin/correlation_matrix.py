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


def get_series_color(series_name: str, series_list: List[str]) -> str:
    """Get the color for a series based on its position in the list."""
    if series_name not in series_list:
        return '#888888'

    idx = series_list.index(series_name)

    # Last series (Total) is always red
    if idx == len(series_list) - 1:
        return TOTAL_COLOR

    return CYCLING_PALETTE[idx % len(CYCLING_PALETTE)]


def create_correlation_matrix(
    fig_size: Tuple[float, float],
    series: List[Tuple[str, Dict[str, float]]],
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

    series_names = [s[0] for s in series]

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

    # Process each cell
    for i in range(n):
        y_name, y_data = series[i]
        y_color = get_series_color(y_name, series_names)

        for j in range(n):
            x_name, x_data = series[j]
            x_color = get_series_color(x_name, series_names)
            ax = axes[i, j]

            if i < j:
                # Upper triangle: remove axes (corner plot)
                ax.axis('off')
            elif i == j:
                # Diagonal: KDE or histogram
                values = list(x_data.values())
                if len(values) > 1:
                    if diagonal_type == 'kde':
                        try:
                            sns.kdeplot(values, ax=ax, fill=True, alpha=0.5,
                                       color=x_color, linewidth=1.5)
                        except Exception:
                            # Fallback to histogram if KDE fails
                            ax.hist(values, bins='auto', alpha=0.5,
                                   color=x_color, edgecolor=x_color)
                    else:
                        ax.hist(values, bins='auto', alpha=0.5,
                               color=x_color, edgecolor=x_color)

                # Add series name in diagonal
                ax.text(0.5, 0.5, x_name, transform=ax.transAxes,
                       ha='center', va='center', fontsize=9, fontweight='bold',
                       color=x_color, alpha=0.7)
            else:
                # Lower triangle: scatter plot
                # Get common student IDs
                common_ids = sorted(set(x_data.keys()) & set(y_data.keys()))

                if len(common_ids) > 0:
                    x_vals = [x_data[sid] for sid in common_ids]
                    y_vals = [y_data[sid] for sid in common_ids]

                    # Draw scatter - we'll extract positions later
                    scatter = ax.scatter(x_vals, y_vals, s=dot_size**2,
                                        alpha=0.6, c=y_color, edgecolors='none')

                    # Calculate and show correlation coefficient
                    if show_correlation_coefficients and len(x_vals) > 1:
                        try:
                            r = np.corrcoef(x_vals, y_vals)[0, 1]
                            if not np.isnan(r):
                                ax.annotate(f'r={r:.2f}', xy=(0.95, 0.05),
                                           xycoords='axes fraction', ha='right',
                                           fontsize=8, color=stat_label_color)
                        except Exception:
                            pass

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
        y_color = get_series_color(y_name, series_names)

        for j in range(n):
            x_name, x_data = series[j]

            if i <= j:
                # Skip upper triangle and diagonal (no scatter points)
                continue

            # This is a lower triangle scatter plot
            common_ids = sorted(set(x_data.keys()) & set(y_data.keys()))

            if len(common_ids) == 0:
                continue

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
                x_vals = [x_data[sid] for sid in common_ids]
                y_vals = [y_data[sid] for sid in common_ids]

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
                            'x_value': x_data[sid],
                            'y_value': y_data[sid],
                            'color': y_color  # Color by Y-axis series
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
                # This happens if SVG extraction fails
                x_vals = [x_data[sid] for sid in common_ids]
                y_vals = [y_data[sid] for sid in common_ids]

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
                        'x_value': x_data[sid],
                        'y_value': y_data[sid],
                        'color': y_color
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
