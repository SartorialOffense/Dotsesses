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
    plt.tight_layout()

    t_rendering = time.perf_counter()

    # Save as SVG to in-memory buffer
    svg_buffer = io.BytesIO()
    plt.savefig(svg_buffer, format='svg', dpi=300, bbox_inches='tight')
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

    # Calculate overall plot bounds to determine series width
    all_x_coords = [p['x'] for p in svg_points]
    if all_x_coords:
        plot_x_min = min(all_x_coords)
        plot_x_max = max(all_x_coords)
        plot_width = plot_x_max - plot_x_min
        num_series = len(series_list)
        # Estimate series width (with some padding factor)
        series_width = (plot_width / num_series) * swarm_spread if num_series > 0 else plot_width

    # Match with data (also sorted by normalized value)
    matched_count = 0
    point_data_list = []  # Collect point data for C# rendering

    for series_name in series_list:
        mask = plot_data['Series'] == series_name
        series_data = plot_data[mask].sort_values('Normalized Value', ascending=False).reset_index(drop=True)
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

        # Calculate center X for this series
        if svg_pts:
            center_x = sum(p['x'] for p in svg_pts) / len(svg_pts)
        else:
            center_x = 0

        # Group points by similar Y values (bin them) to spread horizontally
        # Use a small tolerance to group points at "same" Y level
        y_tolerance = 2.0  # SVG units - points within this range are considered same row

        # Build Y-bins: group points that are close in Y
        y_bins = []
        current_bin = []
        sorted_pts_with_data = list(zip(svg_pts, series_data.iterrows()))

        for svg_point, row in sorted_pts_with_data:
            if not current_bin:
                current_bin.append((svg_point, row))
            else:
                # Check if this point is close to the last one in Y
                last_y = current_bin[-1][0]['y']
                if abs(svg_point['y'] - last_y) <= y_tolerance:
                    current_bin.append((svg_point, row))
                else:
                    y_bins.append(current_bin)
                    current_bin = [(svg_point, row)]

        if current_bin:
            y_bins.append(current_bin)

        # Now spread each bin evenly across the series width
        # Zigzag offset alternates between rows to prevent vertical stacking
        zigzag_offset = dot_size * 0.6  # Offset based on dot size
        max_row_width = series_width * 0.8  # Maximum 80% of series width

        for bin_idx, y_bin in enumerate(y_bins):
            n_points = len(y_bin)

            # Alternate zigzag direction: even rows shift left, odd rows shift right
            row_offset = zigzag_offset if (bin_idx % 2 == 1) else -zigzag_offset

            # Calculate row width: ideal is 3 dot-widths per point, but cap at max
            if n_points <= 1:
                row_width = 0
            else:
                ideal_width = (n_points - 1) * (dot_size * 3)  # 3 dot-widths between each point
                row_width = min(ideal_width, max_row_width)

            for i, (svg_point, row) in enumerate(y_bin):
                _, row_data = row
                elem = svg_point['element']

                # Add generic data attributes to SVG element
                elem.set('data-id', row_data['id'])
                elem.set('data-series', series_name)

                # Calculate spread X position
                if n_points == 1:
                    spread_x = center_x + row_offset
                else:
                    # Spread evenly across row_width
                    spread_fraction = (i / (n_points - 1)) - 0.5  # -0.5 to +0.5
                    spread_x = center_x + spread_fraction * row_width + row_offset

                # Collect point data for C# rendering
                point_data_list.append({
                    'x': spread_x,
                    'y': svg_point['y'],
                    'id': row_data['id'],
                    'series': series_name,
                    'color': color_hex,
                    'value': float(row_data['Value'])
                })

                matched_count += 1

    # Validate all points were matched
    expected_count = len(plot_data)
    if matched_count != expected_count:
        raise ValueError(f"Point matching failed: matched {matched_count} points but expected {expected_count}")

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
