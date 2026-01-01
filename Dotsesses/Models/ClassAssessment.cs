namespace Dotsesses.Models;

/// <summary>
/// Root class containing all student assessments, grade cutoffs, and curve data.
/// </summary>
public class ClassAssessment
{
    public IReadOnlyCollection<StudentAssessment> Assessments { get; }
    public IReadOnlyCollection<GradeCutoff> CurrentCutoffs { get; set; }
    public IReadOnlyCollection<CutoffCountRange> DefaultCurve { get; }
    public IReadOnlyCollection<CutoffCount> Current { get; set; }
    public Dictionary<string, IReadOnlyCollection<GradeCutoff>> SavedCutoffs { get; }
    public Dictionary<int, MuppetNameInfo> MuppetNameMap { get; }
    public Dictionary<string, string> SeriesColorMap { get; }

    public ClassAssessment(
        IReadOnlyCollection<StudentAssessment> assessments,
        IReadOnlyCollection<GradeCutoff> currentCutoffs,
        IReadOnlyCollection<CutoffCountRange> defaultCurve,
        IReadOnlyCollection<CutoffCount> current,
        Dictionary<int, MuppetNameInfo> muppetNameMap,
        Dictionary<string, string> seriesColorMap)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(currentCutoffs);
        ArgumentNullException.ThrowIfNull(defaultCurve);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(muppetNameMap);
        ArgumentNullException.ThrowIfNull(seriesColorMap);

        Assessments = assessments;
        CurrentCutoffs = currentCutoffs;
        DefaultCurve = defaultCurve;
        Current = current;
        MuppetNameMap = muppetNameMap;
        SeriesColorMap = seriesColorMap;
        SavedCutoffs = new Dictionary<string, IReadOnlyCollection<GradeCutoff>>();
    }
}
