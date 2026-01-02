import numpy as np
import matplotlib
matplotlib.use('Agg')  # Non-interactive backend
import matplotlib.pyplot as plt
import matplotlib.colors as mcolors
from sklearn.decomposition import PCA
from sklearn.manifold import TSNE
from sklearn.preprocessing import StandardScaler
import io
import time
from typing import Tuple, List, Dict, Optional

# Try to import UMAP (may not be installed)
try:
    import umap
    UMAP_AVAILABLE = True
except ImportError:
    UMAP_AVAILABLE = False


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


def get_viridis_color(value: float, theme: str = 'dark') -> str:
    """
    Get a viridis colormap color for a normalized value (0-1).
    Returns hex color string.
    """
    # Clamp value to valid range
    value = max(0.0, min(1.0, value))

    # Get viridis colormap
    cmap = plt.cm.viridis
    rgba = cmap(value)

    # Convert to hex
    return mcolors.rgb2hex(rgba[:3])


def normalize_scores(scores: List[float]) -> List[float]:
    """Normalize scores to 0-1 range for color mapping."""
    if not scores:
        return []

    min_score = min(scores)
    max_score = max(scores)

    if max_score == min_score:
        return [0.5] * len(scores)

    return [(s - min_score) / (max_score - min_score) for s in scores]


def create_pca_plot(
    fig_size: Tuple[float, float],
    series_data: List[Tuple[str, Dict[str, float]]],
    total_scores: Dict[str, float],
    theme: str = 'dark',
    dot_size: float = 5.0
) -> Tuple[Dict[str, int], str, List[Dict], Tuple[float, float]]:
    """
    Creates a 2D PCA scatter plot colored by total score.

    Parameters:
    - fig_size: tuple of (width, height) in inches
    - series_data: list of tuples (series_name, {student_id: value}) - excludes Total
    - total_scores: dict {student_id: total_score} for coloring
    - theme: 'dark' or 'light'
    - dot_size: size for scatter dots

    Returns:
    - tuple of (timing_dict, svg_string, point_data_list, explained_variance)
    """
    t_start = time.perf_counter()
    apply_theme(theme)

    # Get common student IDs across all series
    if not series_data:
        return ({'TOTAL': 0}, '', [], (0.0, 0.0))

    common_ids = set(series_data[0][1].keys())
    for _, scores in series_data[1:]:
        common_ids &= set(scores.keys())
    common_ids &= set(total_scores.keys())
    common_ids = sorted(common_ids)

    if len(common_ids) < 2:
        return ({'TOTAL': 0}, '', [], (0.0, 0.0))

    # Build feature matrix
    n_samples = len(common_ids)
    n_features = len(series_data)
    X = np.zeros((n_samples, n_features))

    for j, (_, scores) in enumerate(series_data):
        for i, sid in enumerate(common_ids):
            X[i, j] = scores.get(sid, 0)

    # Get total scores for coloring
    totals = [total_scores.get(sid, 0) for sid in common_ids]
    normalized_totals = normalize_scores(totals)
    colors = [get_viridis_color(v, theme) for v in normalized_totals]

    t_data_prep = time.perf_counter()

    # Standardize and fit PCA
    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    pca = PCA(n_components=2)
    X_pca = pca.fit_transform(X_scaled)

    explained_var = (pca.explained_variance_ratio_[0] * 100,
                     pca.explained_variance_ratio_[1] * 100)

    t_pca = time.perf_counter()

    # Create figure
    fig, ax = plt.subplots(figsize=fig_size)

    # Scatter plot
    scatter = ax.scatter(X_pca[:, 0], X_pca[:, 1],
                        c=colors, s=dot_size**2, alpha=0.7, edgecolors='none')

    # Labels with explained variance
    ax.set_xlabel(f'PC1 ({explained_var[0]:.1f}% var)', fontsize=10)
    ax.set_ylabel(f'PC2 ({explained_var[1]:.1f}% var)', fontsize=10)
    ax.set_title('PCA Projection', fontsize=12, fontweight='bold')

    # Add colorbar
    sm = plt.cm.ScalarMappable(cmap='viridis',
                               norm=plt.Normalize(vmin=min(totals), vmax=max(totals)))
    sm.set_array([])
    cbar = plt.colorbar(sm, ax=ax, shrink=0.8)
    cbar.set_label('Total Score', fontsize=9)

    plt.tight_layout()

    t_rendering = time.perf_counter()

    # Convert data coordinates to display coordinates BEFORE saving
    # This captures the actual pixel positions in the SVG
    fig.canvas.draw()
    display_coords = ax.transData.transform(X_pca)

    # Get the figure dimensions in pixels at current DPI
    fig_dpi = fig.dpi  # Default is 100
    save_dpi = 300  # DPI used when saving SVG
    dpi_scale = save_dpi / fig_dpi

    # SVG viewBox dimensions are scaled by the save DPI
    fig_height_px = fig.get_figheight() * save_dpi

    # Save as SVG
    svg_buffer = io.BytesIO()
    plt.savefig(svg_buffer, format='svg', dpi=save_dpi, transparent=True)
    svg_buffer.seek(0)
    svg_content = svg_buffer.read().decode('utf-8')
    svg_buffer.close()
    plt.close(fig)

    t_svg_save = time.perf_counter()

    # Build point data for C# overlay using display coordinates
    # Scale coordinates from figure DPI to SVG DPI, and flip Y for SVG coordinate system
    point_data_list = []
    for i, sid in enumerate(common_ids):
        point_data_list.append({
            'x': float(display_coords[i, 0] * dpi_scale),
            'y': float(fig_height_px - display_coords[i, 1] * dpi_scale),  # Flip Y for SVG
            'id': sid,
            'total_score': totals[i],
            'color': colors[i]
        })

    t_end = time.perf_counter()

    timing = {
        'Data Preparation': int((t_data_prep - t_start) * 1000),
        'PCA Computation': int((t_pca - t_data_prep) * 1000),
        'Rendering': int((t_rendering - t_pca) * 1000),
        'SVG Conversion': int((t_svg_save - t_rendering) * 1000),
        'TOTAL': int((t_end - t_start) * 1000)
    }

    return (timing, svg_content, point_data_list, explained_var)


