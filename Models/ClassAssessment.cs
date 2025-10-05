namespace Dotsesses.Models;

/// <summary>
/// Root class containing all student assessments, grade cutoffs, and curve data.
/// </summary>
public class ClassAssessment
{
    public IReadOnlyCollection<StudentAssessment> Assessments { get; }
    public IReadOnlyCollection<GradeCutoff> CurrentCutoffs { get; set; }
    public IReadOnlyCollection<CutoffCount> DefaultCurve { get; }
    public IReadOnlyCollection<CutoffCount> Current { get; set; }
    public Dictionary<string, IReadOnlyCollection<GradeCutoff>> SavedCutoffs { get; }
    public Dictionary<int, MuppetNameInfo> MuppetNameMap { get; }

    public ClassAssessment(
        IReadOnlyCollection<StudentAssessment> assessments,
        IReadOnlyCollection<GradeCutoff> currentCutoffs,
        IReadOnlyCollection<CutoffCount> defaultCurve,
        IReadOnlyCollection<CutoffCount> current,
        Dictionary<int, MuppetNameInfo> muppetNameMap)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        ArgumentNullException.ThrowIfNull(currentCutoffs);
        ArgumentNullException.ThrowIfNull(defaultCurve);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(muppetNameMap);

        Assessments = assessments;
        CurrentCutoffs = currentCutoffs;
        DefaultCurve = defaultCurve;
        Current = current;
        MuppetNameMap = muppetNameMap;
        SavedCutoffs = new Dictionary<string, IReadOnlyCollection<GradeCutoff>>();
    }
}
