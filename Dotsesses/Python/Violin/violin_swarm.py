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


def compact_swarm_layout(y_values: np.ndarray, x_size: float, y_size: float,
                          side: int = 0) -> np.ndarray:
    """
    Compact swarm algorithm for optimal dot placement.

    Places points one at a time, always choosing the unplaced point that can be
    positioned closest to the center axis (x=0). This produces tighter, more
    symmetric layouts than the standard sequential swarm algorithm.

    Based on the R beeswarm package's compactSwarm algorithm.

    Parameters:
    - y_values: array of Y positions (data axis values)
    - x_size: horizontal size of each point (for collision detection)
    - y_size: vertical size of each point (for collision detection)
    - side: -1 (left only), 0 (both sides), 1 (right only)

    Returns:
    - x_offsets: array of X offsets from center for each point
    """
    n = len(y_values)
    if n == 0:
        return np.array([])

    # Normalize sizes so we work in "radius = 1" space
    # Two points collide if their distance < 2 (each has radius 1)
    y_scaled = y_values / y_size if y_size > 0 else y_values
    x_scale = x_size / y_size if y_size > 0 else 1.0

    # Track placement state
    placed = np.zeros(n, dtype=bool)
    x_offsets = np.zeros(n)

    # For each unplaced point, track the best (closest to 0) valid X position
    x_low = np.zeros(n)   # Best negative X position
    x_high = np.zeros(n)  # Best positive X position
    x_best = np.zeros(n)  # Current best X (whichever is closer to 0)

    for iteration in range(n):
        # Find unplaced point with x_best closest to 0
        best_idx = -1
        best_abs = float('inf')
        for i in range(n):
            if not placed[i]:
                if abs(x_best[i]) < best_abs:
                    best_abs = abs(x_best[i])
                    best_idx = i

        if best_idx < 0:
            break

        # Place this point
        i = best_idx
        xi = x_best[i]
        yi = y_scaled[i]
        x_offsets[i] = xi
        placed[i] = True

        # Update constraints for all remaining unplaced points
        for j in range(n):
            if placed[j]:
                continue

            # Check if point j is close enough in Y to potentially collide
            y_diff = abs(yi - y_scaled[j])
            if y_diff >= 2.0:  # Too far apart in Y, no collision possible
                continue

            # Calculate minimum X separation needed to avoid collision
            # Two circles of radius 1 collide if distance < 2
            # distance = sqrt(dx^2 + dy^2) >= 2
            # dx >= sqrt(4 - dy^2)
            x_separation = math.sqrt(4.0 - y_diff * y_diff)

            # Update the valid X ranges for point j
            # Point j cannot be placed within x_separation of xi
            new_high = xi + x_separation
            new_low = xi - x_separation

            if new_high > x_high[j]:
                x_high[j] = new_high
            if new_low < x_low[j]:
                x_low[j] = new_low

            # Update best X for point j
            if side == 0:
                # Both sides allowed - pick whichever is closer to 0
                if -x_low[j] < x_high[j]:
                    x_best[j] = x_low[j]
                else:
                    x_best[j] = x_high[j]
            elif side == 1:
                # Right side only
                x_best[j] = x_high[j]
            else:
                # Left side only
                x_best[j] = x_low[j]

    # Scale back to original units
    x_offsets = x_offsets * x_scale * y_size

    # If side == -1, we computed right-side and need to flip
    if side == -1:
        x_offsets = -x_offsets

    return x_offsets


def saturate_color(rgb):
    """Convert RGB tuple (0-1 range) to fully saturated version."""
    r, g, b = rgb[:3]  # Handle RGBA tuples
    h, l, s = rgb_to_hls(r, g, b)
    # Set saturation to maximum (1.0)
    return hls_to_rgb(h, l, 1.0)

# Apply dark theme
plt.style.use('dark_background')