def create_umap_plot(
    fig_size: Tuple[float, float],
    series_data: List[Tuple[str, Dict[str, float]]],
    total_scores: Dict[str, float],
    theme: str = 'dark',
    dot_size: float = 5.0,
    n_neighbors: int = 15,
    min_dist: float = 0.1
) -> Tuple[Dict[str, int], str, List[Dict]]:
    """
    Creates a 2D UMAP scatter plot colored by total score.

    Parameters:
    - fig_size: tuple of (width, height) in inches
    - series_data: list of tuples (series_name, {student_id: value})
    - total_scores: dict {student_id: total_score} for coloring
    - theme: 'dark' or 'light'
    - dot_size: size for scatter dots
    - n_neighbors: UMAP parameter (5-50)
    - min_dist: UMAP parameter (0.0-1.0)

    Returns:
    - tuple of (timing_dict, svg_string, point_data_list)
    """
    if not UMAP_AVAILABLE:
        return ({'TOTAL': 0, 'error': 'UMAP not installed'}, '', [])

    t_start = time.perf_counter()
    apply_theme(theme)

    # Get common student IDs
    if not series_data:
        return ({'TOTAL': 0}, '', [])

    common_ids = set(series_data[0][1].keys())
    for _, scores in series_data[1:]:
        common_ids &= set(scores.keys())
    common_ids &= set(total_scores.keys())
    common_ids = sorted(common_ids)

    if len(common_ids) < 2:
        return ({'TOTAL': 0}, '', [])

    # Build feature matrix
    n_samples = len(common_ids)
    n_features = len(series_data)
    X = np.zeros((n_samples, n_features))

    for j, (_, scores) in enumerate(series_data):
        for i, sid in enumerate(common_ids):
            X[i, j] = scores.get(sid, 0)

    # Get total scores for coloring
    totals = [total_scores.get(sid, 0) for sid in common_ids]
    normalized_totals = normalize_scores(totals)
    colors = [get_viridis_color(v, theme) for v in normalized_totals]

    t_data_prep = time.perf_counter()

    # Standardize
    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    # Fit UMAP
    reducer = umap.UMAP(
        n_neighbors=n_neighbors,
        min_dist=min_dist,
        n_components=2,
        metric='euclidean',
        random_state=42
    )
    X_umap = reducer.fit_transform(X_scaled)

    t_umap = time.perf_counter()

    # Create figure
    fig, ax = plt.subplots(figsize=fig_size)

    scatter = ax.scatter(X_umap[:, 0], X_umap[:, 1],
                        c=colors, s=dot_size**2, alpha=0.7, edgecolors='none')

    ax.set_xlabel('UMAP 1', fontsize=10)
    ax.set_ylabel('UMAP 2', fontsize=10)
    ax.set_title(f'UMAP (neighbors={n_neighbors}, min_dist={min_dist})',
                fontsize=12, fontweight='bold')

    # Add colorbar
    sm = plt.cm.ScalarMappable(cmap='viridis',
                               norm=plt.Normalize(vmin=min(totals), vmax=max(totals)))
    sm.set_array([])
    cbar = plt.colorbar(sm, ax=ax, shrink=0.8)
    cbar.set_label('Total Score', fontsize=9)

    plt.tight_layout()

    t_rendering = time.perf_counter()

    # Convert data coordinates to display coordinates BEFORE saving
    fig.canvas.draw()
    display_coords = ax.transData.transform(X_umap)

    # Get the figure dimensions in pixels at current DPI
    fig_dpi = fig.dpi  # Default is 100
    save_dpi = 300  # DPI used when saving SVG
    dpi_scale = save_dpi / fig_dpi

    # SVG viewBox dimensions are scaled by the save DPI
    fig_height_px = fig.get_figheight() * save_dpi

    # Save as SVG
    svg_buffer = io.BytesIO()
    plt.savefig(svg_buffer, format='svg', dpi=save_dpi, transparent=True)
    svg_buffer.seek(0)
    svg_content = svg_buffer.read().decode('utf-8')
    svg_buffer.close()
    plt.close(fig)

    t_svg_save = time.perf_counter()

    # Build point data using display coordinates
    # Scale coordinates from figure DPI to SVG DPI, and flip Y for SVG coordinate system
    point_data_list = []
    for i, sid in enumerate(common_ids):
        point_data_list.append({
            'x': float(display_coords[i, 0] * dpi_scale),
            'y': float(fig_height_px - display_coords[i, 1] * dpi_scale),  # Flip Y for SVG
            'id': sid,
            'total_score': totals[i],
            'color': colors[i]
        })

    t_end = time.perf_counter()

    timing = {
        'Data Preparation': int((t_data_prep - t_start) * 1000),
        'UMAP Computation': int((t_umap - t_data_prep) * 1000),
        'Rendering': int((t_rendering - t_umap) * 1000),
        'SVG Conversion': int((t_svg_save - t_rendering) * 1000),
        'TOTAL': int((t_end - t_start) * 1000)
    }

    return (timing, svg_content, point_data_list)


