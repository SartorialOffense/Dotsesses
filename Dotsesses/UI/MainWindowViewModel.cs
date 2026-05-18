namespace Dotsesses.UI;

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Calculators;
using Dotsesses.Messages;
using Dotsesses.Models;
using Dotsesses.Services;
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
    private readonly StateService _stateService = new();

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
    private PlotTabContainerViewModel? _plotTabContainerViewModel;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    private string? _currentSaveFilePath;

    [ObservableProperty]
    private string _sourceFileName = "No file loaded";

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
        HoverDelayService hoverDelayService)
    {
        Log("MainWindowViewModel: Constructor started");

        _messenger = messenger;
        _violinPlotViewModel = violinPlotViewModel;
        _correlationPlotViewModel = correlationPlotViewModel;
        _hoverDelayService = hoverDelayService;

        // Create the tab container ViewModel
        _plotTabContainerViewModel = new PlotTabContainerViewModel(violinPlotViewModel, correlationPlotViewModel);

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
            WeakReferenceMessenger.Default,
            null!,
            null!,
            new HoverDelayService());
    }

    /// <summary>
    /// Loads student data from an Excel file and initializes all UI components.
    /// </summary>
    /// <param name="excelFilePath">Path to the Excel file containing scores</param>
    public void LoadFromExcelFile(string excelFilePath)
    {
        Log($"MainWindowViewModel: Loading from Excel file: {excelFilePath}");

        _currentSourceFile = excelFilePath;

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
        return result;
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

                // Now update the ViewModel on the UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ViolinPlotViewModel.SetClassAssessment(ClassAssessment);
                    ViolinPlotViewModel.UpdateDataAndRegenerate(seriesData, commentMap, muppetNameMap, 3.0);
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

                // Build muppet name map: student ID -> muppet name
                var muppetNameMap = ClassAssessment.MuppetNameMap
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name);

                // Update ViewModel on UI thread
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CorrelationPlotViewModel.UpdateDataAndRegenerate(seriesData, muppetNameMap, 3.0);
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
                _currentSourceFile);

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
    /// Only entries with <see cref="ScoreSelection.Aggregate"/> == true contribute.
    /// </summary>
    private static List<(string Name, int? Index)> BuildAggregateSet(IReadOnlyList<ScoreSelection> selections)
        => selections.Where(s => s.Aggregate).Select(s => (s.Name, s.Index)).ToList();

    /// <summary>
    /// Builds an unordered, equality-comparable set of (Name, Index?) keys for the
    /// AGGREGATE-flagged subset of a selection list. Used by
    /// <see cref="ApplyScoreSelections"/> to detect whether the aggregate composition
    /// changed across an Apply, regardless of selection ordering or Display/Correlation
    /// changes (MEM035 / S02 cursor-reset semantics).
    ///
    /// Equality is case-sensitive on the score Name — MEM008 confirms the codebase is
    /// uniformly case-sensitive on score keys after defaults are seeded.
    /// </summary>
    private static HashSet<(string Name, int? Index)> BuildAggregateKeySet(IReadOnlyList<ScoreSelection> selections)
        => selections.Where(s => s.Aggregate).Select(s => (s.Name, s.Index)).ToHashSet();

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
        ClassAssessment.ScoreSelections = ScoreSelectionDefaults.GenerateDefaults(firstStudent.Scores);

        var aggregateSet = BuildAggregateSet(ClassAssessment.ScoreSelections);
        foreach (var assessment in ClassAssessment.Assessments)
        {
            assessment.RecalculateAggregate(aggregateSet);
        }
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

        // Fire-and-forget: violin/correlation regen runs on a background task internally
        // (T03 will make these methods filter their seriesData by Display/Correlation).
        InitializeViolinPlotAsync();
        InitializeCorrelationPlotAsync();

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
                HoveredStudent = new StudentCardViewModel(student, grade, ClassAssessment.SeriesColorMap, () => _hoverDelayService.ClearHover(), BuildDisplayScores(student));
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