def create_violin_swarm_plot(
    fig_size: Tuple[float, float],
    series: List[Tuple[str, Dict[str, float]]],
    colors: Optional[List[str]] = None,
    title: str = '',
    xlabel: str = 'Series',
    ylabel: str = 'Normalized Score (0-1)',
    dot_size: float = 5.0,
    swarm_spread: float = 1.0
) -> Tuple[Dict[str, int], str, List[Dict]]:
    """
    Create a violin + swarm plot with embedded metadata.

    Parameters:
    - fig_size: tuple of (width, height) in inches as floats
    - series: list of tuples (series_name, {id: value})
    - colors: optional list of color strings (hex or matplotlib named colors)
    - title: plot title (default: empty, no title shown)
    - xlabel: x-axis label (default: 'Series')
    - ylabel: y-axis label (default: 'Normalized Score (0-1)')
    - dot_size: size for dots (default: 5.0)
    - swarm_spread: multiplier for horizontal spread of swarm dots (default: 1.0, use 4.0 for 4x spread)

    Returns:
    - tuple of (timing_dict, svg_string, point_data_list)
      timing_dict contains: {'Phase Name': time_in_ms, ...}
      svg_string contains the SVG with violin plots but without swarm points
      point_data_list contains: [{'x': float, 'y': float, 'id': str, 'series': str, 'color': str}, ...]
    """
    t_start = time.perf_counter()

    # Set random seed for reproducible jitter positions
    np.random.seed(42)

    # Prepare data for plotting
    rows = []
    for series_name, id_scores in series:
        for item_id, score in id_scores.items():
            rows.append({
                'id': item_id,
                'Series': series_name,
                'Value': score
            })

    plot_data = pd.DataFrame(rows)

    # Normalize values to same height (0-1 scale per series)
    plot_data['Normalized Value'] = plot_data.groupby('Series')['Value'].transform(
        lambda x: (x - x.min()) / (x.max() - x.min())
    )

    t_data_prep = time.perf_counter()

    # Create the plot
    fig, ax = plt.subplots(figsize=fig_size)

    # Use provided colors or explicit fully saturated colors
    if colors is None:
        # Cycling palette for non-Total series (excludes red, which is reserved for Total)
        cycling_palette = [
            '#0066FF',  # Bright blue
            '#FF6600',  # Bright orange
            '#00CC00',  # Bright green
            '#FF00FF',  # Bright magenta
            '#9933FF',  # Bright purple
            '#00CCCC',  # Bright cyan
            '#FFCC00',  # Bright yellow
        ]
        total_color = '#FF3333'  # Bright red - reserved for Total (last series)

        # Build color list: cycle through palette for all but last, red for last (Total)
        num_series = len(series)
        colors = []
        for i in range(num_series - 1):
            colors.append(cycling_palette[i % len(cycling_palette)])
        colors.append(total_color)  # Last series (Total) is always red

    # Sort data by ID for consistent ordering
    plot_data = plot_data.sort_values(['Series', 'id']).reset_index(drop=True)

    # Create violin plot (alpha=0.4 for more transparency, making dots more visible)
    sns.violinplot(data=plot_data, x='Series', y='Normalized Value',
                   hue='Series', palette=colors, alpha=0.4, inner=None,
                   legend=False, ax=ax)

    # Reset seed right before swarmplot for consistent positioning
    np.random.seed(42)

    # Add swarm plot - beeswarm algorithm avoids overlap for easier selection
    swarm = sns.swarmplot(data=plot_data, x='Series', y='Normalized Value',
                          hue='Series', palette=colors, size=dot_size,
                          dodge=False, legend=False, ax=ax)

    # Customize plot
    if title:
        ax.set_title(title, fontsize=16, fontweight='bold', pad=20, color='white')
    ax.set_xlabel(xlabel, fontsize=12, fontweight='bold')
    ax.set_ylabel(ylabel, fontsize=12, fontweight='bold')
    # Grid removed for cleaner appearance
    # Simplify y-axis to show key reference points
    ax.set_yticks([0, 0.25, 0.5, 0.75, 1.0])
    ax.set_yticklabels(['0', '0.25', '0.5', '0.75', '1.0'])
    plt.xticks(rotation=15, ha='right')

    # ===== Add Statistics Tick Marks on Right Side =====
    # Calculate mean and std dev from the Total series (last series)
    total_series_name = series[-1][0]
    total_data = plot_data[plot_data['Series'] == total_series_name]['Normalized Value']

    if len(total_data) > 0:
        mean_val = total_data.mean()
        std_val = total_data.std()

        # Create secondary y-axis on the right for statistics ticks
        ax2 = ax.twinx()
        ax2.set_ylim(ax.get_ylim())  # Match the primary y-axis limits

        # Build tick positions and labels
        stat_ticks = [mean_val]
        stat_labels = ['μ']

        # Add positive standard deviations
        for i in range(1, 6):
            pos = mean_val + i * std_val
            if pos <= 1.0:
                stat_ticks.append(pos)
                stat_labels.append(f'+{i}σ')

        # Add negative standard deviations
        for i in range(1, 6):
            pos = mean_val - i * std_val
            if pos >= 0.0:
                stat_ticks.append(pos)
                stat_labels.append(f'-{i}σ')

        ax2.set_yticks(stat_ticks)
        ax2.set_yticklabels(stat_labels, fontsize=10, color='#B4B4B4')
        ax2.tick_params(axis='y', length=0, pad=2)  # No tick marks, just labels
        # Hide all spines on secondary axis
        for spine in ax2.spines.values():
            spine.set_visible(False)

    # Add internal padding on the right side of the plot (inside the plot area)
    # This creates space between the last series and the right edge/statistics labels
    xlim = ax.get_xlim()
    ax.set_xlim(xlim[0], xlim[1] + 0.25)  # Add padding on the right for statistics

    # Add padding on left and right to prevent violin plots from being cut off
    plt.tight_layout()

    t_rendering = time.perf_counter()

    # Save as SVG to in-memory buffer
    # Note: Don't use bbox_inches='tight' as it overrides subplots_adjust padding
    # Use transparent=True so Avalonia's theme background shows through
    svg_buffer = io.BytesIO()
    plt.savefig(svg_buffer, format='svg', dpi=300, transparent=True)
    svg_buffer.seek(0)
    svg_content = svg_buffer.read().decode('utf-8')
    svg_buffer.close()
    plt.close(fig)

    t_svg_save = time.perf_counter()

    # Parse SVG and add annotations
    ET.register_namespace('', 'http://www.w3.org/2000/svg')
    root = ET.fromstring(svg_content)

    # Find all use elements in PathCollection groups (swarm points)
    svg_points = []
    series_list = plot_data['Series'].unique()

    for elem in root.iter():
        if elem.tag.endswith('g') and elem.get('id', '').startswith('PathCollection'):
            collection_id = elem.get('id')
            collection_idx = int(collection_id.replace('PathCollection_', '')) - 1
            series_name = series_list[collection_idx] if collection_idx < len(series_list) else None

            for child in elem.iter():
                if child.tag.endswith('use'):
                    x = child.get('x')
                    y = child.get('y')
                    if x and y and series_name:
                        svg_points.append({
                            'element': child,
                            'x': float(x),
                            'y': float(y),
                            'series': series_name
                        })

    # Group SVG points by series and sort by y-coordinate
    points_by_series = defaultdict(list)
    for point in svg_points:
        points_by_series[point['series']].append(point)

    # Sort each group by y-coordinate (top to bottom in SVG = low to high y)
    for series_name in points_by_series:
        points_by_series[series_name].sort(key=lambda p: p['y'])

    # Calculate overall plot bounds to determine series width and Y mapping
    all_x_coords = [p['x'] for p in svg_points]
    all_y_coords = [p['y'] for p in svg_points]
    if all_x_coords:
        plot_x_min = min(all_x_coords)
        plot_x_max = max(all_x_coords)
        plot_width = plot_x_max - plot_x_min
        num_series = len(series_list)
        # Estimate series width (with some padding factor)
        series_width = (plot_width / num_series) * swarm_spread if num_series > 0 else plot_width

    # Calculate Y bounds for mapping normalized values to SVG Y coordinates
    # In SVG, Y increases downward, so min Y = top (normalized 1.0), max Y = bottom (normalized 0.0)
    if all_y_coords:
        svg_y_min = min(all_y_coords)  # Top of plot (high values)
        svg_y_max = max(all_y_coords)  # Bottom of plot (low values)
    else:
        svg_y_min = 0
        svg_y_max = 100

    # Generate point data using compact swarm algorithm
    point_data_list = []

    for series_name in series_list:
        mask = plot_data['Series'] == series_name
        series_data = plot_data[mask].reset_index(drop=True)
        svg_pts = points_by_series.get(series_name, [])

        # Get color for this series (from palette)
        series_idx = list(series_list).index(series_name)
        color_rgb = colors[series_idx] if series_idx < len(colors) else (0.5, 0.5, 0.5)

        # Convert matplotlib RGB (0-1) to hex color
        if isinstance(color_rgb, str):
            color_hex = color_rgb  # Already a hex string
        else:
            color_hex = '#{:02x}{:02x}{:02x}'.format(
                int(color_rgb[0] * 255),
                int(color_rgb[1] * 255),
                int(color_rgb[2] * 255)
            )

        # Calculate center X for this series from SVG points
        if svg_pts:
            center_x = sum(p['x'] for p in svg_pts) / len(svg_pts)
        else:
            center_x = 0

        # Get normalized values for swarm algorithm
        y_values = series_data['Normalized Value'].values

        # Run compact swarm algorithm
        # y_size controls collision detection threshold in normalized units
        # x_size controls horizontal spacing in SVG units
        y_size = 0.02  # Normalized units - points within this Y distance can collide
        x_size = dot_size * 1.5  # SVG units for horizontal collision

        x_offsets = compact_swarm_layout(y_values, x_size, y_size, side=0)

        # Scale X offsets to fit within series width
        if len(x_offsets) > 0 and np.max(np.abs(x_offsets)) > 0:
            max_offset = np.max(np.abs(x_offsets))
            max_allowed = series_width * 0.4  # Use 40% of series width on each side
            if max_offset > max_allowed:
                scale_factor = max_allowed / max_offset
                x_offsets = x_offsets * scale_factor

        # Generate point data
        for i, (_, row_data) in enumerate(series_data.iterrows()):
            norm_val = row_data['Normalized Value']

            # X position = center + swarm offset
            spread_x = center_x + x_offsets[i]

            # Calculate Y directly from normalized value using matplotlib's exact transform
            # Linear interpolation between svg_y_max (norm=0) and svg_y_min (norm=1)
            correct_y = svg_y_max - norm_val * (svg_y_max - svg_y_min)

            # Mark SVG element for removal (if we have corresponding SVG point)
            if i < len(svg_pts):
                elem = svg_pts[i]['element']
                elem.set('data-id', row_data['id'])
                elem.set('data-series', series_name)

            # Collect point data for C# rendering
            point_data_list.append({
                'x': spread_x,
                'y': correct_y,
                'id': row_data['id'],
                'series': series_name,
                'color': color_hex,
                'value': float(row_data['Value'])
            })

    # Validate all points were generated
    expected_count = len(plot_data)
    if len(point_data_list) != expected_count:
        raise ValueError(f"Point generation failed: generated {len(point_data_list)} points but expected {expected_count}")

    # Remove swarm points from SVG (they will be rendered dynamically in C#)
    for elem in root.iter():
        if elem.tag.endswith('g') and elem.get('id', '').startswith('PathCollection'):
            # Find all <use> elements with data-id and remove them
            for child in list(elem.iter()):
                if child.tag.endswith('use') and child.get('data-id') is not None:
                    # Remove from parent
                    parent = elem
                    for p in root.iter():
                        if child in list(p):
                            p.remove(child)
                            break

    # Convert back to string
    svg_string = ET.tostring(root, encoding='unicode', method='xml')
    # Add XML declaration
    svg_output = '<?xml version=\'1.0\' encoding=\'utf-8\'?>\n' + svg_string

    t_annotations = time.perf_counter()

    # Calculate elapsed times in milliseconds
    timing = {
        'Data Preparation': int((t_data_prep - t_start) * 1000),
        'Rendering': int((t_rendering - t_data_prep) * 1000),
        'SVG Conversion': int((t_svg_save - t_rendering) * 1000),
        'Adding Annotations': int((t_annotations - t_svg_save) * 1000),
        'TOTAL': int((t_annotations - t_start) * 1000)
    }

    return (timing, svg_output, point_data_list)
