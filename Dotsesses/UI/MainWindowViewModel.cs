namespace Dotsesses.UI;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Calculators;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;
using Microsoft.Extensions.DependencyInjection;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;

/// <summary>
/// Main window ViewModel coordinating dotplot, cursors, drill-down, and compliance.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "dotsesses_startup.log");

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    private readonly CutoffCountCalculator _cutoffCountCalculator = null!;
    private readonly InitialCutoffCalculator _initialCutoffCalculator = null!;
    private readonly CursorValidation _cursorValidation = null!;
    private readonly IMessenger _messenger = null!;
    private readonly HoverDelayService _hoverDelayService = null!;
    private readonly StateService _stateService = null!;
    private readonly WorkspaceFactory? _workspaceFactory;
    // Used at load to compute data-driven default Significance selection
    // (ADR-0016). Null in the test factory, which skips that refinement.
    private readonly SignificancePlotService? _significancePlotService;

    /// <summary>
    /// Gets a fresh GradeAssigner built from the session's current
    /// cutoffs. Used by drill-down lookup and export. Constructing
    /// per call is cheap and removes the stale-cache risk that the
    /// cursor-mirror era had.
    /// </summary>
    public GradeAssigner GradeAssigner => new(GradingSession.CurrentState.Cutoffs);

    private CutoffSlot? _draggingCursor;
    private bool _isDraggingCursor;
    private string? _currentSourceFile;

    // Double-click tracking
    private DateTime _lastClickTime;
    private int? _lastClickedStudentId;
    private const int DoubleClickThresholdMs = 500;

    [ObservableProperty]
    private int? _hoveredStudentId;

    [ObservableProperty]
    private StudentCardViewModel? _hoveredStudent;

    [ObservableProperty]
    private ClassAssessment _classAssessment = null!;

    /// <summary>
    /// Live grading state for the loaded Class. Constructed in lockstep
    /// with <see cref="ClassAssessment"/> on every file load (see
    /// ADR-0008). Drag, Compliance, and persistence all flow through this.
    /// </summary>
    [ObservableProperty]
    private GradingSession _gradingSession = null!;

    [ObservableProperty]
    private PlotModel _dotplotModel = null!;

    [ObservableProperty]
    private ComplianceGridViewModel? _complianceGrid;

    [ObservableProperty]
    private bool _isCompliancePaneOpen = true;

    [ObservableProperty]
    private bool _isResizeCursor;

    [ObservableProperty]
    private bool _isDrillDownPaneOpen = true;

    [ObservableProperty]
    private bool _isViolinPaneOpen = true;

    [ObservableProperty]
    private ViolinPlotViewModel? _violinPlotViewModel;

    [ObservableProperty]
    private CorrelationPlotViewModel? _correlationPlotViewModel;

    [ObservableProperty]
    private SignificancePlotViewModel? _significancePlotViewModel;

    [ObservableProperty]
    private PlotTabContainerViewModel? _plotTabContainerViewModel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string? _currentSaveFilePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _sourceFileName = "No file loaded";

    /// <summary>
    /// The full window title — file name plus an unsaved-changes dot
    /// (matching the Save button's indicator). Lets OS task switchers
    /// distinguish multiple open analyses at a glance. See ADR-0012.
    /// </summary>
    public string WindowTitle => HasUnsavedChanges
        ? $"Dotsesses - {SourceFileName} ●"
        : $"Dotsesses - {SourceFileName}";

    /// <summary>
    /// Gets the full path to the current source file.
    /// </summary>
    public string? CurrentSourceFile => _currentSourceFile;

    /// <summary>
    /// Exposes the hover delay service for debug display and clear command binding.
    /// </summary>
    public HoverDelayService HoverDelayService => _hoverDelayService;

    /// <summary>
    /// Exposes the state service for checking last used directory.
    /// </summary>
    public StateService StateService => _stateService;

    /// <summary>
    /// The scoped messenger that belongs to this VM's workspace. View
    /// code-behind reaches it through <c>DataContext</c> to register
    /// for messages and to broadcast within the same window's scope.
    /// See ADR-0012.
    /// </summary>
    public IMessenger Messenger => _messenger;

    /// <summary>
    /// Re-renders dotplot annotations from the session's current state.
    /// Invoked whenever the session emits a LastChange notification
    /// (drag commit, enable/disable, reseed, load). Compliance counts
    /// flow through <see cref="ComplianceGridViewModel"/>'s own
    /// subscription, not this method.
    /// </summary>
    private void OnSessionStateChanged()
    {
        if (GradingSession is null) return;
        if (DotplotModel is null) return; // Test factory path

        UpdateCursors();
        HasUnsavedChanges = true;
    }

    partial void OnGradingSessionChanged(GradingSession? oldValue, GradingSession newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnGradingSessionPropertyChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnGradingSessionPropertyChanged;
        }

        if (ViolinPlotViewModel is not null)
        {
            ViolinPlotViewModel.GradingSession = newValue;
        }
    }

    private void OnGradingSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GradingSession.LastChange)) return;
        OnSessionStateChanged();
    }

    public MainWindowViewModel()
    {
        
    }
    
    public MainWindowViewModel(
        IMessenger messenger,
        ViolinPlotViewModel violinPlotViewModel,
        CorrelationPlotViewModel correlationPlotViewModel,
        SignificancePlotViewModel significancePlotViewModel,
        HoverDelayService hoverDelayService,
        StateService stateService,
        WorkspaceFactory workspaceFactory,
        SignificancePlotService significancePlotService)
    {
        Log("MainWindowViewModel: Constructor started");

        _messenger = messenger;
        _violinPlotViewModel = violinPlotViewModel;
        _correlationPlotViewModel = correlationPlotViewModel;
        _significancePlotViewModel = significancePlotViewModel;
        // null in the test factory; guarded so CreateForTesting stays valid.
        if (significancePlotViewModel != null)
        {
            significancePlotViewModel.OptimizeRequested = OnOptimizeSignificance;
        }
        _hoverDelayService = hoverDelayService;
        _stateService = stateService;
        _workspaceFactory = workspaceFactory;
        _significancePlotService = significancePlotService;

        // Create the tab container ViewModel
        _plotTabContainerViewModel = new PlotTabContainerViewModel(violinPlotViewModel, correlationPlotViewModel, significancePlotViewModel);

        Log("MainWindowViewModel: Creating calculators");
        _cutoffCountCalculator = new CutoffCountCalculator();
        _initialCutoffCalculator = new InitialCutoffCalculator();
        _cursorValidation = new CursorValidation();

        Log("MainWindowViewModel: Registering message handlers");
        // Subscribe to hover activation from delay service
        _hoverDelayService.OnHoverActivated += OnHoverActivated;

        // Register for hover messages from violin plot (for cross-view sync)
        _messenger.Register<StudentHoverMessage>(this, (r, m) =>
        {
            // Always respond to clear messages (null), only filter non-self sources for hover
            if (m.StudentId == null || m.Source != "dotplot")
            {
                HoveredStudentId = m.StudentId;
            }
        });

        // Register for student edited messages to refresh comment displays
        _messenger.Register<StudentEditedMessage>(this, (r, m) =>
        {
            UpdateDotplotPoints();
            ViolinPlotViewModel?.RefreshHoverVisualization();
            HasUnsavedChanges = true;
        });

        Log("MainWindowViewModel: Constructor completed (data loading deferred)");
    }

    /// <summary>
    /// Test factory: constructs a MainWindowViewModel with null plot view-models and immediately loads
    /// the supplied Excel fixture so ClassAssessment is non-null. The violin/correlation init paths
    /// guard against null plot VMs, so this yields a working unit-test target without requiring the
    /// full Avalonia/OxyPlot UI graph. Production callers must use the parameterized constructor.
    /// </summary>
    /// <param name="excelFilePath">Absolute path to an .xlsx fixture (e.g. example/IP exam scores 2025.xlsx).</param>
    public static MainWindowViewModel CreateForTesting(string excelFilePath)
    {
        var vm = CreateForTesting();
        vm.LoadFromExcelFile(excelFilePath);
        return vm;
    }

    /// <summary>
    /// Test factory overload that builds a fresh, unloaded VM. The caller is responsible for
    /// invoking <see cref="LoadFromExcelFile"/> or the LoadStateAsync command. Useful for tests
    /// that need to load a v1/.dots file directly on a clean VM without first loading xlsx
    /// (loading twice would double-add cursors and compliance rows because neither path clears
    /// those collections — that is a separate pre-existing bug).
    /// </summary>
    public static MainWindowViewModel CreateForTesting()
    {
        return new MainWindowViewModel(
            new WeakReferenceMessenger(),
            null!,
            null!,
            null!,
            new HoverDelayService(),
            new StateService(),
            null!,
            null!);
    }

    /// <summary>
    /// Loads student data from an Excel file and initializes all UI components.
    /// </summary>
    /// <param name="excelFilePath">Path to the Excel file containing scores</param>
    public void LoadFromExcelFile(string excelFilePath)
    {
        Log($"MainWindowViewModel: Loading from Excel file: {excelFilePath}");

        _currentSourceFile = excelFilePath;
        _stateService.LastUsedDirectory = Path.GetDirectoryName(excelFilePath);

        var scoreReader = new ScoreReader();
        var readResult = scoreReader.Read(excelFilePath);
        var students = readResult.Students;

        var curveGenerator = new DefaultCurveGenerator();
        var defaultCurve = curveGenerator.GenerateRanges(students.Count);

        var muppetNameGenerator = new MuppetNameGenerator();
        var studentIds = students.Select(s => s.Id).OrderBy(id => id);
        var muppetNameMap = muppetNameGenerator.Generate(studentIds);

        // Generate series color map from first student's scores
        var firstStudent = students.First();
        var seriesNames = firstStudent.Scores
            .Select(s => s.Index.HasValue ? $"{s.Name} {s.Index}" : s.Name)
            .ToList();
        var seriesColorMap = SeriesColorService.GenerateColorMap(seriesNames);

        ClassAssessment = new ClassAssessment(
            students,
            defaultCurve,
            muppetNameMap,
            seriesColorMap
        );

        GradingSession = new GradingSession(
            ClassAssessment,
            new CursorPlacementCalculator(),
            _cursorValidation,
            _cutoffCountCalculator,
            _initialCutoffCalculator);

        ComplianceGrid = new ComplianceGridViewModel(ClassAssessment, GradingSession);

        SeedDefaultSelectionsIfEmpty();

        // SeedCursorsFromDefaults reseeds the session and rewires the
        // violin plot. ApplyScoreSelections reuses it for an
        // aggregate-set change.
        Log("MainWindowViewModel: Seeding cursors from default curve");
        SeedCursorsFromDefaults();

        Log("MainWindowViewModel: Initializing dotplot");
        InitializeDotplot();

        // Update the display filename
        SourceFileName = Path.GetFileName(excelFilePath);

        Log("MainWindowViewModel: Excel file loaded successfully");

        // Surface any non-fatal reader warnings (duplicate columns,
        // sparse / constant series, skipped rows, etc.) once the load
        // is otherwise complete. The View renders one combined dialog.
        if (readResult.Warnings.Count > 0 && _messenger != null)
        {
            Log($"MainWindowViewModel: Load surfaced {readResult.Warnings.Count} warning(s)");
            _messenger.Send(new Messages.LoadWarningsMessage(readResult.Warnings));
        }
    }

    /// <summary>
    /// Compute the filtered score list shown in the drill-down panel for one student.
    /// Honors <see cref="ClassAssessment.ScoreSelections"/>'s Display flag using the same
    /// (Name, Index?) tuple HashSet pattern as <see cref="BuildSeriesData"/>. When the
    /// selection list is empty (not yet seeded), passes through the full score list
    /// unchanged so behavior matches pre-S04.
    /// </summary>
    private List<Score> BuildDisplayScores(StudentAssessment student)
    {
        ArgumentNullException.ThrowIfNull(student);
        if (ClassAssessment.ScoreSelections.Count == 0)
        {
            return student.Scores.ToList();
        }
        var displaySet = ClassAssessment.ScoreSelections
            .Where(s => s.Display)
            .Select(s => (s.Name, s.Index))
            .ToHashSet();
        return student.Scores
            .Where(s => displaySet.Contains((s.Name, s.Index)))
            .ToList();
    }

    /// <summary>
    /// Build the (SeriesName, Scores) seriesData payload shared by the violin and correlation plot
    /// initializers, filtered by a <see cref="ScoreSelection"/> predicate (typically <c>s =&gt; s.Display</c>
    /// or <c>s =&gt; s.Correlation</c>). Public + static so it can be unit-tested without spinning up the
    /// async background thread that wraps it. Defensive empty-selection-set fallback: if
    /// <see cref="ClassAssessment.ScoreSelections"/> is empty, every score on the first student passes
    /// through unchanged — this preserves pre-S04 behavior on datasets that have not been seeded.
    /// </summary>
    /// <summary>
    /// Builds the (Name, {studentId → subgroup}) series payload for the Significance
    /// Matrix's categorical axis. Iterates each <see cref="StudentAssessment.Attributes"/>
    /// list, filters by the supplied selector (typically
    /// <c>s =&gt; s.Significance &amp;&amp; s.Type == ScoreColumnType.Categorical</c>).
    /// Student IDs are formatted as "S001" / "S002" / … to match the
    /// <see cref="BuildSeriesData"/> convention so the two builders can share
    /// downstream join logic.
    /// </summary>
    public static List<(string SeriesName, Dictionary<string, string> Subgroups)> BuildCategoricalSeriesData(
        ClassAssessment classAssessment,
        Func<ScoreSelection, bool> selector)
    {
        ArgumentNullException.ThrowIfNull(classAssessment);
        ArgumentNullException.ThrowIfNull(selector);

        var hasSelections = classAssessment.ScoreSelections.Count > 0;
        var keySet = classAssessment.ScoreSelections
            .Where(selector)
            .Select(s => (s.Name, s.Index))
            .ToHashSet();

        var result = new List<(string SeriesName, Dictionary<string, string> Subgroups)>();
        if (!classAssessment.Assessments.Any()) return result;

        // Categorical column identity = (Name, Index). Walk Attributes lists from
        // every student to collect the columns. The first student's Attributes list
        // is the canonical column order — newer slice-2 conversions append, so
        // first-student-iteration is consistent with the user's import/conversion
        // order.
        var firstStudent = classAssessment.Assessments.First();
        foreach (var attr in firstStudent.Attributes)
        {
            if (hasSelections && !keySet.Contains((attr.Name, attr.Index))) continue;
            var seriesName = attr.Index.HasValue ? $"{attr.Name} {attr.Index}" : attr.Name;
            var subgroups = new Dictionary<string, string>();
            foreach (var assessment in classAssessment.Assessments)
            {
                var studentAttr = assessment.Attributes.FirstOrDefault(a =>
                    a.Name == attr.Name && a.Index == attr.Index);
                if (studentAttr != null)
                {
                    subgroups[$"S{assessment.Id:D3}"] = studentAttr.Value;
                }
            }
            result.Add((seriesName, subgroups));
        }
        return result;
    }

    /// <summary>
    /// Builds the canonical subgroup order per categorical/ordinal column for the
    /// Significance Matrix (ADR-0017): suffixed labels by their <c>~N</c> rank,
    /// then unsuffixed alphabetical. Aligned by series name with
    /// <see cref="BuildCategoricalSeriesData"/> (same selector, same iteration),
    /// so the renderer can match column j's order to column j's data.
    /// </summary>
    public static List<(string SeriesName, List<string> OrderedSubgroups)> BuildSubgroupOrders(
        ClassAssessment classAssessment,
        Func<ScoreSelection, bool> selector)
    {
        ArgumentNullException.ThrowIfNull(classAssessment);
        ArgumentNullException.ThrowIfNull(selector);

        var hasSelections = classAssessment.ScoreSelections.Count > 0;
        var keySet = classAssessment.ScoreSelections
            .Where(selector)
            .Select(s => (s.Name, s.Index))
            .ToHashSet();

        var result = new List<(string SeriesName, List<string> OrderedSubgroups)>();
        if (!classAssessment.Assessments.Any()) return result;

        var firstStudent = classAssessment.Assessments.First();
        foreach (var attr in firstStudent.Attributes)
        {
            if (hasSelections && !keySet.Contains((attr.Name, attr.Index))) continue;
            var seriesName = attr.Index.HasValue ? $"{attr.Name} {attr.Index}" : attr.Name;
            var observations = new List<(string Label, int? SortOrder)>();
            foreach (var assessment in classAssessment.Assessments)
            {
                var studentAttr = assessment.Attributes.FirstOrDefault(a =>
                    a.Name == attr.Name && a.Index == attr.Index);
                if (studentAttr != null)
                {
                    observations.Add((studentAttr.Value, studentAttr.SortOrder));
                }
            }
            result.Add((seriesName, Calculators.SubgroupOrderCalculator.Order(observations)));
        }
        return result;
    }

    public static List<(string SeriesName, Dictionary<string, double> Scores)> BuildSeriesData(
        ClassAssessment classAssessment,
        Func<ScoreSelection, bool> selector)
    {
        ArgumentNullException.ThrowIfNull(classAssessment);
        ArgumentNullException.ThrowIfNull(selector);

        var hasSelections = classAssessment.ScoreSelections.Count > 0;
        var keySet = classAssessment.ScoreSelections
            .Where(selector)
            .Select(s => (s.Name, s.Index))
            .ToHashSet();

        var result = new List<(string SeriesName, Dictionary<string, double> Scores)>();
        var firstStudent = classAssessment.Assessments.First();

        foreach (var score in firstStudent.Scores)
        {
            // Defensive empty-selection-set fallback: if no selections exist, pass through unchanged.
            if (hasSelections && !keySet.Contains((score.Name, score.Index))) continue;

            var seriesName = score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;
            var seriesScores = new Dictionary<string, double>();
            foreach (var assessment in classAssessment.Assessments)
            {
                var studentScore = assessment.Scores.FirstOrDefault(s =>
                    s.Name == score.Name && s.Index == score.Index);
                if (studentScore != null)
                {
                    seriesScores[$"S{assessment.Id:D3}"] = studentScore.Value;
                }
            }
            result.Add((seriesName, seriesScores));
        }

        // Ordinal columns (ADR-0017) are numeric-capable: their data lives in
        // Attributes but SortOrder (N) is the value, so they can appear in the
        // violin / correlation views like a Score. Only emit those whose
        // (Name, Index) is in the selector-derived key set AND typed Ordinal —
        // a plain Categorical attribute has no numeric value and is skipped.
        var ordinalKeys = classAssessment.ScoreSelections
            .Where(s => s.Type == ScoreColumnType.Ordinal)
            .Where(s => !hasSelections || keySet.Contains((s.Name, s.Index)))
            .Select(s => (s.Name, s.Index))
            .ToHashSet();
        if (ordinalKeys.Count > 0)
        {
            foreach (var attr in firstStudent.Attributes)
            {
                if (!ordinalKeys.Contains((attr.Name, attr.Index))) continue;
                var seriesName = attr.Index.HasValue ? $"{attr.Name} {attr.Index}" : attr.Name;
                var seriesScores = new Dictionary<string, double>();
                foreach (var assessment in classAssessment.Assessments)
                {
                    var studentAttr = assessment.Attributes.FirstOrDefault(a =>
                        a.Name == attr.Name && a.Index == attr.Index);
                    if (studentAttr?.SortOrder is int n)
                    {
                        seriesScores[$"S{assessment.Id:D3}"] = n;
                    }
                }
                result.Add((seriesName, seriesScores));
            }
        }
        return result;
    }

    /// <summary>
    /// Builds per-series metadata for the correlation matrix, keyed by the same
    /// formatted series name <see cref="BuildSeriesData"/> emits (so lookups are
    /// order-independent). Each entry records the column's
    /// <see cref="ScoreColumnType"/>, whether it should be rest-score de-biased
    /// against Total, and whether it is Total — letting the Python renderer key
    /// behavior off explicit flags instead of series position (ADR-0018).
    ///
    /// Total = a Score named "Total" (case-insensitive) with no Index. De-bias is
    /// gated on the explicit <see cref="ScoreSelection.BiasCorrect"/> flag (Numeric,
    /// non-Total), decoupled from aggregate membership so a composite column can be
    /// corrected without being summed into the aggregate.
    /// </summary>
    public static Dictionary<string, CorrelationColumnInfo> BuildCorrelationColumnMetadata(
        ClassAssessment classAssessment,
        Func<ScoreSelection, bool> selector)
    {
        ArgumentNullException.ThrowIfNull(classAssessment);
        ArgumentNullException.ThrowIfNull(selector);

        var map = new Dictionary<string, CorrelationColumnInfo>();
        foreach (var s in classAssessment.ScoreSelections.Where(selector))
        {
            var seriesName = s.Index.HasValue ? $"{s.Name} {s.Index}" : s.Name;
            var isTotal = s.Index == null &&
                string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase);
            // De-bias is driven by the explicit BiasCorrect flag, not aggregate
            // membership (ADR-0018). Guard to Numeric non-Total defensively — the
            // Settings UI already only lets those rows carry the flag.
            var biasCorrect =
                s.Type == ScoreColumnType.Numeric && s.BiasCorrect && !isTotal;
            map[seriesName] = new CorrelationColumnInfo(s.Type, biasCorrect, isTotal);
        }
        return map;
    }

    /// <summary>
    /// Builds the hover-label map for Ordinal series in the violin plot
    /// (ADR-0017): keyed by (studentId, seriesName), the value is the stripped
    /// categorical label (e.g. "✔✔+") so the tooltip can show it instead of the
    /// numeric SortOrder. Only Ordinal columns matching the selector contribute;
    /// numeric Scores have no entry (their tooltip keeps showing the number).
    /// </summary>
    public static Dictionary<(int StudentId, string SeriesName), string> BuildOrdinalLabelMap(
        ClassAssessment classAssessment,
        Func<ScoreSelection, bool> selector)
    {
        ArgumentNullException.ThrowIfNull(classAssessment);
        ArgumentNullException.ThrowIfNull(selector);

        var map = new Dictionary<(int StudentId, string SeriesName), string>();
        var ordinalKeys = classAssessment.ScoreSelections
            .Where(s => s.Type == ScoreColumnType.Ordinal && selector(s))
            .Select(s => (s.Name, s.Index))
            .ToHashSet();
        if (ordinalKeys.Count == 0) return map;

        foreach (var assessment in classAssessment.Assessments)
        {
            foreach (var attr in assessment.Attributes)
            {
                if (!ordinalKeys.Contains((attr.Name, attr.Index))) continue;
                var seriesName = attr.Index.HasValue ? $"{attr.Name} {attr.Index}" : attr.Name;
                map[(assessment.Id, seriesName)] = attr.Value;
            }
        }
        return map;
    }

    /// <summary>
    /// Initializes the violin plot asynchronously after the UI is loaded.
    /// Call this from MainWindow.Loaded event to avoid blocking startup.
    /// </summary>
    public void InitializeViolinPlotAsync()
    {
        Log("MainWindowViewModel: Starting async violin plot initialization");
        Task.Run(async () =>
        {
            try
            {
                Log("MainWindowViewModel: Calling InitializeViolinPlot on background thread");

                // The actual violin plot generation can happen on background thread,
                // but we need to prepare the data first
                if (ViolinPlotViewModel == null)
                {
                    Log("MainWindowViewModel: ViolinPlotViewModel is null, skipping");
                    return;
                }

                // Transform student assessment data into violin plot series format (CPU work, can be on background thread).
                // Filter by Display selection so toggling a Display checkbox in Settings hides the series after Apply.
                var seriesData = BuildSeriesData(ClassAssessment, s => s.Display);

                // Build comment map: (student ID, series name) -> score comment
                var commentMap = new Dictionary<(int StudentId, string SeriesName), string>();
                foreach (var assessment in ClassAssessment.Assessments)
                {
                    foreach (var score in assessment.Scores)
                    {
                        var seriesName = score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;
                        if (!string.IsNullOrEmpty(score.Comment))
                        {
                            commentMap[(assessment.Id, seriesName)] = score.Comment;
                        }
                    }
                }

                // Build muppet name map: student ID -> muppet name
                var muppetNameMap = ClassAssessment.MuppetNameMap
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);

                // Ordinal series carry a label for hover (✔✔+ rather than 3); ADR-0017.
                var ordinalLabelMap = BuildOrdinalLabelMap(ClassAssessment, s => s.Display);

                // Now update the ViewModel on the UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ViolinPlotViewModel.SetClassAssessment(ClassAssessment);
                    ViolinPlotViewModel.UpdateDataAndRegenerate(seriesData, commentMap, muppetNameMap, 3.0, ordinalLabelMap);
                });

                Log("MainWindowViewModel: Violin plot initialization completed");
            }
            catch (Exception ex)
            {
                // Without this catch, a Python-side throw (e.g. seaborn KDE choking on
                // a constant-value series) becomes an unhandled task exception and
                // aborts the process. Surface it instead.
                Log($"MainWindowViewModel: Violin plot init failed: {ex}");
                _messenger.Send(new Messages.PlotInitErrorMessage("Violin plot", ex.Message));
            }
        });
    }

    /// <summary>
    /// Initializes the correlation plot asynchronously after the UI is loaded.
    /// Call this from MainWindow.Loaded event to avoid blocking startup.
    /// </summary>
    public void InitializeCorrelationPlotAsync()
    {
        Log("MainWindowViewModel: Starting async correlation plot initialization");
        Task.Run(async () =>
        {
            try
            {
                Log("MainWindowViewModel: Calling InitializeCorrelationPlot on background thread");

                if (CorrelationPlotViewModel == null)
                {
                    Log("MainWindowViewModel: CorrelationPlotViewModel is null, skipping");
                    return;
                }

                // Transform student assessment data into correlation plot series format (CPU work).
                // Filter by Correlation selection so toggling a Correlation checkbox hides the row/column after Apply.
                var seriesData = BuildSeriesData(ClassAssessment, s => s.Correlation);
                // Per-series column metadata (type / aggregate-component / Total
                // flag) so Python keys behavior off explicit flags, not position
                // (ADR-0018 slice 1).
                var columnMetadata = BuildCorrelationColumnMetadata(ClassAssessment, s => s.Correlation);

                // Build muppet name map: student ID -> muppet name
                var muppetNameMap = ClassAssessment.MuppetNameMap
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);

                // Update ViewModel on UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CorrelationPlotViewModel.UpdateDataAndRegenerate(seriesData, columnMetadata, muppetNameMap, 3.0);
                });

                Log("MainWindowViewModel: Correlation plot initialization completed");
            }
            catch (Exception ex)
            {
                Log($"MainWindowViewModel: Correlation plot init failed: {ex}");
                _messenger.Send(new Messages.PlotInitErrorMessage("Correlation matrix", ex.Message));
            }
        });
    }

    /// <summary>
    /// Initializes the Significance Matrix plot asynchronously after the UI is loaded
    /// (mirrors <see cref="InitializeViolinPlotAsync"/> / <see cref="InitializeCorrelationPlotAsync"/>).
    /// Numeric rows AND Categorical columns are independently filtered by
    /// <see cref="ScoreSelection.Significance"/>; an empty filter produces an empty matrix
    /// (see ADR-0014).
    /// </summary>
    public void InitializeSignificanceMatrixAsync()
    {
        Log("MainWindowViewModel: Starting async Significance Matrix initialization");
        Task.Run(async () =>
        {
            try
            {
                if (SignificancePlotViewModel == null)
                {
                    Log("MainWindowViewModel: SignificancePlotViewModel is null, skipping");
                    return;
                }

                var numericSeries = BuildSeriesData(ClassAssessment,
                    s => s.Significance && s.Type == ScoreColumnType.Numeric);
                // Ordinal columns join the matrix as categorical columns
                // (ADR-0017) — their labels are Subgroups; SortOrder drives order.
                bool isSigCategorical(ScoreSelection s) => s.Significance &&
                    (s.Type == ScoreColumnType.Categorical || s.Type == ScoreColumnType.Ordinal);
                var categoricalSeries = BuildCategoricalSeriesData(ClassAssessment, isSigCategorical);
                var subgroupOrders = BuildSubgroupOrders(ClassAssessment, isSigCategorical);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SignificancePlotViewModel.UpdateDataAndRegenerate(numericSeries, categoricalSeries, 5.0, subgroupOrders);
                });

                Log("MainWindowViewModel: Significance Matrix initialization completed");
            }
            catch (Exception ex)
            {
                Log($"MainWindowViewModel: Significance Matrix init failed: {ex}");
                _messenger.Send(new Messages.PlotInitErrorMessage("Significance matrix", ex.Message));
            }
        });
    }

    private void InitializeDotplot()
    {
        // Use transparent background so Avalonia's theme RegionColor shows through
        DotplotModel = new PlotModel
        {
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent,
            PlotAreaBorderThickness = new OxyThickness(0, 1, 0, 1), // Top and bottom only
            PlotAreaBorderColor = OxyColor.FromRgb(60, 60, 60),
            Padding = new OxyThickness(0),
            PlotMargins = new OxyThickness(0)
        };

        // Enable mouse events for point selection and cursor dragging
        DotplotModel.MouseDown += OnDotplotMouseDown;
        DotplotModel.MouseMove += OnDotplotMouseMove;
        DotplotModel.MouseUp += OnDotplotMouseUp;
        
        // Hook up to updated event to maintain fixed heights
        DotplotModel.Updated += (s, e) => UpdateAxisPositions();

        // Three-part layout with positioning (0=bottom, 1=top in OxyPlot)
        // Grade Cursors: bottom 25%
        // Dot Display: middle 60%
        // Statistics Display: top 15%

        double cursorStart = 0.0;
        double cursorEnd = 0.25;
        double dotStart = 0.25;
        double dotEnd = 0.80;
        double statsStart = 0.80;
        double statsEnd = 1.0;

        // ===== Shared X-Axis (hidden, spans all three areas) =====
        // Bounds set below via RefreshDotplotAxisBounds so that the same logic
        // re-applies whenever the aggregate selection changes.
        var sharedXAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Key = "SharedX",
            AxislineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = OxyColors.Transparent,
            StartPosition = 0,
            EndPosition = 1,
            MinimumPadding = 0,
            MaximumPadding = 0
        };
        DotplotModel.Axes.Add(sharedXAxis);

        // ===== Statistics Display Y-Axis (top area) =====
        var statsYAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "StatsY",
            Minimum = -0.35,
            Maximum = 1.35,
            AxislineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = OxyColors.Transparent,
            StartPosition = statsStart,
            EndPosition = statsEnd,
            MinimumPadding = 0,
            MaximumPadding = 0
        };
        DotplotModel.Axes.Add(statsYAxis);

        // ===== Dot Display Y-Axis (middle area) =====
        // Maximum set below via RefreshDotplotAxisBounds (depends on the
        // current AggregateGrade distribution's bin density).
        var dotYAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "DotY",
            Minimum = -2,
            AxislineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = OxyColors.Transparent,
            StartPosition = dotStart,
            EndPosition = dotEnd,
            MinimumPadding = 0,
            MaximumPadding = 0
        };
        DotplotModel.Axes.Add(dotYAxis);

        // ===== Grade Cursors Y-Axis (bottom area) =====
        var cursorYAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "CursorY",
            Minimum = 0,
            Maximum = 1,
            AxislineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = OxyColors.Transparent,
            StartPosition = cursorStart,
            EndPosition = cursorEnd,
            MinimumPadding = 0,
            MaximumPadding = 0
        };
        DotplotModel.Axes.Add(cursorYAxis);

        RefreshDotplotAxisBounds();
        UpdateDotplotPoints();
        UpdateStatistics();
        UpdateCursors();
    }

    /// <summary>
    /// Re-derive the SharedX and DotY axis bounds from the current
    /// AggregateGrade distribution. Called on initial dotplot setup and
    /// every time the aggregate composition changes — without this,
    /// changing the aggregate set leaves the X axis pinned to the old
    /// range and dots silently fall off the visible plot area.
    /// </summary>
    private void RefreshDotplotAxisBounds()
    {
        var sharedXAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "SharedX");
        var dotYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "DotY");
        if (sharedXAxis == null || dotYAxis == null) return;

        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var scoreRange = maxScore - minScore;
        // Degenerate case (empty aggregate set, or every student tied):
        // a zero-width axis renders nothing, so widen by a fixed unit.
        var xPadding = scoreRange > 0 ? scoreRange * 0.2 : 1.0;
        sharedXAxis.Minimum = minScore - xPadding;
        sharedXAxis.Maximum = maxScore + xPadding;

        var maxStudentsInBin = ClassAssessment.Assessments
            .GroupBy(a => a.AggregateGrade)
            .Max(g => g.Count());
        var yMax = (maxStudentsInBin - 1) * 2;
        dotYAxis.Maximum = yMax + 2;
    }


    private void InitializeViolinPlot()
    {
        if (ViolinPlotViewModel == null) return;

        // Transform student assessment data into violin plot series format
        var seriesData = new List<(string SeriesName, Dictionary<string, double> Scores)>();

        // Get all unique score types from the first student (all students have the same score types)
        var firstStudent = ClassAssessment.Assessments.First();
        var scoreTypes = firstStudent.Scores.Select(s =>
            s.Index.HasValue ? $"{s.Name} {s.Index}" : s.Name).ToList();

        // Create a series for each score type
        foreach (var score in firstStudent.Scores)
        {
            var seriesName = score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;
            var seriesScores = new Dictionary<string, double>();

            foreach (var assessment in ClassAssessment.Assessments)
            {
                // Find the matching score for this student
                var studentScore = assessment.Scores.FirstOrDefault(s =>
                    s.Name == score.Name && s.Index == score.Index);

                if (studentScore != null)
                {
                    seriesScores[$"S{assessment.Id:D3}"] = studentScore.Value;
                }
            }

            seriesData.Add((seriesName, seriesScores));
        }

        // Build comment map: (student ID, series name) -> score comment
        var commentMap = new Dictionary<(int StudentId, string SeriesName), string>();
        foreach (var assessment in ClassAssessment.Assessments)
        {
            foreach (var score in assessment.Scores)
            {
                var seriesName = score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;
                if (!string.IsNullOrEmpty(score.Comment))
                {
                    commentMap[(assessment.Id, seriesName)] = score.Comment;
                }
            }
        }

        // Build muppet name map: student ID -> muppet name
        var muppetNameMap = ClassAssessment.MuppetNameMap
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);

        // Update data and regenerate with stored display dimensions
        ViolinPlotViewModel.UpdateDataAndRegenerate(seriesData, commentMap, muppetNameMap, 3.0);
    }

    public void UpdateDotplotPoints()
    {
        // Re-derive axis bounds so dots stay on-screen when the aggregate
        // selection changes (otherwise the X axis stays pinned to the
        // initial range and the new dots fall off the plot area).
        RefreshDotplotAxisBounds();

        // Clear existing series (keep axes)
        DotplotModel.Series.Clear();

        // Calculate mean and standard deviation for sigma display
        var scores = ClassAssessment.Assessments.Select(a => (double)a.AggregateGrade).ToList();
        var mean = scores.Average();
        var stdDev = Math.Sqrt(scores.Average(s => Math.Pow(s - mean, 2)));

        // Group students by aggregate score and stack vertically
        var scoreGroups = ClassAssessment.Assessments
            .GroupBy(a => a.AggregateGrade)
            .OrderBy(g => g.Key);

        // Fixed dot size (50% larger than original 2.0)
        const double markerSize = 3.0;

        // Red dots to match violin plot Total series - separate series for circles and squares
        var totalRed = OxyColor.Parse("#FF3333"); // Match violin plot Total series color
        var dotSeriesCircle = new ScatterSeries
        {
            MarkerType = MarkerType.Circle,
            MarkerSize = markerSize,
            MarkerFill = totalRed,
            MarkerStroke = totalRed,
            MarkerStrokeThickness = 0.5,
            XAxisKey = "SharedX",
            YAxisKey = "DotY",
            TrackerFormatString = ""
        };

        var dotSeriesSquare = new ScatterSeries
        {
            MarkerType = MarkerType.Square,
            MarkerSize = markerSize,
            MarkerFill = OxyColors.Transparent,
            MarkerStroke = totalRed,
            MarkerStrokeThickness = 1.5,
            XAxisKey = "SharedX",
            YAxisKey = "DotY",
            TrackerFormatString = ""
        };

        foreach (var group in scoreGroups)
        {
            var studentsAtScore = group.OrderBy(s => s.Id).ToList();
            var binOffset = group.Key % 2 == 1 ? 0.1 : 0.0;

            for (int i = 0; i < studentsAtScore.Count; i++)
            {
                double yPos = i * 2 + binOffset;
                var student = studentsAtScore[i];
                var muppetName = ClassAssessment.MuppetNameMap.TryGetValue(student.Id, out var info) ? info.Name : "Unknown";

                // Calculate delta from mean in sigma units
                var sigmaValue = (student.AggregateGrade - mean) / stdDev;
                var sigmaSign = sigmaValue >= 0 ? "+" : "";

                var point = new ScatterPoint(group.Key, yPos, tag: $"{muppetName}\nScore: {student.AggregateGrade} ({sigmaSign}{sigmaValue:F1}σ)");

                // Add to appropriate series based on whether the Total score has a comment
                var totalScore = student.Scores.FirstOrDefault(s => s.Name.Equals("Total", StringComparison.OrdinalIgnoreCase));
                bool hasComment = totalScore != null && !string.IsNullOrEmpty(totalScore.Comment);
                if (hasComment)
                {
                    dotSeriesSquare.Points.Add(point);
                }
                else
                {
                    dotSeriesCircle.Points.Add(point);
                }
            }
        }

        // Add main dots (circles then squares)
        DotplotModel.Series.Add(dotSeriesCircle);
        DotplotModel.Series.Add(dotSeriesSquare);

        DotplotModel.InvalidatePlot(true);
    }

    private void UpdateCursors()
    {
        // Clear only cursor-related annotations (keep statistics)
        var statsAnnotations = DotplotModel.Annotations
            .Where(a => a.YAxisKey == "StatsY")
            .ToList();

        DotplotModel.Annotations.Clear();

        foreach (var ann in statsAnnotations)
        {
            DotplotModel.Annotations.Add(ann);
        }

        // Slots structurally exclude the catch-all (ADR-0011).
        var enabledSlots = GradingSession.Slots
            .Where(s => s.IsEnabled)
            .OrderBy(s => s.Score)
            .ToList();
        var minRawScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxRawScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var scoreRange = maxRawScore - minRawScore;
        var xPadding = scoreRange * 0.2;
        var minScore = minRawScore - xPadding;
        var maxScore = maxRawScore + xPadding;

        // Get axis positions for proper rendering
        var dotYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "DotY");
        var cursorYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "CursorY");

        if (dotYAxis == null || cursorYAxis == null) return;

        // ===== Top and bottom borders for Cursor Area =====
        var cursorTopLine = new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = 1,
            MinimumX = minScore,
            MaximumX = maxScore,
            Color = OxyColor.FromRgb(60, 60, 60),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1,
            XAxisKey = "SharedX",
            YAxisKey = "CursorY",
            Layer = AnnotationLayer.BelowSeries
        };
        DotplotModel.Annotations.Add(cursorTopLine);

        var cursorBottomLine = new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = 0,
            MinimumX = minScore,
            MaximumX = maxScore,
            Color = OxyColor.FromRgb(60, 60, 60),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1,
            XAxisKey = "SharedX",
            YAxisKey = "CursorY",
            Layer = AnnotationLayer.BelowSeries
        };
        DotplotModel.Annotations.Add(cursorBottomLine);

        // ===== Vertical Cursor Lines with Square Handles =====
        // Every enabled slot gets a cursor line — Slots already excludes
        // the structural catch-all, so no lowest-Order filter is needed.
        foreach (var slot in enabledSlots)
        {
            // Thin vertical line in the Dot area (behind dots)
            var line = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = slot.Score,
                Color = OxyColor.FromAColor(128, OxyColors.White), // 50% transparency
                LineStyle = LineStyle.Solid,
                StrokeThickness = 1,
                XAxisKey = "SharedX",
                YAxisKey = "DotY",
                MinimumY = dotYAxis.Minimum,
                MaximumY = dotYAxis.Maximum,
                Layer = AnnotationLayer.BelowSeries
            };
            DotplotModel.Annotations.Add(line);

            // Extend line into cursor area (from top of cursor area down to handle)
            var cursorLine = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = slot.Score,
                Color = OxyColor.FromAColor(128, OxyColors.White), // 50% transparency
                LineStyle = LineStyle.Solid,
                StrokeThickness = 1,
                XAxisKey = "SharedX",
                YAxisKey = "CursorY",
                MinimumY = 0.5, // Stop at handle center
                MaximumY = cursorYAxis.Maximum
            };
            DotplotModel.Annotations.Add(cursorLine);

            // Square handle at bottom of cursor area using PointAnnotation (fixed screen-space size), hollow
            var handle = new PointAnnotation
            {
                X = slot.Score,
                Y = 0.5, // Center of cursor area
                Size = 3, // Screen pixels
                Shape = MarkerType.Square,
                Fill = OxyColors.Black,
                Stroke = OxyColors.White,
                StrokeThickness = 2,
                XAxisKey = "SharedX",
                YAxisKey = "CursorY"
            };
            DotplotModel.Annotations.Add(handle);
        }

        // ===== Grade Labels Below Cursors =====
        // Labels include the catch-all (it's in EnabledGrades but not in Slots).
        var enabledGrades = GradingSession.CurrentState.EnabledGrades
            .OrderBy(g => g.Order)
            .ToList();

        if (enabledGrades.Any())
        {
            // Label for each grade
            for (int i = 0; i < enabledGrades.Count; i++)
            {
                var grade = enabledGrades[i];
                double labelX;

                if (i == 0)
                {
                    // Highest grade (best, e.g., A): between last cursor and right boundary
                    labelX = (enabledSlots.Last().Score + maxScore) / 2;
                }
                else if (i == enabledGrades.Count - 1)
                {
                    // Lowest grade (catch-all): between left boundary and the
                    // lowest-score cursor line.
                    var firstCursorWithLine = enabledSlots.FirstOrDefault();
                    if (firstCursorWithLine != null)
                    {
                        labelX = (minScore + firstCursorWithLine.Score) / 2;
                    }
                    else
                    {
                        // Only one grade enabled - center in the plot
                        labelX = (minScore + maxScore) / 2;
                    }
                }
                else
                {
                    // Middle grades: between this grade's slot and the next higher grade's slot.
                    var slotForThisGrade = enabledSlots.FirstOrDefault(s => s.Grade.Order == grade.Order);
                    var slotForNextGrade = enabledSlots.FirstOrDefault(s => s.Grade.Order == enabledGrades[i - 1].Order);

                    if (slotForThisGrade != null && slotForNextGrade != null)
                    {
                        labelX = (slotForThisGrade.Score + slotForNextGrade.Score) / 2;
                    }
                    else
                    {
                        continue; // Skip if we can't find the slots
                    }
                }

                var label = new TextAnnotation
                {
                    Text = grade.DisplayName,
                    TextPosition = new DataPoint(labelX, 0.5),
                    TextColor = OxyColors.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    TextHorizontalAlignment = HorizontalAlignment.Center,
                    TextVerticalAlignment = VerticalAlignment.Middle,
                    XAxisKey = "SharedX",
                    YAxisKey = "CursorY",
                    Background = OxyColors.Transparent,
                    Stroke = OxyColors.White,
                    StrokeThickness = 1,
                    Padding = new OxyThickness(2, 2, 2, 1)
                };
                DotplotModel.Annotations.Add(label);
            }
        }

        DotplotModel.InvalidatePlot(true);
    }

    private void UpdateStatistics()
    {
        // Drop any prior stats annotations so repeat calls (e.g. after an
        // aggregate selection change) don't stack duplicates on top of
        // the old labels. Cursor annotations live on CursorY and are
        // untouched here — they're refreshed via UpdateCursors.
        var nonStatsAnnotations = DotplotModel.Annotations
            .Where(a => a.YAxisKey != "StatsY")
            .ToList();
        DotplotModel.Annotations.Clear();
        foreach (var ann in nonStatsAnnotations)
        {
            DotplotModel.Annotations.Add(ann);
        }

        // Calculate statistics from assessments
        var scores = ClassAssessment.Assessments.Select(a => (double)a.AggregateGrade).ToList();
        var mean = scores.Average();
        var stdDev = Math.Sqrt(scores.Average(s => Math.Pow(s - mean, 2)));

        var minRawScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxRawScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var scoreRange = maxRawScore - minRawScore;
        var xPadding = scoreRange * 0.2;
        var minScore = minRawScore - xPadding;
        var maxScore = maxRawScore + xPadding;

        var lightGray = OxyColor.FromRgb(180, 180, 180);

        // ===== Top/Bottom borders for Stats Area (no left/right) =====
        // Lines at edges of expanded axis range (-0.2 to 1.2) for padding around stats text
        var statsTopLine = new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = 1.30,
            MinimumX = minScore,
            MaximumX = maxScore,
            Color = OxyColor.FromRgb(60, 60, 60),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1,
            XAxisKey = "SharedX",
            YAxisKey = "StatsY",
            Layer = AnnotationLayer.AboveSeries
        };
        DotplotModel.Annotations.Add(statsTopLine);

        var statsBottomLine = new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = -0.30,
            MinimumX = minScore,
            MaximumX = maxScore,
            Color = OxyColor.FromRgb(60, 60, 60),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1,
            XAxisKey = "SharedX",
            YAxisKey = "StatsY",
            Layer = AnnotationLayer.AboveSeries
        };
        DotplotModel.Annotations.Add(statsBottomLine);

        // ===== Mean Label =====
        var meanLabel = new TextAnnotation
        {
            Text = "μ",
            TextPosition = new DataPoint(mean, 0.5),
            TextColor = lightGray,
            FontSize = 16,
            TextHorizontalAlignment = HorizontalAlignment.Center,
            TextVerticalAlignment = VerticalAlignment.Middle,
            XAxisKey = "SharedX",
            YAxisKey = "StatsY"
        };
        DotplotModel.Annotations.Add(meanLabel);

        // ===== Standard Deviation Labels =====
        // Skip sigma ticks entirely if the distribution is degenerate
        // (e.g. every student tied because the aggregate selection is
        // empty). Without this guard the loops below are infinite, since
        // mean + N * 0 stays <= maxScore forever, and the host process
        // OOMs adding annotations.
        if (stdDev <= 0) return;

        // Positive std devs
        int posStdCount = 1;
        while (mean + posStdCount * stdDev <= maxScore)
        {
            var x = mean + posStdCount * stdDev;

            var label = new TextAnnotation
            {
                Text = $"+{posStdCount}σ",
                TextPosition = new DataPoint(x, 0.5),
                TextColor = lightGray,
                FontSize = 14,
                TextHorizontalAlignment = HorizontalAlignment.Center,
                TextVerticalAlignment = VerticalAlignment.Middle,
                XAxisKey = "SharedX",
                YAxisKey = "StatsY"
            };
            DotplotModel.Annotations.Add(label);

            posStdCount++;
        }

        // Negative std devs
        int negStdCount = 1;
        while (mean - negStdCount * stdDev >= minScore)
        {
            var x = mean - negStdCount * stdDev;

            var label = new TextAnnotation
            {
                Text = $"-{negStdCount}σ",
                TextPosition = new DataPoint(x, 0.5),
                TextColor = lightGray,
                FontSize = 14,
                TextHorizontalAlignment = HorizontalAlignment.Center,
                TextVerticalAlignment = VerticalAlignment.Middle,
                XAxisKey = "SharedX",
                YAxisKey = "StatsY"
            };
            DotplotModel.Annotations.Add(label);

            negStdCount++;
        }
    }


    private void UpdateAxisPositions()
    {
        const double statsHeight = 30;
        const double cursorHeight = 30;
        
        // Get actual plot height
        var plotHeight = DotplotModel.PlotArea.Height;
        
        if (plotHeight > statsHeight + cursorHeight + 50) // Minimum viable height
        {
            // Calculate positions to maintain fixed heights
            double cursorStart = 0.0;
            double cursorEnd = cursorHeight / plotHeight;
            double dotStart = cursorEnd;
            double dotEnd = (plotHeight - statsHeight) / plotHeight;
            double statsStart = dotEnd;
            double statsEnd = 1.0;

            // Update axes
            var statsYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "StatsY");
            var dotYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "DotY");
            var cursorYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "CursorY");

            if (statsYAxis != null)
            {
                statsYAxis.StartPosition = statsStart;
                statsYAxis.EndPosition = statsEnd;
            }

            if (dotYAxis != null)
            {
                dotYAxis.StartPosition = dotStart;
                dotYAxis.EndPosition = dotEnd;
            }

            if (cursorYAxis != null)
            {
                cursorYAxis.StartPosition = cursorStart;
                cursorYAxis.EndPosition = cursorEnd;
            }
        }
    }

    /// <summary>
    /// Pushes the active session and the student-aggregate range onto
    /// the violin VM. Compliance rows now flow through
    /// <see cref="ComplianceGrid"/>'s own subscription, so the violin
    /// reads them via that VM (not via MWVM).
    /// </summary>
    private void WireViolinPlot()
    {
        if (ViolinPlotViewModel == null) return;

        ViolinPlotViewModel.GradingSession = GradingSession;
        ViolinPlotViewModel.ComplianceGrid = ComplianceGrid;
        ViolinPlotViewModel.MinScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        ViolinPlotViewModel.MaxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
    }

    /// <summary>
    /// Re-seeds the session from the default curve and rewires the
    /// violin plot. Used by initial load
    /// (<see cref="LoadFromExcelFile"/>, <see cref="LoadStateAsync"/>) and by
    /// the aggregate-set-changed branch of <see cref="ApplyScoreSelections"/>.
    /// The session itself owns the narrow-aggregate fallback (M002/S05/T03)
    /// inside <see cref="GradingSession.ReseedFromDefaults"/>, so the call
    /// here always emits a valid state — and the
    /// <see cref="ComplianceGridViewModel"/> picks up the new counts via
    /// its own LastChange subscription.
    /// </summary>
    private void SeedCursorsFromDefaults()
    {
        GradingSession.ReseedFromDefaults();
        WireViolinPlot();
    }

    [RelayCommand]
    private void ToggleCompliancePane()
    {
        IsCompliancePaneOpen = !IsCompliancePaneOpen;
    }

    [RelayCommand]
    private void ToggleDrillDownPane()
    {
        IsDrillDownPaneOpen = !IsDrillDownPaneOpen;
    }

    [RelayCommand]
    private void ToggleViolinPane()
    {
        IsViolinPaneOpen = !IsViolinPaneOpen;
    }

    /// <summary>
    /// Saves current state to a JSON file. Called from UI.
    /// The actual file dialog is handled in the View.
    /// </summary>
    [RelayCommand]
    private async Task SaveStateAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _stateService.SaveAsync(
                filePath,
                ClassAssessment.Assessments,
                GradingSession,
                ClassAssessment.ScoreSelections,
                _currentSourceFile,
                SignificancePlotViewModel?.TestFamily ?? SignificanceTestFamily.Parametric);

            CurrentSaveFilePath = filePath;
            HasUnsavedChanges = false;
            Log($"State saved to: {filePath}");
        }
        catch (Exception ex)
        {
            Log($"Error saving state: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Opens another analysis file in its own window. Each invocation
    /// spawns a fresh DI scope, loads the file into the scope's
    /// <see cref="MainWindowViewModel"/>, and shows a new
    /// <see cref="MainWindow"/>. The new window is fully independent —
    /// drags, hover, comments in one window do not affect any other.
    /// See ADR-0012.
    /// </summary>
    /// <param name="parent">The invoking window — supplies the
    /// <see cref="IStorageProvider"/> for the file picker.</param>
    [RelayCommand]
    private async Task OpenAnotherFile(Window? parent)
    {
        if (parent is null || _workspaceFactory is null) return;

        var filePath = await PromptForFile(parent.StorageProvider, _stateService.LastUsedDirectory);
        if (string.IsNullOrEmpty(filePath)) return;

        var workspace = await _workspaceFactory.CreateForFileAsync(filePath);
        var newVm = workspace.Services.GetRequiredService<MainWindowViewModel>();

        // The Closed handler closure keeps the workspace alive for the window's
        // lifetime and disposes the scope deterministically when the user closes
        // the window — releasing the messenger, hover service, VMs, and loaded
        // ClassAssessment for GC. See ADR-0012.
        var window = new MainWindow { DataContext = newVm };
        window.Closed += (_, _) => workspace.Dispose();
        window.Show();
    }

    private static async Task<string?> PromptForFile(IStorageProvider storageProvider, string? suggestedDirectory)
    {
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrEmpty(suggestedDirectory) && Directory.Exists(suggestedDirectory))
        {
            startLocation = await storageProvider.TryGetFolderFromPathAsync(suggestedDirectory);
        }

        var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open another file",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported files") { Patterns = new[] { "*.xlsx", "*.xls", "*.dots" } },
                new FilePickerFileType("Excel files") { Patterns = new[] { "*.xlsx", "*.xls" } },
                new FilePickerFileType("Dotsesses files") { Patterns = new[] { "*.dots" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    /// <summary>
    /// Loads state from a JSON file. Called from UI.
    /// The actual file dialog is handled in the View.
    /// </summary>
    [RelayCommand]
    private async Task LoadStateAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var state = await _stateService.LoadAsync(filePath);

            // Restore the Significance Matrix test family before the matrix is
            // (re)initialized below, so the first render uses the saved family.
            // v4 files default to Parametric via SavedState's field default.
            if (SignificancePlotViewModel != null)
            {
                SignificancePlotViewModel.TestFamily = state.SignificanceTestFamily;
            }

            // Convert saved students back to domain models
            var (students, muppetMap) = _stateService.ConvertToStudents(state);

            // Rebuild ClassAssessment with loaded data
            var curveGenerator = new DefaultCurveGenerator();
            var defaultCurve = curveGenerator.GenerateRanges(students.Count);

            // Generate series color map from first student's scores
            var firstStudent = students.First();
            var seriesNames = firstStudent.Scores
                .Select(s => s.Index.HasValue ? $"{s.Name} {s.Index}" : s.Name)
                .ToList();
            var seriesColorMap = SeriesColorService.GenerateColorMap(seriesNames);

            ClassAssessment = new ClassAssessment(
                students,
                defaultCurve,
                muppetMap,
                seriesColorMap);

            GradingSession = new GradingSession(
                ClassAssessment,
                new CursorPlacementCalculator(),
                _cursorValidation,
                _cutoffCountCalculator,
                _initialCutoffCalculator);

            ComplianceGrid = new ComplianceGridViewModel(ClassAssessment, GradingSession);

            var (savedCutoffs, savedEnabledGrades) =
                _stateService.ConvertToGradingState(state, GradingSession);
            GradingSession.LoadCutoffs(savedCutoffs, savedEnabledGrades);

            // Restore saved score selections from v2 .dots files. v1 files (and brand-new v2
            // files written before the selection feature shipped) deserialize as empty
            // ScoreSelections, which falls through to SeedDefaultSelectionsIfEmpty below.
            var savedSelections = _stateService.ConvertToScoreSelections(state);
            if (savedSelections.Count > 0)
            {
                ClassAssessment.ScoreSelections = savedSelections;
                // Recompute per-student aggregates against the loaded selection set so the
                // dotplot/cursor pipeline below sees the correct AggregateGrade values.
                var aggregateSet = BuildAggregateSet(savedSelections);
                foreach (var assessment in ClassAssessment.Assessments)
                {
                    assessment.RecalculateAggregate(aggregateSet);
                }
            }

            SeedDefaultSelectionsIfEmpty();

            _currentSourceFile = state.SourceFile;
            CurrentSaveFilePath = filePath;

            // SeedCursorsFromDefaults rebuilds violin wiring against the
            // loaded session. The session was already hydrated above via
            // GradingSession.LoadCutoffs, so saved cursor positions are
            // preserved without an additional overlay.
            Log("LoadStateAsync: Seeding cursors from default curve");
            SeedCursorsFromDefaults();

            // Re-apply the saved cutoffs after SeedCursorsFromDefaults
            // (which calls ReseedFromDefaults and overwrites them).
            GradingSession.LoadCutoffs(savedCutoffs, savedEnabledGrades);

            Log("LoadStateAsync: Initializing dotplot");
            InitializeDotplot();

            // Update the display filename from the source file in the saved state
            if (!string.IsNullOrEmpty(state.SourceFile))
            {
                SourceFileName = Path.GetFileName(state.SourceFile);
            }

            HasUnsavedChanges = false;
            Log($"State loaded from: {filePath}");
        }
        catch (Exception ex)
        {
            Log($"Error loading state: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Builds the (Name, Index?) aggregate-set tuple list from a selection list.
    /// Only Numeric entries with <see cref="ScoreSelection.Aggregate"/> == true contribute —
    /// Categorical columns have no numeric value to sum, so a stray
    /// <c>Aggregate=true, Type=Categorical</c> row is filtered out defensively.
    ///
    /// TEMPORARY (TD010): when <see cref="FeatureFlags.UseSpreadsheetTotal"/> is on,
    /// the aggregate set is just the Total column, so <c>RecalculateAggregate</c>
    /// takes the score from the spreadsheet Total verbatim (and leaves its value
    /// untouched) instead of summing components.
    /// </summary>
    private static List<(string Name, int? Index)> BuildAggregateSet(IReadOnlyList<ScoreSelection> selections)
        => FeatureFlags.UseSpreadsheetTotal
            ? SpreadsheetTotalKeys(selections)
            : selections.Where(s => s.Aggregate && s.Type == ScoreColumnType.Numeric)
                        .Select(s => (s.Name, s.Index))
                        .ToList();

    /// <summary>
    /// The single Total key, derived from the selections so its Name casing matches
    /// the spreadsheet (falling back to "Total"). Used only by the temporary
    /// spreadsheet-Total mode (TD010).
    /// </summary>
    private static List<(string Name, int? Index)> SpreadsheetTotalKeys(IReadOnlyList<ScoreSelection> selections)
    {
        var total = selections.FirstOrDefault(s => s.Index == null &&
            string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase));
        return new List<(string Name, int? Index)> { (total?.Name ?? "Total", null) };
    }

    /// <summary>
    /// Builds an unordered, equality-comparable set of (Name, Index?) keys for the
    /// AGGREGATE-flagged subset of a selection list. Used by
    /// <see cref="ApplyScoreSelections"/> to detect whether the aggregate composition
    /// changed across an Apply, regardless of selection ordering or Display/Correlation
    /// changes (MEM035 / S02 cursor-reset semantics).
    ///
    /// Equality is case-sensitive on the score Name — MEM008 confirms the codebase is
    /// uniformly case-sensitive on score keys after defaults are seeded. Categorical
    /// rows are filtered out so a Numeric→Categorical conversion in slice 2 cleanly
    /// flips this key set and triggers the ADR-0005 reseed.
    /// </summary>
    private static HashSet<(string Name, int? Index)> BuildAggregateKeySet(IReadOnlyList<ScoreSelection> selections)
        => FeatureFlags.UseSpreadsheetTotal
            ? SpreadsheetTotalKeys(selections).ToHashSet()
            : selections.Where(s => s.Aggregate && s.Type == ScoreColumnType.Numeric)
                        .Select(s => (s.Name, s.Index))
                        .ToHashSet();

    /// <summary>
    /// For every column whose <see cref="ScoreSelection.Type"/> differs between the old
    /// and new selection lists, move its per-student data between
    /// <see cref="StudentAssessment.Scores"/> and <see cref="StudentAssessment.Attributes"/>.
    /// Comments survive in both directions (<see cref="Score.Comment"/> ↔
    /// <see cref="StudentAttribute.Comment"/>). Stringification and parsing both use
    /// <see cref="System.Globalization.CultureInfo.InvariantCulture"/> for round-trippability.
    /// Slice 2 of ADR-0013.
    /// </summary>
    private void ApplyTypeTransitions(
        IReadOnlyList<ScoreSelection> oldSelections,
        IReadOnlyList<ScoreSelection> newSelections)
    {
        var oldByKey = oldSelections.ToDictionary(s => (s.Name, s.Index));
        foreach (var fresh in newSelections)
        {
            if (!oldByKey.TryGetValue((fresh.Name, fresh.Index), out var prior)) continue;
            if (prior.Type == fresh.Type) continue;

            if (prior.Type == ScoreColumnType.Numeric &&
                fresh.Type == ScoreColumnType.Categorical)
            {
                MoveScoreToAttribute(fresh.Name, fresh.Index);
            }
            else if (prior.Type == ScoreColumnType.Categorical &&
                     fresh.Type == ScoreColumnType.Numeric)
            {
                MoveAttributeToScore(fresh.Name, fresh.Index);
            }
        }
    }

    private void MoveScoreToAttribute(string name, int? index)
    {
        foreach (var assessment in ClassAssessment.Assessments)
        {
            var score = assessment.Scores.FirstOrDefault(s => s.Name == name && s.Index == index);
            if (score == null) continue;
            assessment.Scores.Remove(score);
            assessment.Attributes.Add(new StudentAttribute(
                name,
                index,
                score.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                score.Comment));
        }
    }

    private void MoveAttributeToScore(string name, int? index)
    {
        foreach (var assessment in ClassAssessment.Assessments)
        {
            var attr = assessment.Attributes.FirstOrDefault(a => a.Name == name && a.Index == index);
            if (attr == null) continue;
            assessment.Attributes.Remove(attr);
            // The row VM's G4 guard ensures every value parses; the defensive fallback
            // silently drops a value that doesn't (unreachable in practice).
            if (double.TryParse(attr.Value,
                                System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var v))
            {
                assessment.Scores.Add(new Score(name, index, v, attr.Comment));
            }
        }
    }

    /// <summary>
    /// If <see cref="ClassAssessment.ScoreSelections"/> is empty (fresh .xlsx load or v1 .dots load
    /// that pre-dates the selections feature), populate it via
    /// <see cref="ScoreSelectionDefaults.GenerateDefaults"/> and recompute every student's
    /// aggregate cache against the new selection set. Closes R012's open half.
    /// </summary>
    private void SeedDefaultSelectionsIfEmpty()
    {
        if (ClassAssessment == null) return;
        if (ClassAssessment.ScoreSelections.Count > 0) return;
        if (!ClassAssessment.Assessments.Any()) return;

        var firstStudent = ClassAssessment.Assessments.First();
        var ordinalColumns = ScoreSelectionDefaults.DetectOrdinalColumns(ClassAssessment.Assessments);
        var baseDefaults = ScoreSelectionDefaults.GenerateDefaults(
            firstStudent.Scores,
            firstStudent.Attributes,
            ordinalColumns);

        ClassAssessment.ScoreSelections = ApplyDataDrivenSelection(baseDefaults);

        var aggregateSet = BuildAggregateSet(ClassAssessment.ScoreSelections);
        foreach (var assessment in ClassAssessment.Assessments)
        {
            assessment.RecalculateAggregate(aggregateSet);
        }
    }

    /// <summary>
    /// Pares the all-on base defaults down to a sensible per-plot subset
    /// (ADR-0016): Distribution = Total + 10 leftmost numerics; Correlation =
    /// the top-r² non-Total pairs + Total; Significance = categoricals with a
    /// numeric at p≤0.2 plus their top-3 numerics. Only Display / Correlation /
    /// Significance are rewritten; Type and Aggregate are preserved. The
    /// Significance refinement is skipped (base flags kept) when the Python
    /// service is unavailable (test factory).
    /// </summary>
    private IReadOnlyList<ScoreSelection> ApplyDataDrivenSelection(
        IReadOnlyList<ScoreSelection> baseDefaults)
    {
        var numericSel = baseDefaults.Where(s => s.Type == ScoreColumnType.Numeric).ToList();
        var numericSeries = BuildSeriesData(ClassAssessment!, s => s.Type == ScoreColumnType.Numeric);
        var categoricalSeries = BuildCategoricalSeriesData(ClassAssessment!, s => s.Type == ScoreColumnType.Categorical);

        var orderedNumericNames = numericSeries.Select(s => s.SeriesName).ToList();
        var totalName = numericSel
            .Where(s => string.Equals(s.Name, "Total", StringComparison.OrdinalIgnoreCase) && s.Index == null)
            .Select(SeriesNameOf)
            .FirstOrDefault();

        var displayNames = PlotSelectionCalculator.SelectDistribution(orderedNumericNames, totalName);

        var correlationInput = numericSeries
            .Select(s => (s.SeriesName, (IReadOnlyDictionary<string, double>)s.Scores))
            .ToList();
        var correlationNames = PlotSelectionCalculator.SelectCorrelation(correlationInput, totalName);

        // Significance needs per-cell p-values (Python). Skip the refinement
        // when the service is absent — leave the base Significance flags.
        HashSet<string>? significanceNames = null;
        if (_significancePlotService != null && categoricalSeries.Count > 0)
        {
            significanceNames = PlotSelectionCalculator.SelectSignificance(
                orderedNumericNames,
                categoricalSeries.Select(s => s.SeriesName).ToList(),
                BuildSignificancePLookup(numericSeries, categoricalSeries, SignificanceTestFamily.Parametric),
                // A Grade column is derived from the scores, so it would test as
                // trivially significant — don't auto-select it (user can still
                // enable it in Settings).
                excludedCategoricalNames: new[] { "Grade", "Grades" });
        }

        return baseDefaults.Select(s =>
        {
            var name = SeriesNameOf(s);
            bool isNumeric = s.Type == ScoreColumnType.Numeric;
            bool isCategorical = s.Type == ScoreColumnType.Categorical;
            return s with
            {
                Display = isNumeric ? displayNames.Contains(name) : s.Display,
                Correlation = isNumeric && correlationNames.Contains(name),
                // Ordinal columns aren't part of the categorical significance scan
                // (ADR-0017 — they join the matrix as columns in a later slice), so
                // leave their base Significance rather than force-clearing it.
                Significance = significanceNames != null && (isNumeric || isCategorical)
                    ? significanceNames.Contains(name)
                    : s.Significance,
            };
        }).ToList();
    }

    /// <summary>The series-name join key used by the plots and the selection
    /// calculator: <c>"Name Index"</c> when indexed, else <c>"Name"</c>.</summary>
    private static string SeriesNameOf(ScoreSelection s) =>
        s.Index.HasValue ? $"{s.Name} {s.Index}" : s.Name;

    /// <summary>
    /// Builds a per-cell p-value lookup (numeric series name, categorical series
    /// name) → p, by running the omnibus test for the given test family. Shared
    /// by the load-time auto-select and the interactive Optimize. Returns an
    /// all-null lookup when the Python service is unavailable.
    /// </summary>
    private Func<string, string, double?> BuildSignificancePLookup(
        List<(string SeriesName, Dictionary<string, double> Scores)> numericSeries,
        List<(string SeriesName, Dictionary<string, string> Subgroups)> categoricalSeries,
        SignificanceTestFamily testFamily)
    {
        if (_significancePlotService == null || categoricalSeries.Count == 0)
        {
            return (_, _) => null;
        }

        var pvalues = _significancePlotService.ComputeCellPValues(
            numericSeries.Select(s => (s.SeriesName, s.Scores)).ToList(),
            categoricalSeries.Select(s => (s.SeriesName, s.Subgroups)).ToList(),
            testFamily);
        return (num, cat) => pvalues.TryGetValue((num, cat), out var p) ? p : null;
    }

    /// <summary>
    /// Optimize the Significance Matrix: holding the currently-displayed
    /// categorical columns fixed, rank every (numeric × displayed-categorical)
    /// cell by p (using the matrix's current test family), take the 10
    /// lowest-p cells, and set the matrix's numeric rows to the numerics in
    /// them — replacing the current rows. Categoricals, Display, Correlation
    /// and Aggregate are untouched. Invoked via the Optimize button on the
    /// Significance plot (callback wired in the constructor).
    /// </summary>
    private void OnOptimizeSignificance()
    {
        if (ClassAssessment == null || _significancePlotService == null) return;

        var numericSeries = BuildSeriesData(ClassAssessment, s => s.Type == ScoreColumnType.Numeric);
        // Ordinal columns are Significance Matrix columns too (ADR-0017), so
        // they count as optimization targets. Ordering isn't needed here —
        // ComputeCellPValues (the only consumer below) is order-independent.
        var categoricalSeries = BuildCategoricalSeriesData(
            ClassAssessment, s => s.Significance &&
                (s.Type == ScoreColumnType.Categorical || s.Type == ScoreColumnType.Ordinal));
        if (categoricalSeries.Count == 0) return; // nothing displayed to optimize against

        var testFamily = SignificancePlotViewModel?.TestFamily ?? SignificanceTestFamily.Parametric;
        var pLookup = BuildSignificancePLookup(numericSeries, categoricalSeries, testFamily);

        var numericSet = PlotSelectionCalculator.SelectTopCellNumerics(
            numericSeries.Select(s => s.SeriesName).ToList(),
            categoricalSeries.Select(s => s.SeriesName).ToList(),
            pLookup,
            topCells: 10);

        var newSelections = ClassAssessment.ScoreSelections.Select(s =>
            s.Type == ScoreColumnType.Numeric
                ? s with { Significance = numericSet.Contains(SeriesNameOf(s)) }
                : s) // displayed categoricals stay selected; others stay as-is
            .ToList();

        ApplyScoreSelections(newSelections);
    }

    /// <summary>
    /// Orchestrates a recompute of all selection-derived state in response to the user
    /// pressing Apply in the Settings dialog. Mutates <see cref="ClassAssessment.ScoreSelections"/>,
    /// recalculates per-student aggregate caches, refreshes grade counts and dotplot points,
    /// kicks off async violin/correlation regen (filtered by selection in T03), sets
    /// <see cref="HasUnsavedChanges"/>, and rebuilds the drill-down card if a student is hovered.
    /// </summary>
    public void ApplyScoreSelections(IReadOnlyList<ScoreSelection> newSelections)
    {
        ArgumentNullException.ThrowIfNull(newSelections);

        var aggregateBefore = ClassAssessment.Assessments.FirstOrDefault()?.AggregateGrade ?? 0;

        Log($"MainWindowViewModel: ApplyScoreSelections — {newSelections.Count} selections " +
            $"(Display={newSelections.Count(s => s.Display)}, Aggregate={newSelections.Count(s => s.Aggregate)}, " +
            $"Correlation={newSelections.Count(s => s.Correlation)}); first-student aggregate before={aggregateBefore}");

        // Capture the prior aggregate-key set BEFORE assigning the new selections so we can
        // detect aggregate composition changes after the per-student aggregate recompute below
        // (MEM035: aggregate-set change → re-seed cursors via SeedCursorsFromDefaults; Display-
        // or Correlation-only changes leave cursors untouched).
        var oldAggregateKeys = BuildAggregateKeySet(ClassAssessment.ScoreSelections);
        var newAggregateKeys = BuildAggregateKeySet(newSelections);
        var aggregateSetChanged = !oldAggregateKeys.SetEquals(newAggregateKeys);

        // Move per-student data between Scores and Attributes for any column whose
        // Type changed (slice 2 of ADR-0013). Must run BEFORE ClassAssessment.ScoreSelections
        // is reassigned and BEFORE RecalculateAggregate, so the subsequent aggregate sum sees
        // the post-move shape.
        ApplyTypeTransitions(ClassAssessment.ScoreSelections, newSelections);

        ClassAssessment.ScoreSelections = newSelections;

        var aggregateSet = BuildAggregateSet(newSelections);
        foreach (var assessment in ClassAssessment.Assessments)
        {
            assessment.RecalculateAggregate(aggregateSet);
        }

        var aggregateAfter = ClassAssessment.Assessments.FirstOrDefault()?.AggregateGrade ?? 0;
        Log($"MainWindowViewModel: ApplyScoreSelections — first-student aggregate after={aggregateAfter} " +
            $"(changed={aggregateBefore != aggregateAfter})");

        // The per-student RecalculateAggregate loop above MUST run
        // before SeedCursorsFromDefaults so the session reseed sees
        // the freshly-updated AggregateGrade values.
        //
        // Edge case: when newAggregateKeys is empty, every student's
        // AggregateGrade collapses to 0 — the empty-aggregate
        // configuration is degenerate (no meaningful cursor placement
        // exists), so we skip the reset and leave the existing cursors
        // in place. Cursors remain visible so the user can re-add
        // aggregate components and recover.
        if (aggregateSetChanged && newAggregateKeys.Count > 0)
        {
            Log("MainWindowViewModel: ApplyScoreSelections — aggregate set changed, re-seeding cursors from default curve");
            SeedCursorsFromDefaults();
        }

        UpdateDotplotPoints();
        // Stats labels (mean / ±σ ticks at the top of the dotplot) are
        // computed from AggregateGrade, so they need a refresh whenever
        // the aggregate set changes — otherwise they keep showing the
        // mean/σ of the prior aggregate composition.
        UpdateStatistics();

        // OxyPlot's PlotView does not auto-refresh when the model is mutated; InvalidatePlot
        // alone is unreliable for compiled-binding scenarios. Force the view to re-bind by
        // raising PropertyChanged on the bound model property — the [ObservableProperty]
        // setter shortcut works because Avalonia treats a same-reference reassignment as a
        // change notification when OnPropertyChanged is invoked explicitly.
        OnPropertyChanged(nameof(DotplotModel));

        // Fire-and-forget: violin/correlation/significance regen runs on background tasks internally.
        // Each filters its own slice of ScoreSelections (Display / Correlation / Significance).
        InitializeViolinPlotAsync();
        InitializeCorrelationPlotAsync();
        InitializeSignificanceMatrixAsync();

        HasUnsavedChanges = true;

        // Rebuild the drill-down card so it reflects the new Display filter (T04 will wire
        // StudentCardViewModel.DisplayScores to honor the filter; the rebuild path is a no-op
        // today but is necessary so the hovered card refreshes when ScoreSelections change).
        if (HoveredStudent != null)
        {
            var hoveredId = HoveredStudentId;
            OnHoveredStudentIdChanged(null);
            OnHoveredStudentIdChanged(hoveredId);
        }
    }

    /// <summary>
    /// Marks the state as having unsaved changes.
    /// </summary>
    public void MarkAsChanged()
    {
        HasUnsavedChanges = true;
    }

    private string GetGradeForStudent(StudentAssessment student)
    {
        return GradingSession.AssignedGradeFor(student.Id).DisplayName;
    }

    /// <summary>
    /// Called by HoverDelayService when a hover is activated (after delay and stability check).
    /// </summary>
    private void OnHoverActivated(int? studentId)
    {
        HoveredStudentId = studentId;

        // Broadcast to violin plot for cross-view sync
        _messenger.Send(new StudentHoverMessage(
            studentId,
            "dotplot",
            null));
    }

    partial void OnHoveredStudentIdChanged(int? value)
    {
        // Dispose previous StudentCardViewModel to unsubscribe from score changes
        HoveredStudent?.Dispose();

        if (value.HasValue)
        {
            var student = ClassAssessment.Assessments.FirstOrDefault(s => s.Id == value.Value);
            if (student != null)
            {
                var grade = GetGradeForStudent(student);
                HoveredStudent = new StudentCardViewModel(student, grade, ClassAssessment.SeriesColorMap, _messenger, () => _hoverDelayService.ClearHover(), BuildDisplayScores(student));
            }
            else
            {
                HoveredStudent = null;
            }
        }
        else
        {
            HoveredStudent = null;
        }
    }

    private void OnDotplotMouseDown(object? sender, OxyMouseDownEventArgs e)
    {
        var series = DotplotModel.Series.FirstOrDefault() as ScatterSeries;
        if (series == null)
            return;

        // Transform click position to data coordinates
        var clickPos = series.InverseTransform(e.Position);

        // Check if we're in the Dot Display region
        var dotYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "DotY");
        if (dotYAxis != null && clickPos.Y >= dotYAxis.Minimum && clickPos.Y <= dotYAxis.Maximum)
        {
            // Find nearest student using screen space distance
            var nearestPoint = FindNearestStudent(e.Position);

            if (nearestPoint != null)
            {
                var student = ClassAssessment.Assessments.FirstOrDefault(a => a.Id == nearestPoint.Id);

                // Handle right-click - open comment editor
                if (e.ChangedButton == OxyMouseButton.Right && student != null)
                {
                    _messenger.Send(new Messages.EditStudentMessage(student.Id));
                    e.Handled = true;
                    return;
                }

                // Handle left-click - check for double-click
                if (e.ChangedButton == OxyMouseButton.Left)
                {
                    var now = DateTime.Now;
                    var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;

                    if (_lastClickedStudentId == nearestPoint.Id && timeSinceLastClick < DoubleClickThresholdMs && student != null)
                    {
                        // Double-click detected - open comment editor
                        _messenger.Send(new Messages.EditStudentMessage(student.Id));
                        _lastClickedStudentId = null; // Reset to prevent triple-click
                        e.Handled = true;
                        return;
                    }

                    // Single click - record for potential double-click
                    _lastClickTime = now;
                    _lastClickedStudentId = nearestPoint.Id;
                    e.Handled = true;
                    return;
                }
            }
            else
            {
                // Clicked on empty space in Dot Display area - clear hover
                if (e.ChangedButton == OxyMouseButton.Left)
                {
                    _hoverDelayService.ClearHover();
                    e.Handled = true;
                    return;
                }
            }
        }

        // Handle left-click only for cursor dragging
        if (e.ChangedButton != OxyMouseButton.Left)
            return;

        // Check if clicking near a cursor (within 3 units horizontally)
        var nearestCursor = FindNearestCursor(clickPos.X);
        var cursorYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "CursorY");

        // Check if we're in the cursor area by transforming with the cursor axis
        if (cursorYAxis != null && nearestCursor.slot != null && nearestCursor.distance < 3)
        {
            // Transform the Y position using the cursor Y axis
            var cursorY = cursorYAxis.InverseTransform(e.Position.Y);

            // Only allow cursor dragging if clicking in the Grade Cursors area
            if (cursorY >= cursorYAxis.Minimum && cursorY <= cursorYAxis.Maximum)
            {
                // Start dragging cursor
                _draggingCursor = nearestCursor.slot;
                _isDraggingCursor = true;
                e.Handled = true;
                return;
            }
        }

        // Always mark event as handled to prevent default OxyPlot tracker behavior
        e.Handled = true;
    }

    private void OnDotplotMouseMove(object? sender, OxyMouseEventArgs e)
    {
        var series = DotplotModel.Series.FirstOrDefault() as ScatterSeries;
        if (series == null)
            return;

        var pos = series.InverseTransform(e.Position);

        if (!_isDraggingCursor || _draggingCursor == null)
        {
            // Check if hovering over a cursor
            var nearestCursor = FindNearestCursor(pos.X);
            var cursorYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "CursorY");

            if (cursorYAxis != null && nearestCursor.slot != null && nearestCursor.distance < 3)
            {
                var cursorY = cursorYAxis.InverseTransform(e.Position.Y);
                IsResizeCursor = cursorY >= cursorYAxis.Minimum && cursorY <= cursorYAxis.Maximum;
            }
            else
            {
                IsResizeCursor = false;
            }

            // Check for student hover - report candidate to delay service
            var student = FindNearestStudent(e.Position);
            int? candidateId = student?.Id;

            // Report hover candidate to delay service (it handles timing)
            // Note: we pass the OxyPlot screen position for stability check
            if (candidateId != null)
            {
                _hoverDelayService.ReportHoverCandidate(candidateId, new Avalonia.Point(e.Position.X, e.Position.Y));
            }
            // Don't clear on null - require explicit clear

            e.Handled = true;
            return;
        }

        var newScore = (int)Math.Round(pos.X);

        // Validation lives on the session — invalid moves are rejected
        // and leave the slot at its last valid position. The session
        // applies its own canonical bounds (see ScoreBoundsMargin{Below,
        // Above} in GradingSession).
        GradingSession.MoveCutoff(_draggingCursor.Grade, newScore, this);
        e.Handled = true;
    }

    private void OnDotplotMouseUp(object? sender, OxyMouseEventArgs e)
    {
        if (_isDraggingCursor && _draggingCursor != null)
        {
            // Session already committed every drag step during pointer-move;
            // nothing to finalize here.
            _isDraggingCursor = false;
            _draggingCursor = null;
            e.Handled = true;
        }
    }

    private (CutoffSlot? slot, double distance) FindNearestCursor(double xPos)
    {
        CutoffSlot? nearest = null;
        double minDistance = double.MaxValue;

        // Slots already exclude the structural catch-all (ADR-0011), so
        // every enabled slot is a candidate.
        foreach (var slot in GradingSession.Slots.Where(s => s.IsEnabled))
        {
            double distance = Math.Abs(slot.Score - xPos);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = slot;
            }
        }

        return (nearest, minDistance);
    }

    private StudentAssessment? FindNearestStudent(ScreenPoint clickPosition)
    {
        // Get the axes we need for transformation
        var xAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "SharedX");
        var yAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "DotY");
        
        if (xAxis == null || yAxis == null)
            return null;

        // Group students by score to find Y positions
        var scoreGroups = ClassAssessment.Assessments
            .GroupBy(a => a.AggregateGrade)
            .OrderBy(g => g.Key)
            .ToList();

        double minDistance = double.MaxValue;
        StudentAssessment? nearest = null;

        foreach (var group in scoreGroups)
        {
            var studentsAtScore = group.OrderBy(s => s.Id).ToList();
            var binOffset = group.Key % 2 == 1 ? 0.1 : 0.0;

            for (int i = 0; i < studentsAtScore.Count; i++)
            {
                double yPos = i * 2 + binOffset;
                
                // Transform data position to screen position
                var dataPoint = new DataPoint(group.Key, yPos);
                var screenPoint = xAxis.Transform(dataPoint.X, yPos, yAxis);
                
                // Calculate pixel distance
                double distance = Math.Sqrt(
                    Math.Pow(screenPoint.X - clickPosition.X, 2) + 
                    Math.Pow(screenPoint.Y - clickPosition.Y, 2));

                if (distance < minDistance && distance <= 10) // Within 10 pixels
                {
                    minDistance = distance;
                    nearest = studentsAtScore[i];
                }
            }
        }

        return nearest;
    }

    /// <summary>
    /// Applies the specified theme to the DotPlot model.
    /// </summary>
    public void ApplyTheme(ThemeName theme)
    {
        if (DotplotModel == null) return;

        // Update model background
        DotplotModel.Background = ThemeColors.OxyBackground(theme);
        DotplotModel.PlotAreaBackground = ThemeColors.OxyBackground(theme);
        DotplotModel.PlotAreaBorderColor = ThemeColors.OxyBorder(theme);

        // Update annotations colors
        foreach (var annotation in DotplotModel.Annotations)
        {
            switch (annotation)
            {
                case LineAnnotation line:
                    // Cursor lines use transparent line color
                    if (line.Color.A < 255) // Semi-transparent
                    {
                        line.Color = ThemeColors.OxyTransparentLine(theme);
                    }
                    else
                    {
                        line.Color = ThemeColors.OxyBorder(theme);
                    }
                    break;

                case RectangleAnnotation rect:
                    // Cursor handles
                    rect.Fill = ThemeColors.OxyHandleFill(theme);
                    rect.Stroke = ThemeColors.OxyHandleStroke(theme);
                    break;

                case TextAnnotation text:
                    // Grade labels have white border (Stroke) and transparent background
                    // Statistics labels have no border (Stroke = Transparent)
                    if (text.StrokeThickness > 0 && text.Stroke != OxyColors.Transparent)
                    {
                        // Grade labels with border - use foreground color for text and stroke
                        text.TextColor = ThemeColors.OxyForeground(theme);
                        text.Stroke = ThemeColors.OxyForeground(theme);
                        text.Background = OxyColors.Transparent;
                    }
                    else
                    {
                        // Statistics labels - use secondary text color
                        text.TextColor = ThemeColors.OxySecondaryText(theme);
                    }
                    break;
            }
        }

        DotplotModel.InvalidatePlot(true);
    }
}
