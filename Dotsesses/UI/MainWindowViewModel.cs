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

    private GradeAssigner _gradeAssigner = null!;

    /// <summary>
    /// Gets the current grade assigner for export purposes.
    /// </summary>
    public GradeAssigner GradeAssigner => _gradeAssigner;

    private CursorViewModel? _draggingCursor;
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
    /// ADR-0008). Drag, Compliance, and persistence migrate onto this
    /// in slices #3–#5; until then the legacy <c>_cursors</c> /
    /// <c>ClassAssessment.CurrentCutoffs</c> paths still drive the UI.
    /// </summary>
    [ObservableProperty]
    private GradingSession _gradingSession = null!;

    [ObservableProperty]
    private PlotModel _dotplotModel = null!;

    [ObservableProperty]
    private ObservableCollection<CursorViewModel> _cursors = null!;

    [ObservableProperty]
    private ObservableCollection<ComplianceRowViewModel> _complianceRows = null!;

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
    /// Mirrors the GradingSession's slot state into the legacy
    /// <see cref="Cursors"/> collection so existing OxyPlot rendering
    /// and Compliance recalc paths keep working while drag goes
    /// through the session. Called when GradingSession swaps in
    /// (file load) and on every session.LastChange notification.
    /// Slice 3 of issue #6 — minimal scope. Future cleanup slice will
    /// remove _cursors entirely (see issue #14).
    /// </summary>
    private void SyncCursorsFromSession()
    {
        if (GradingSession is null) return;
        foreach (var slot in GradingSession.Slots)
        {
            var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(slot.Grade));
            if (cursor is null) continue;
            if (cursor.Score != slot.Score) cursor.Score = slot.Score;
            if (cursor.IsEnabled != slot.IsEnabled) cursor.IsEnabled = slot.IsEnabled;
        }
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
        SyncCursorsFromSession();
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

        _cursors = new ObservableCollection<CursorViewModel>();
        _complianceRows = new ObservableCollection<ComplianceRowViewModel>();

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
        var students = scoreReader.Read(excelFilePath);

        var curveGenerator = new DefaultCurveGenerator();
        var defaultCurve = curveGenerator.GenerateRanges(students.Count);

        var midpointCurve = defaultCurve
            .Where(r => r.LowerBound > 0 || r.UpperBound > 0)
            .Select(r => new CutoffCount(r.Grade, r.Midpoint))
            .ToList();

        var initialCutoffs = _initialCutoffCalculator.Calculate(students, midpointCurve);
        var current = _cutoffCountCalculator.Calculate(students, initialCutoffs);

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
            initialCutoffs,
            defaultCurve,
            current,
            muppetNameMap,
            seriesColorMap
        );

        GradingSession = new GradingSession(
            ClassAssessment,
            new CursorPlacementCalculator(),
            _cursorValidation,
            _cutoffCountCalculator,
            _initialCutoffCalculator);

        SeedDefaultSelectionsIfEmpty();

        // Helper owns: recompute curves/cutoffs against post-seed aggregates, rebuild
        // _gradeAssigner, reset Cursors + ComplianceRows safely, and rewire the violin
        // plot. T02 will reuse the same helper from ApplyScoreSelections when an
        // aggregate-set change requires a cursor reset.
        Log("MainWindowViewModel: Seeding cursors from default curve");
        SeedCursorsFromDefaults();

        Log("MainWindowViewModel: Recalculating grade counts");
        RecalculateGradeCounts();

        Log("MainWindowViewModel: Initializing dotplot");
        InitializeDotplot();

        // Update the display filename
        SourceFileName = Path.GetFileName(excelFilePath);

        Log("MainWindowViewModel: Excel file loaded successfully");
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

        // Calculate score range with padding (20% on each side, like violin plot's -0.2 to 1.2)
        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var scoreRange = maxScore - minScore;
        var xPadding = scoreRange * 0.2;

        // Calculate Y-axis range for Dot Display based on max students in a bin
        // Use fixed buffer of 2 units on each side to handle single-student bins
        var scoreGroups = ClassAssessment.Assessments.GroupBy(a => a.AggregateGrade);
        var maxStudentsInBin = scoreGroups.Max(g => g.Count());
        var yMax = (maxStudentsInBin - 1) * 2;

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
        var sharedXAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Key = "SharedX",
            Minimum = minScore - xPadding,
            Maximum = maxScore + xPadding,
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
        var dotYAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "DotY",
            Minimum = -2,
            Maximum = yMax + 2,
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

        UpdateDotplotPoints();
        UpdateStatistics();
        UpdateCursors();
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

        var enabledCursors = Cursors.Where(c => c.IsEnabled).OrderBy(c => c.Score).ToList();
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
        // Skip the lowest grade (highest Order) - it has no cursor, just a label
        var lowestGrade = enabledCursors.OrderByDescending(c => c.Grade.Order).FirstOrDefault();
        foreach (var cursor in enabledCursors.Where(c => c != lowestGrade))
        {
            // Thin vertical line in the Dot area (behind dots)
            var line = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = cursor.Score,
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
                X = cursor.Score,
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
                X = cursor.Score,
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
        // Get all enabled grades sorted by order (best to worst)
        var enabledGrades = Cursors
            .Where(c => c.IsEnabled)
            .Select(c => c.Grade)
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
                    labelX = (enabledCursors.Last().Score + maxScore) / 2;
                }
                else if (i == enabledGrades.Count - 1)
                {
                    // Lowest grade (worst): between left boundary and first cursor WITH A LINE
                    // (The lowest grade's cursor doesn't have a line drawn, so use the next one)
                    var firstCursorWithLine = enabledCursors.Where(c => c != lowestGrade).OrderBy(c => c.Score).FirstOrDefault();
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
                    // Middle grades: between cursor for this grade and the next higher grade's cursor
                    // Find cursor for this grade (it's the lower bound)
                    var cursorForThisGrade = enabledCursors.FirstOrDefault(c => c.Grade.Order == grade.Order);
                    var cursorForNextGrade = enabledCursors.FirstOrDefault(c => c.Grade.Order == enabledGrades[i - 1].Order);
                    
                    if (cursorForThisGrade != null && cursorForNextGrade != null)
                    {
                        labelX = (cursorForThisGrade.Score + cursorForNextGrade.Score) / 2;
                    }
                    else
                    {
                        continue; // Skip if we can't find the cursors
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

    private bool _isUpdatingCursorsFromSubscription;

    private void InitializeCursors()
    {
        // Create cursors for ALL grades, all enabled at startup
        var allGrades = new DefaultCurveGenerator().GetAllGrades();

        // Grades with defined percentage ranges have non-zero bounds
        var gradesWithRanges = ClassAssessment.DefaultCurve
            .Where(cc => cc.LowerBound > 0 || cc.UpperBound > 0)
            .Select(cc => cc.Grade)
            .ToHashSet();

        // First pass: create all cursors, initially enabled
        // Note: Don't subscribe to PropertyChanged yet - DotplotModel isn't created yet
        foreach (var grade in allGrades)
        {
            var cutoff = ClassAssessment.CurrentCutoffs.FirstOrDefault(c => c.Grade.Equals(grade));
            int score = cutoff?.Score ?? 0; // Will be calculated for non-default grades below

            var cursor = new CursorViewModel(grade, score, isEnabled: true);
            Cursors.Add(cursor);
        }

        // Second pass: position grades without defined percentages (C-, D+, D, F)
        // Start at -0.25 of score range and stack upward, with lowest grade (F) at bottom
        var gradesWithoutRanges = allGrades.Where(g => !gradesWithRanges.Contains(g)).ToList();

        if (gradesWithoutRanges.Any())
        {
            // Calculate cursor spacing: 2x barbell handle size (8px * 2 = 16px) converted to score units
            const double barbellHandlePixels = 8.0;
            const double spacingPixels = barbellHandlePixels * 2;
            var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
            var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
            var scoreRange = maxScore - minScore;
            // Use a reasonable default plot height estimate
            const double estimatedPlotHeight = 400.0 * 0.8; // 80% of 400px
            var cursorSpacing = (int)Math.Ceiling(spacingPixels * scoreRange / Math.Max(1, estimatedPlotHeight));

            // Calculate base position at -0.25 of score range (below minimum score)
            var basePosition = minScore - (scoreRange * 0.25);

            // Sort grades by Order descending (F=10 first, then D=9, D+=8, C-=7, etc.)
            // This puts lowest grade at the bottom position
            var sortedGrades = gradesWithoutRanges.OrderByDescending(g => g.Order).ToList();

            // Position each grade starting from base, stacking upward
            for (int i = 0; i < sortedGrades.Count; i++)
            {
                var grade = sortedGrades[i];
                var cursor = Cursors.First(c => c.Grade.Equals(grade));
                cursor.Score = (int)Math.Round(basePosition + (i * cursorSpacing));
            }
        }

        // Subscribe to property changes after all positioning is done
        foreach (var cursor in Cursors)
        {
            cursor.PropertyChanged += OnCursorPropertyChanged;
        }
    }

    private void OnCursorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isUpdatingCursorsFromSubscription) return; // Prevent re-entrancy
        if (DotplotModel == null) return; // Not yet initialized

        if (e.PropertyName == nameof(CursorViewModel.Score) ||
            e.PropertyName == nameof(CursorViewModel.IsEnabled))
        {
            // Cursor was moved or enabled/disabled
            _isUpdatingCursorsFromSubscription = true;
            try
            {
                UpdateCursors(); // Refresh dot plot annotations
                RecalculateGradeCounts(); // Update compliance grid counts
                HasUnsavedChanges = true; // Mark as changed
            }
            finally
            {
                _isUpdatingCursorsFromSubscription = false;
            }
        }
    }

    private void WireCursorsToViolinPlot()
    {
        if (ViolinPlotViewModel == null) return;

        ViolinPlotViewModel.Cursors = Cursors;
        ViolinPlotViewModel.ComplianceRows = ComplianceRows;
        ViolinPlotViewModel.MinScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        ViolinPlotViewModel.MaxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
    }

    private void InitializeComplianceGrid()
    {
        var allGrades = new DefaultCurveGenerator().GetAllGrades();

        foreach (var grade in allGrades)
        {
            var defaultEntry = ClassAssessment.DefaultCurve.FirstOrDefault(cc => cc.Grade.Equals(grade));
            var currentEntry = ClassAssessment.Current.FirstOrDefault(cc => cc.Grade.Equals(grade));

            int lowerTarget = defaultEntry?.LowerBound ?? 0;
            int upperTarget = defaultEntry?.UpperBound ?? 0;
            int currentCount = currentEntry?.Count ?? 0;
            // All grades enabled at startup
            bool isEnabled = true;

            ComplianceRows.Add(new ComplianceRowViewModel(
                grade,
                lowerTarget,
                upperTarget,
                currentCount,
                isEnabled,
                OnComplianceCheckboxChanged
            ));
        }
    }

    /// <summary>
    /// Re-seeds cursor positions, compliance rows, and the supporting curve/cutoff/compliance
    /// state from the default-curve pipeline at the current student aggregate range.
    ///
    /// Owns the full default-curve seeding sequence that is shared by initial load
    /// (<see cref="LoadFromExcelFile"/>, <see cref="LoadStateAsync"/>) and — once T02 lands —
    /// by the aggregate-set-changed branch of <see cref="ApplyScoreSelections"/>.
    ///
    /// Safe to call repeatedly on the same VM: it unsubscribes <see cref="OnCursorPropertyChanged"/>
    /// from every existing cursor, clears <see cref="Cursors"/> and <see cref="ComplianceRows"/>,
    /// then re-runs the seeding pipeline so MEM023's append-not-clear gotcha cannot fire on
    /// repeated invocations. Mutation of cursors/compliance rows is guarded by
    /// <see cref="_isUpdatingCursorsFromSubscription"/> so any PropertyChanged signal that does
    /// slip through (e.g. during Score reassignment) does not re-enter <see cref="OnCursorPropertyChanged"/>
    /// and throw "Cutoffs are out of order" while the collection is mid-rebuild.
    ///
    /// Pipeline order (matches the inline code that previously lived in LoadFromExcelFile/LoadStateAsync):
    /// 1. Recompute defaultCurve via <see cref="DefaultCurveGenerator.GenerateRanges"/> for the
    ///    current student count.
    /// 2. Project to midpoint-targeted curve using <see cref="CutoffCount"/>.
    /// 3. Compute initialCutoffs via <see cref="InitialCutoffCalculator.Calculate"/>.
    /// 4. Compute current grade counts via <see cref="CutoffCountCalculator.Calculate"/>.
    /// 5. Push CurrentCutoffs and Current onto <see cref="ClassAssessment"/>. (DefaultCurve is
    ///    immutable once <see cref="ClassAssessment"/> is constructed; both load paths build it
    ///    correctly via the constructor, so no update is needed here.)
    /// 6. Rebuild <see cref="_gradeAssigner"/>.
    /// 7. Reset cursors safely (unsubscribe -> clear -> re-add -> reposition non-range grades ->
    ///    re-subscribe).
    /// 8. Reset compliance rows (clear -> re-init).
    /// 9. Rewire violin plot references / score range.
    /// </summary>
    private void SeedCursorsFromDefaults()
    {
        // 1-2: Default curve and midpoint projection.
        var defaultCurve = new DefaultCurveGenerator().GenerateRanges(ClassAssessment.Assessments.Count);
        var midpointCurve = defaultCurve
            .Where(r => r.LowerBound > 0 || r.UpperBound > 0)
            .Select(r => new CutoffCount(r.Grade, r.Midpoint))
            .ToList();

        // 3-4: Compute initialCutoffs and current grade distribution against the live
        //      AggregateGrade values on ClassAssessment.Assessments.
        var initialCutoffs = _initialCutoffCalculator.Calculate(ClassAssessment.Assessments, midpointCurve);
        var current = _cutoffCountCalculator.Calculate(ClassAssessment.Assessments, initialCutoffs);

        // 5: Push the recomputed grids back onto ClassAssessment. DefaultCurve is read-only
        //    on the model and was already populated by the ClassAssessment constructor in
        //    both load paths, so we leave it alone here. T02 will revisit if the
        //    aggregate-set-changed branch needs to mutate it.
        ClassAssessment.CurrentCutoffs = initialCutoffs;
        ClassAssessment.Current = current;

        // 6: Rebuild the grade assigner so any subsequent grade lookups see the new cutoffs.
        _gradeAssigner = new GradeAssigner(initialCutoffs);

        // 7: Reset cursors. Guard with _isUpdatingCursorsFromSubscription so no stray
        //    PropertyChanged event re-enters OnCursorPropertyChanged while the collection
        //    is being rebuilt (MEM023 — re-entrancy can throw "Cutoffs are out of order"
        //    when a partially-rebuilt cursor list contains both old and new cursors for
        //    the same grade).
        var wasUpdating = _isUpdatingCursorsFromSubscription;
        _isUpdatingCursorsFromSubscription = true;
        try
        {
            // Unsubscribe BEFORE Clear so no late PropertyChanged on a removed cursor fires
            // back into our handler.
            foreach (var cursor in Cursors)
            {
                cursor.PropertyChanged -= OnCursorPropertyChanged;
            }
            Cursors.Clear();

            // 8: Reset compliance rows up-front; InitializeComplianceGrid below appends and
            //    is safe now that we cleared first.
            ComplianceRows.Clear();

            // Re-run the cursor seeding pipeline (the InitializeCursors body, inlined so the
            // post-clear/pre-resubscribe sequencing stays inside this guard).
            var allGrades = new DefaultCurveGenerator().GetAllGrades();

            var gradesWithRanges = ClassAssessment.DefaultCurve
                .Where(cc => cc.LowerBound > 0 || cc.UpperBound > 0)
                .Select(cc => cc.Grade)
                .ToHashSet();

            foreach (var grade in allGrades)
            {
                var cutoff = ClassAssessment.CurrentCutoffs.FirstOrDefault(c => c.Grade.Equals(grade));
                int score = cutoff?.Score ?? 0; // Will be repositioned below for non-default grades.
                var cursor = new CursorViewModel(grade, score, isEnabled: true);
                Cursors.Add(cursor);
            }

            // Second pass: position grades without defined percentages (C-, D+, D, F).
            var gradesWithoutRanges = allGrades.Where(g => !gradesWithRanges.Contains(g)).ToList();
            if (gradesWithoutRanges.Any())
            {
                const double barbellHandlePixels = 8.0;
                const double spacingPixels = barbellHandlePixels * 2;
                var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
                var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
                var scoreRange = maxScore - minScore;
                const double estimatedPlotHeight = 400.0 * 0.8;
                var cursorSpacing = (int)Math.Ceiling(spacingPixels * scoreRange / Math.Max(1, estimatedPlotHeight));

                var basePosition = minScore - (scoreRange * 0.25);
                var sortedGrades = gradesWithoutRanges.OrderByDescending(g => g.Order).ToList();

                for (int i = 0; i < sortedGrades.Count; i++)
                {
                    var grade = sortedGrades[i];
                    var cursor = Cursors.First(c => c.Grade.Equals(grade));
                    cursor.Score = (int)Math.Round(basePosition + (i * cursorSpacing));
                }
            }

            // M002/S05/T03 defensive fallback: if the combined first-pass (range-driven) +
            // second-pass (no-range catch-all) cursor positions produce a non-monotonic
            // sequence by Grade.Order, GradeAssigner..ctor will throw "Cutoffs are out of
            // order" on the next RecalculateGradeCounts. This happens on narrow aggregate
            // ranges — e.g. when the user reduces the Aggregate selection to a single
            // narrow non-Total component (SC1 Case 7 repro: aggregate range collapsed to
            // 0–10, no way to fit 13 monotonic cutoffs). Fall back to even spacing across
            // [minScore, maxScore] mirroring MEM028's Count > 0 guard for the empty case.
            if (!AreCursorsMonotonicByGrade())
            {
                Log("MainWindowViewModel: SeedCursorsFromDefaults — non-monotonic cursors " +
                    "after default-curve placement (narrow aggregate range); falling back to " +
                    "even spacing across [minScore, maxScore]");
                ApplyEvenSpacingFallback();
            }

            // 8 (continued): rebuild the compliance grid from the freshly-cleared list.
            InitializeComplianceGrid();

            // Re-subscribe AFTER all positioning is complete and AFTER ComplianceRows
            // is rebuilt, so the first PropertyChanged event from a user drag in the
            // future doesn't try to recalculate against a half-formed grid.
            foreach (var cursor in Cursors)
            {
                cursor.PropertyChanged += OnCursorPropertyChanged;
            }
        }
        finally
        {
            _isUpdatingCursorsFromSubscription = wasUpdating;
        }

        // 9: Wire the (possibly new) Cursors / ComplianceRows references and refreshed
        //    MinScore/MaxScore through to the violin plot.
        WireCursorsToViolinPlot();
    }

    /// <summary>
    /// Returns true when every cursor's Score is monotonically non-increasing as Grade.Order
    /// increases (i.e. better grades have ≥ scores than worse grades). GradeAssigner..ctor
    /// rejects any violation, so this predicate gates the T03 even-spacing fallback.
    /// </summary>
    private bool AreCursorsMonotonicByGrade()
    {
        var sorted = Cursors.OrderBy(c => c.Grade.Order).ToList();
        for (int i = 0; i < sorted.Count - 1; i++)
        {
            // Mirror GradeAssigner.ValidateCutoffOrdering — strict less-than fails the check;
            // equal scores are tolerated.
            if (sorted[i].Score < sorted[i + 1].Score) return false;
        }
        return true;
    }

    /// <summary>
    /// Defensive fallback for narrow aggregate ranges (M002/S05/T03). Replaces every cursor's
    /// Score with an even-spacing layout across the current [minScore, maxScore] derived from
    /// ClassAssessment.Assessments. Uses CursorPlacementCalculator.ResetToEvenSpacingMonotonic
    /// so best grade lands at maxScore, worst at minScore, and intermediates spread linearly.
    /// </summary>
    private void ApplyEvenSpacingFallback()
    {
        if (Cursors.Count == 0) return;
        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var grades = Cursors.Select(c => c.Grade).ToList();
        var rebalanced = new CursorPlacementCalculator()
            .ResetToEvenSpacingMonotonic(grades, minScore, maxScore);
        foreach (var cutoff in rebalanced)
        {
            var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(cutoff.Grade));
            if (cursor != null)
            {
                cursor.Score = cutoff.Score;
            }
        }
        // Mirror back into ClassAssessment.CurrentCutoffs so RecalculateGradeCounts (which
        // rebuilds from Cursors anyway) and any downstream readers see the rebalanced values.
        ClassAssessment.CurrentCutoffs = Cursors
            .Select(c => new GradeCutoff(c.Grade, c.Score))
            .ToList();
    }

    private void OnComplianceCheckboxChanged()
    {
        UpdateCursorsFromComplianceGrid();
        RecalculateGradeCounts();
        UpdateDotplotPoints();
    }

    private void UpdateCursorsFromComplianceGrid()
    {
        // Track which grades were newly enabled (before changing IsEnabled)
        var newlyEnabledGrades = new List<Grade>();

        // First pass: identify what changed without modifying state
        foreach (var row in ComplianceRows)
        {
            var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(row.Grade));
            if (cursor != null && row.IsEnabled && !cursor.IsEnabled)
            {
                newlyEnabledGrades.Add(cursor.Grade);
            }
        }

        // Capture the currently positioned grades BEFORE enabling new ones
        var positionedGrades = new HashSet<Grade>(
            Cursors.Where(c => c.IsEnabled).Select(c => c.Grade));

        // Second pass: update IsEnabled state
        foreach (var row in ComplianceRows)
        {
            var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(row.Grade));
            if (cursor != null && cursor.IsEnabled != row.IsEnabled)
            {
                cursor.IsEnabled = row.IsEnabled;
            }
        }

        // If grades were enabled, position them relative to existing cursors
        if (newlyEnabledGrades.Any())
        {
            // Calculate cursor spacing: 2x barbell handle size (8px * 2 = 16px) converted to score units
            const double barbellHandlePixels = 8.0;
            const double spacingPixels = barbellHandlePixels * 2;
            var scoreRange = ViolinPlotViewModel?.MaxScore - ViolinPlotViewModel?.MinScore ?? 100;
            var plotAreaFraction = (ViolinPlotViewModel?.GetPlotAreaBottomFraction() ?? 0.9) -
                                   (ViolinPlotViewModel?.GetPlotAreaTopFraction() ?? 0.1);
            var estimatedPlotHeight = 400.0 * plotAreaFraction;
            var cursorSpacing = (int)Math.Ceiling(spacingPixels * scoreRange / Math.Max(1, estimatedPlotHeight));

            // Process new grades in order (top grades first)
            foreach (var newGrade in newlyEnabledGrades.OrderBy(g => g.Order))
            {
                var newCursor = Cursors.First(c => c.Grade.Equals(newGrade));

                // Find adjacent positioned cursors
                var higherCursor = Cursors
                    .Where(c => positionedGrades.Contains(c.Grade) && c.Grade.Order < newGrade.Order)
                    .OrderByDescending(c => c.Grade.Order)
                    .FirstOrDefault();

                var lowerCursor = Cursors
                    .Where(c => positionedGrades.Contains(c.Grade) && c.Grade.Order > newGrade.Order)
                    .OrderBy(c => c.Grade.Order)
                    .FirstOrDefault();

                if (higherCursor != null)
                {
                    // Place new cursor one spacing below the higher cursor
                    newCursor.Score = higherCursor.Score - cursorSpacing;

                    // Reposition all cursors below the new one to be evenly spaced
                    var currentScore = newCursor.Score;
                    foreach (var cursor in Cursors
                        .Where(c => c.IsEnabled && c.Grade.Order > newGrade.Order)
                        .OrderBy(c => c.Grade.Order))
                    {
                        currentScore -= cursorSpacing;
                        cursor.Score = currentScore;
                    }
                }
                else if (lowerCursor != null)
                {
                    // Adding at top - place above the highest positioned cursor
                    var highestScore = Cursors
                        .Where(c => positionedGrades.Contains(c.Grade))
                        .Max(c => c.Score);
                    newCursor.Score = highestScore + cursorSpacing;
                }
                else
                {
                    // First cursor - use middle of score range
                    var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
                    var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
                    newCursor.Score = (minScore + maxScore) / 2;
                }

                // Mark this grade as positioned for subsequent iterations
                positionedGrades.Add(newGrade);
            }
        }

        UpdateCursors();
    }

    private void RecalculateGradeCounts()
    {
        // Build cutoffs from enabled cursors
        var enabledCutoffs = Cursors
            .Where(c => c.IsEnabled)
            .Select(c => new GradeCutoff(c.Grade, c.Score))
            .ToList();

        ClassAssessment.CurrentCutoffs = enabledCutoffs;
        _gradeAssigner = new GradeAssigner(enabledCutoffs);
        var newCurrent = _cutoffCountCalculator.Calculate(ClassAssessment.Assessments, enabledCutoffs);
        ClassAssessment.Current = newCurrent;

        // Update compliance grid with new counts
        foreach (var row in ComplianceRows)
        {
            var currentEntry = ClassAssessment.Current.FirstOrDefault(cc => cc.Grade.Equals(row.Grade));
            row.CurrentCount = currentEntry?.Count ?? 0;
        }
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

            var midpointCurve = defaultCurve
                .Where(r => r.LowerBound > 0 || r.UpperBound > 0)
                .Select(r => new CutoffCount(r.Grade, r.Midpoint))
                .ToList();

            var initialCutoffs = _initialCutoffCalculator.Calculate(students, midpointCurve);
            var current = _cutoffCountCalculator.Calculate(students, initialCutoffs);

            // Generate series color map from first student's scores
            var firstStudent = students.First();
            var seriesNames = firstStudent.Scores
                .Select(s => s.Index.HasValue ? $"{s.Name} {s.Index}" : s.Name)
                .ToList();
            var seriesColorMap = SeriesColorService.GenerateColorMap(seriesNames);

            ClassAssessment = new ClassAssessment(
                students,
                initialCutoffs,
                defaultCurve,
                current,
                muppetMap,
                seriesColorMap);

            GradingSession = new GradingSession(
                ClassAssessment,
                new CursorPlacementCalculator(),
                _cursorValidation,
                _cutoffCountCalculator,
                _initialCutoffCalculator);

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

            // Helper owns: recompute curves/cutoffs against post-seed aggregates,
            // build _gradeAssigner, reset Cursors + ComplianceRows safely, and rewire
            // the violin plot. ApplyCursors below overlays any saved cursor positions
            // on top of the freshly-seeded defaults so a .dots load keeps user state.
            Log("LoadStateAsync: Seeding cursors from default curve");
            SeedCursorsFromDefaults();

            // Apply saved cursor positions AFTER the helper has populated Cursors
            // with the default-seeded entries so saved positions overlay the defaults.
            _stateService.ApplyCursors(state, Cursors);

            Log("LoadStateAsync: Recalculating grade counts");
            RecalculateGradeCounts();

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

        // Order matters: the per-student RecalculateAggregate loop above MUST run before
        // SeedCursorsFromDefaults because the helper reads ClassAssessment.Assessments and
        // computes new cutoffs against their freshly-updated AggregateGrade values.
        // RecalculateGradeCounts (called below) MUST run AFTER the reset so it sees the
        // freshly-seeded cursors, not the stale ones.
        //
        // Edge case: when newAggregateKeys is empty, every student's AggregateGrade collapses
        // to 0. Running the seed helper in that state would have InitialCutoffCalculator
        // produce a mix of zero cutoffs (for grades whose target count is filled by zero-
        // aggregate students) and stepping-down catch-all cutoffs (-12, -24, …) for grades
        // past the last student, which then violates the "better grade ≥ worse grade"
        // ordering invariant inside GradeAssigner and throws "Cutoffs are out of order"
        // (per ApplyScoreSelections_WithEmptySelections_DoesNotCrash). The empty-aggregate
        // configuration is itself a degenerate state — there is no meaningful cursor placement
        // to be made — so we skip the reset and leave the existing cursors in place. Cursors
        // remain visible so the user can re-add aggregate components and recover.
        if (aggregateSetChanged && newAggregateKeys.Count > 0)
        {
            Log("MainWindowViewModel: ApplyScoreSelections — aggregate set changed, re-seeding cursors from default curve");
            SeedCursorsFromDefaults();
        }

        RecalculateGradeCounts();
        UpdateDotplotPoints();

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
        var grade = _gradeAssigner.AssignGrade(student.AggregateGrade);
        return grade.DisplayName;
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
        if (cursorYAxis != null && nearestCursor.cursor != null && nearestCursor.distance < 3)
        {
            // Transform the Y position using the cursor Y axis
            var cursorY = cursorYAxis.InverseTransform(e.Position.Y);

            // Only allow cursor dragging if clicking in the Grade Cursors area
            if (cursorY >= cursorYAxis.Minimum && cursorY <= cursorYAxis.Maximum)
            {
                // Start dragging cursor
                _draggingCursor = nearestCursor.cursor;
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

            if (cursorYAxis != null && nearestCursor.cursor != null && nearestCursor.distance < 3)
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

        // Pre-clamp to legacy bounds so the visual continues to track the
        // mouse smoothly past the data envelope. Session.MoveCutoff applies
        // its own canonical bounds (see ScoreBoundsMargin{Below,Above} in
        // GradingSession); rejected moves leave the cursor at its last
        // valid position, which gives users implicit boundary feedback.
        var legacyMinBound = ClassAssessment.Assessments.Min(a => a.AggregateGrade) - 20;
        var legacyMaxBound = ClassAssessment.Assessments.Max(a => a.AggregateGrade) + 20;
        var allCutoffs = Cursors
            .Where(c => c.IsEnabled)
            .Select(c => new GradeCutoff(c.Grade, c == _draggingCursor ? newScore : c.Score))
            .ToList();
        var clampedScore = _cursorValidation.ValidateMovement(
            _draggingCursor.Grade, newScore, allCutoffs, (int)legacyMinBound, (int)legacyMaxBound);

        // Slice 3: route the commit through GradingSession. The session
        // emits LastChange on success → SyncCursorsFromSession mirrors
        // back into _cursors → existing cursor PropertyChanged handlers
        // update OxyPlot annotations and Compliance counts.
        // Guard against the legacy `Cursors` collection containing
        // non-draggable grades (the structural catch-all and any
        // zero-range grades that aren't in `session.Slots`). The session
        // throws ArgumentException for those (per ADR-0011); the drag
        // handler must not propagate that to OxyPlot's event pump.
        if (GradingSession is not null
            && GradingSession.Slots.Any(s => s.Grade.Equals(_draggingCursor.Grade)))
        {
            GradingSession.MoveCutoff(_draggingCursor.Grade, clampedScore, this);
        }
        e.Handled = true;
    }

    private void OnDotplotMouseUp(object? sender, OxyMouseEventArgs e)
    {
        if (_isDraggingCursor && _draggingCursor != null)
        {
            // Finalize cursor drag - include all enabled cursors for count calculation
            var updatedCutoffs = Cursors
                .Where(c => c.IsEnabled)
                .Select(c => new GradeCutoff(c.Grade, c.Score))
                .ToList();

            ClassAssessment.CurrentCutoffs = updatedCutoffs;
            _gradeAssigner = new GradeAssigner(updatedCutoffs);
            var newCurrent = _cutoffCountCalculator.Calculate(ClassAssessment.Assessments, updatedCutoffs);
            ClassAssessment.Current = newCurrent;

            // Update compliance grid
            foreach (var row in ComplianceRows)
            {
                var currentEntry = ClassAssessment.Current.FirstOrDefault(cc => cc.Grade.Equals(row.Grade));
                if (currentEntry != null)
                {
                    row.CurrentCount = currentEntry.Count;
                }
            }

            _isDraggingCursor = false;
            _draggingCursor = null;
            e.Handled = true;
        }
    }

    private (CursorViewModel? cursor, double distance) FindNearestCursor(double xPos)
    {
        CursorViewModel? nearest = null;
        double minDistance = double.MaxValue;

        // Exclude the lowest grade (highest Order) - it has no draggable cursor
        var lowestGrade = Cursors.Where(c => c.IsEnabled).OrderByDescending(c => c.Grade.Order).FirstOrDefault();
        foreach (var cursor in Cursors.Where(c => c.IsEnabled && c != lowestGrade))
        {
            double distance = Math.Abs(cursor.Score - xPos);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = cursor;
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
