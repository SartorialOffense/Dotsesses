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

    private GradeAssigner _gradeAssigner = null!;
    private CursorViewModel? _draggingCursor;
    private bool _isDraggingCursor;

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

    /// <summary>
    /// Exposes the hover delay service for debug display and clear command binding.
    /// </summary>
    public HoverDelayService HoverDelayService => _hoverDelayService;

    public MainWindowViewModel()
    {
        
    }
    
    public MainWindowViewModel(IMessenger messenger, ViolinPlotViewModel violinPlotViewModel, HoverDelayService hoverDelayService)
    {
        Log("MainWindowViewModel: Constructor started");

        _messenger = messenger;
        _violinPlotViewModel = violinPlotViewModel;
        _hoverDelayService = hoverDelayService;

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

        // Register for student edited messages to refresh plots
        _messenger.Register<StudentEditedMessage>(this, (r, m) =>
        {
            UpdateDotplotPoints();
            InitializeViolinPlot();
        });

        Log("MainWindowViewModel: Initializing with synthetic data");
        InitializeWithSyntheticData();

        Log("MainWindowViewModel: Initializing cursors");
        InitializeCursors();

        Log("MainWindowViewModel: Wiring cursors to violin plot");
        WireCursorsToViolinPlot();

        Log("MainWindowViewModel: Initializing compliance grid");
        InitializeComplianceGrid();

        Log("MainWindowViewModel: Recalculating grade counts for all enabled grades");
        RecalculateGradeCounts();

        Log("MainWindowViewModel: Initializing dotplot");
        InitializeDotplot();

        Log("MainWindowViewModel: Constructor completed (violin plot deferred)");
        // Defer violin plot initialization to avoid blocking UI on startup
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

            // Transform student assessment data into violin plot series format (CPU work, can be on background thread)
            var seriesData = new List<(string SeriesName, Dictionary<string, double> Scores)>();
            var firstStudent = ClassAssessment.Assessments.First();

            foreach (var score in firstStudent.Scores)
            {
                var seriesName = score.Index.HasValue ? $"{score.Name} {score.Index}" : score.Name;
                var seriesScores = new Dictionary<string, double>();

                foreach (var assessment in ClassAssessment.Assessments)
                {
                    var studentScore = assessment.Scores.FirstOrDefault(s =>
                        s.Name == score.Name && s.Index == score.Index);

                    if (studentScore != null)
                    {
                        seriesScores[$"S{assessment.Id:D3}"] = studentScore.Value;
                    }
                }

                seriesData.Add((seriesName, seriesScores));
            }

            var commentMap = ClassAssessment.Assessments.ToDictionary(
                a => a.Id,
                a => a.Comment ?? "");

            // Now update the ViewModel on the UI thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ViolinPlotViewModel.UpdateDataAndRegenerate(seriesData, commentMap, 3.0);
            });

            Log("MainWindowViewModel: Violin plot initialization completed");
        });
    }

    private void InitializeWithSyntheticData()
    {
        // Read real scores from Excel file
        const string scoresFilePath = "/Users/trumbjd/Dev/Dotsesses/Dotsesses/2024 Final Scores.xlsx";
        var scoreReader = new ScoreReader();
        var students = scoreReader.Read(scoresFilePath);

        var curveGenerator = new DefaultCurveGenerator();
        var defaultCurve = curveGenerator.GenerateRanges();

        // Use midpoints for initial cursor placement
        var midpointCurve = defaultCurve
            .Select(r => new CutoffCount(r.Grade, r.Midpoint))
            .ToList();

        var initialCutoffs = _initialCutoffCalculator.Calculate(students, midpointCurve);
        var current = _cutoffCountCalculator.Calculate(students, initialCutoffs);

        // Get MuppetName map from generator
        var muppetNameGenerator = new MuppetNameGenerator();
        var studentIds = students.Select(s => s.Id).OrderBy(id => id);
        var muppetNameMap = muppetNameGenerator.Generate(studentIds);

        ClassAssessment = new ClassAssessment(
            students,
            initialCutoffs,
            defaultCurve,
            current,
            muppetNameMap
        );

        _gradeAssigner = new GradeAssigner(initialCutoffs);
    }

    private void InitializeDotplot()
    {
        // Use transparent background so Avalonia's theme RegionColor shows through
        DotplotModel = new PlotModel
        {
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent,
            PlotAreaBorderThickness = new OxyThickness(1), // Full outline
            PlotAreaBorderColor = OxyColor.FromRgb(60, 60, 60), // Thin gray
            Padding = new OxyThickness(0), // Remove padding around plot area
            PlotMargins = new OxyThickness(0) // Remove margins around plot area
        };

        // Enable mouse events for point selection and cursor dragging
        DotplotModel.MouseDown += OnDotplotMouseDown;
        DotplotModel.MouseMove += OnDotplotMouseMove;
        DotplotModel.MouseUp += OnDotplotMouseUp;
        
        // Hook up to updated event to maintain fixed heights
        DotplotModel.Updated += (s, e) => UpdateAxisPositions();

        // Calculate score range with padding
        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var xPadding = 10;

        // Calculate Y-axis padding for Dot Display based on max students in a bin
        var scoreGroups = ClassAssessment.Assessments.GroupBy(a => a.AggregateGrade);
        var maxStudentsInBin = scoreGroups.Max(g => g.Count());
        var yPadding = maxStudentsInBin * 0.1;

        // Three-part layout with positioning (0=bottom, 1=top in OxyPlot)
        // Grade Cursors: bottom 25%
        // Dot Display: middle 60%
        // Statistics Display: top 15%
        
        double cursorStart = 0.0;
        double cursorEnd = 0.25;
        double dotStart = 0.25;
        double dotEnd = 0.85;
        double statsStart = 0.85;
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
            Minimum = 0,
            Maximum = 1,
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
            Minimum = -yPadding,
            Maximum = (maxStudentsInBin - 1) * 2 + yPadding,
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

        // Create comment map
        var commentMap = ClassAssessment.Assessments.ToDictionary(
            a => a.Id,
            a => a.Comment ?? "");

        // Update data and regenerate with stored display dimensions
        ViolinPlotViewModel.UpdateDataAndRegenerate(seriesData, commentMap, 3.0);
    }

    public void UpdateDotplotPoints()
    {
        // Clear existing series (keep axes)
        DotplotModel.Series.Clear();

        // Group students by aggregate score and stack vertically
        var scoreGroups = ClassAssessment.Assessments
            .GroupBy(a => a.AggregateGrade)
            .OrderBy(g => g.Key);

        // Fixed dot size
        const double markerSize = 2.0;

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

                var point = new ScatterPoint(group.Key, yPos, tag: $"{muppetName}\nScore: {student.AggregateGrade}");

                // Add to appropriate series based on whether student has a comment
                bool hasComment = !string.IsNullOrEmpty(student.Comment);
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
        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade) - 10;
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade) + 10;

        // Get axis positions for proper rendering
        var dotYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "DotY");
        var cursorYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "CursorY");

        if (dotYAxis == null || cursorYAxis == null) return;

        // ===== Thin Rectangle Around Cursor Area =====
        var cursorRect = new RectangleAnnotation
        {
            MinimumX = minScore,
            MaximumX = maxScore,
            MinimumY = 0,
            MaximumY = 1,
            Fill = OxyColors.Transparent,
            Stroke = OxyColor.FromRgb(60, 60, 60), // Thin gray border
            StrokeThickness = 1,
            XAxisKey = "SharedX",
            YAxisKey = "CursorY",
            Layer = AnnotationLayer.BelowSeries
        };
        DotplotModel.Annotations.Add(cursorRect);

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
                    // Lowest grade (worst): between left boundary and first cursor
                    labelX = (minScore + enabledCursors.First().Score) / 2;
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
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    TextHorizontalAlignment = HorizontalAlignment.Center,
                    TextVerticalAlignment = VerticalAlignment.Middle,
                    XAxisKey = "SharedX",
                    YAxisKey = "CursorY",
                    Stroke = OxyColors.Transparent,
                    StrokeThickness = 0
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
        
        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade) - 10;
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade) + 10;

        var lightGray = OxyColor.FromRgb(180, 180, 180);

        // ===== Thin Rectangle Around Stats Area =====
        var statsRect = new RectangleAnnotation
        {
            MinimumX = minScore,
            MaximumX = maxScore,
            MinimumY = 0,
            MaximumY = 1,
            Fill = OxyColors.Transparent,
            Stroke = OxyColor.FromRgb(60, 60, 60), // Thin gray border
            StrokeThickness = 1,
            XAxisKey = "SharedX",
            YAxisKey = "StatsY",
            Layer = AnnotationLayer.BelowSeries
        };
        DotplotModel.Annotations.Add(statsRect);

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
        var defaultGrades = ClassAssessment.DefaultCurve.Select(cc => cc.Grade).ToHashSet();

        // First pass: create all cursors, initially enabled
        // Note: Don't subscribe to PropertyChanged yet - DotplotModel isn't created yet
        foreach (var grade in allGrades)
        {
            var cutoff = ClassAssessment.CurrentCutoffs.FirstOrDefault(c => c.Grade.Equals(grade));
            int score = cutoff?.Score ?? 0; // Will be calculated for non-default grades below

            var cursor = new CursorViewModel(grade, score, isEnabled: true);
            Cursors.Add(cursor);
        }

        // Second pass: position grades without defined percentages (D, D-, F)
        // Place them 2 barbell heights below the previous grade
        var gradesWithoutDefault = allGrades.Where(g => !defaultGrades.Contains(g)).OrderBy(g => g.Order);

        if (gradesWithoutDefault.Any())
        {
            // Calculate cursor spacing: 2x barbell handle size (8px * 2 = 16px) converted to score units
            const double barbellHandlePixels = 8.0;
            const double spacingPixels = barbellHandlePixels * 2;
            var scoreRange = ClassAssessment.Assessments.Max(a => a.AggregateGrade) -
                            ClassAssessment.Assessments.Min(a => a.AggregateGrade);
            // Use a reasonable default plot height estimate
            const double estimatedPlotHeight = 400.0 * 0.8; // 80% of 400px
            var cursorSpacing = (int)Math.Ceiling(spacingPixels * scoreRange / Math.Max(1, estimatedPlotHeight));

            foreach (var grade in gradesWithoutDefault)
            {
                var cursor = Cursors.First(c => c.Grade.Equals(grade));

                // Find the cursor immediately above this one (lower Order = higher grade)
                var higherCursor = Cursors
                    .Where(c => c.Grade.Order < grade.Order)
                    .OrderByDescending(c => c.Grade.Order)
                    .FirstOrDefault();

                if (higherCursor != null)
                {
                    cursor.Score = higherCursor.Score - cursorSpacing;
                }
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

        if (e.PropertyName == nameof(CursorViewModel.Score))
        {
            // Cursor was moved (possibly from violin plot)
            _isUpdatingCursorsFromSubscription = true;
            try
            {
                UpdateCursors(); // Refresh dot plot annotations
                RecalculateGradeCounts(); // Update compliance grid counts
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
        if (value.HasValue)
        {
            var student = ClassAssessment.Assessments.FirstOrDefault(s => s.Id == value.Value);
            if (student != null)
            {
                var grade = GetGradeForStudent(student);
                HoveredStudent = new StudentCardViewModel(student, grade, () => _hoverDelayService.ClearHover());
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

        // Allow cursor movement beyond actual student scores
        var minBound = ClassAssessment.Assessments.Min(a => a.AggregateGrade) - 20;
        var maxBound = ClassAssessment.Assessments.Max(a => a.AggregateGrade) + 20;

        // Validate cursor movement (include ALL enabled cursors for proper ordering constraints)
        var allCutoffs = Cursors
            .Where(c => c.IsEnabled)
            .Select(c => new GradeCutoff(c.Grade, c == _draggingCursor ? newScore : c.Score))
            .ToList();

        var validatedScore = _cursorValidation.ValidateMovement(_draggingCursor.Grade, newScore, allCutoffs, (int)minBound, (int)maxBound);

        _isUpdatingCursorsFromSubscription = true;
        try
        {
            _draggingCursor.Score = validatedScore;
            UpdateCursors();
            RecalculateGradeCounts(); // Update counts during drag
        }
        finally
        {
            _isUpdatingCursorsFromSubscription = false;
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
}