def create_tsne_plot(
    fig_size: Tuple[float, float],
    series_data: List[Tuple[str, Dict[str, float]]],
    total_scores: Dict[str, float],
    theme: str = 'dark',
    dot_size: float = 5.0,
    perplexity: float = 30.0,
    learning_rate: float = 200.0
) -> Tuple[Dict[str, int], str, List[Dict]]:
    """
    Creates a 2D t-SNE scatter plot colored by total score.

    Parameters:
    - fig_size: tuple of (width, height) in inches
    - series_data: list of tuples (series_name, {student_id: value})
    - total_scores: dict {student_id: total_score} for coloring
    - theme: 'dark' or 'light'
    - dot_size: size for scatter dots
    - perplexity: t-SNE parameter (5-50)
    - learning_rate: t-SNE parameter (10-1000)

    Returns:
    - tuple of (timing_dict, svg_string, point_data_list)
    """
    t_start = time.perf_counter()
    apply_theme(theme)

    # Get common student IDs
    if not series_data:
        return ({'TOTAL': 0}, '', [])

    common_ids = set(series_data[0][1].keys())
    for _, scores in series_data[1:]:
        common_ids &= set(scores.keys())
    common_ids &= set(total_scores.keys())
    common_ids = sorted(common_ids)

    if len(common_ids) < 2:
        return ({'TOTAL': 0}, '', [])

    # Perplexity must be less than n_samples
    effective_perplexity = min(perplexity, len(common_ids) - 1)

    # Build feature matrix
    n_samples = len(common_ids)
    n_features = len(series_data)
    X = np.zeros((n_samples, n_features))

    for j, (_, scores) in enumerate(series_data):
        for i, sid in enumerate(common_ids):
            X[i, j] = scores.get(sid, 0)

    # Get total scores for coloring
    totals = [total_scores.get(sid, 0) for sid in common_ids]
    normalized_totals = normalize_scores(totals)
    colors = [get_viridis_color(v, theme) for v in normalized_totals]

    t_data_prep = time.perf_counter()

    # Standardize
    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)

    # Fit t-SNE (max_iter was called n_iter in sklearn <1.8)
    tsne = TSNE(
        n_components=2,
        perplexity=effective_perplexity,
        learning_rate=learning_rate,
        max_iter=1000,
        random_state=42
    )
    X_tsne = tsne.fit_transform(X_scaled)

    t_tsne = time.perf_counter()

    # Create figure
    fig, ax = plt.subplots(figsize=fig_size)

    scatter = ax.scatter(X_tsne[:, 0], X_tsne[:, 1],
                        c=colors, s=dot_size**2, alpha=0.7, edgecolors='none')

    ax.set_xlabel('t-SNE 1', fontsize=10)
    ax.set_ylabel('t-SNE 2', fontsize=10)
    ax.set_title(f't-SNE (perplexity={effective_perplexity:.0f}, lr={learning_rate:.0f})',
                fontsize=12, fontweight='bold')

    # Add colorbar
    sm = plt.cm.ScalarMappable(cmap='viridis',
                               norm=plt.Normalize(vmin=min(totals), vmax=max(totals)))
    sm.set_array([])
    cbar = plt.colorbar(sm, ax=ax, shrink=0.8)
    cbar.set_label('Total Score', fontsize=9)

    plt.tight_layout()

    t_rendering = time.perf_counter()

    # Convert data coordinates to display coordinates BEFORE saving
    fig.canvas.draw()
    display_coords = ax.transData.transform(X_tsne)

    # Get the figure dimensions in pixels at current DPI
    fig_dpi = fig.dpi  # Default is 100
    save_dpi = 300  # DPI used when saving SVG
    dpi_scale = save_dpi / fig_dpi

    # SVG viewBox dimensions are scaled by the save DPI
    fig_height_px = fig.get_figheight() * save_dpi

    # Save as SVG
    svg_buffer = io.BytesIO()
    plt.savefig(svg_buffer, format='svg', dpi=save_dpi, transparent=True)
    svg_buffer.seek(0)
    svg_content = svg_buffer.read().decode('utf-8')
    svg_buffer.close()
    plt.close(fig)

    t_svg_save = time.perf_counter()

    # Build point data using display coordinates
    # Scale coordinates from figure DPI to SVG DPI, and flip Y for SVG coordinate system
    point_data_list = []
    for i, sid in enumerate(common_ids):
        point_data_list.append({
            'x': float(display_coords[i, 0] * dpi_scale),
            'y': float(fig_height_px - display_coords[i, 1] * dpi_scale),  # Flip Y for SVG
            'id': sid,
            'total_score': totals[i],
            'color': colors[i]
        })

    t_end = time.perf_counter()

    timing = {
        'Data Preparation': int((t_data_prep - t_start) * 1000),
        't-SNE Computation': int((t_tsne - t_data_prep) * 1000),
        'Rendering': int((t_rendering - t_tsne) * 1000),
        'SVG Conversion': int((t_svg_save - t_rendering) * 1000),
        'TOTAL': int((t_end - t_start) * 1000)
    }

    return (timing, svg_content, point_data_list)
