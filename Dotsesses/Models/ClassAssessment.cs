namespace Dotsesses.Models;

/// <summary>
/// Static class data: students, the GradeCurve template, and naming /
/// presentation maps. Live grading state (current cutoffs, current
/// counts, enabled-grades set) lives exclusively on the paired
/// <see cref="Dotsesses.UI.GradingSession"/> per ADR-0008.
/// </summary>
public class ClassAssessment
{
    public IReadOnlyCollection<StudentAssessment> Assessments { get; }
    public IReadOnlyCollection<CutoffCountRange> DefaultCurve { get; }
    public Dictionary<string, IReadOnlyCollection<GradeCutoff>> SavedCutoffs { get; }
    public Dictionary<int, MuppetNameInfo> MuppetNameMap { get; }
    public Dictionary<string, string> SeriesColorMap { get; }
    public IReadOnlyList<ScoreSelection> ScoreSelections { get; set; } = Array.Empty<ScoreSelection>();

    public ClassAssessment(
        IReadOnlyCollection<StudentAssessment> assessments,
        IReadOnlyCollection<CutoffCountRange> defaultCurve,
        Dictionary<int, MuppetNameInfo> muppetNameMap,
        Dictionary<string, string> seriesColorMap)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(defaultCurve);
        ArgumentNullException.ThrowIfNull(muppetNameMap);
        ArgumentNullException.ThrowIfNull(seriesColorMap);

        Assessments = assessments;
        DefaultCurve = defaultCurve;
        MuppetNameMap = muppetNameMap;
        SeriesColorMap = seriesColorMap;
        SavedCutoffs = new Dictionary<string, IReadOnlyCollection<GradeCutoff>>();
    }
}
