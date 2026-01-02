namespace Dotsesses.Models;

/// <summary>
/// Represents a single point in a dimensionality reduction plot (PCA, UMAP, t-SNE).
/// </summary>
public record ProjectionDataPoint(
    double X,
    double Y,
    int StudentId,
    double TotalScore,
    string Color
);
