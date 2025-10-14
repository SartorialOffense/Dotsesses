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
    private bool _isColorSelectionPaneOpen = false;

    [ObservableProperty]
    private bool _isSizePaneOpen = false;

    [ObservableProperty]
    private bool _isDrillDownPaneOpen = true;

    [ObservableProperty]
    private bool _isViolinPaneOpen = true;

    [ObservableProperty]
    private string? _selectedColorAttribute;

    [ObservableProperty]
    private ViolinPlotViewModel? _violinPlotViewModel;

    [ObservableProperty]
    private ObservableCollection<ColorLegendItem> _colorLegend = new();

    [ObservableProperty]
    private double _dotSize = 2.0;

    public List<string> AvailableColorAttributes { get; private set; } = new();

    public MainWindowViewModel()
    {
        
    }
    
    public MainWindowViewModel(IMessenger messenger, ViolinPlotViewModel violinPlotViewModel)
    {
        Log("MainWindowViewModel: Constructor started");

        _messenger = messenger;
        _violinPlotViewModel = violinPlotViewModel;

        Log("MainWindowViewModel: Creating calculators");
        _cutoffCountCalculator = new CutoffCountCalculator();
        _initialCutoffCalculator = new InitialCutoffCalculator();
        _cursorValidation = new CursorValidation();

        _cursors = new ObservableCollection<CursorViewModel>();
        _complianceRows = new ObservableCollection<ComplianceRowViewModel>();

        Log("MainWindowViewModel: Registering message handlers");
        // Register for hover messages from violin plot
        _messenger.Register<StudentHoverMessage>(this, (r, m) =>
        {
            if (m.Source != "dotplot") // Only respond to violin messages
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

        Log("MainWindowViewModel: Initializing compliance grid");
        InitializeComplianceGrid();

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
        var generator = new SyntheticStudentGenerator();
        var students = generator.Generate();

        var curveGenerator = new DefaultCurveGenerator();
        var defaultCurve = curveGenerator.Generate();

        var initialCutoffs = _initialCutoffCalculator.Calculate(students, defaultCurve);
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
        
        // Initialize available color attributes
        AvailableColorAttributes = new List<string> { "[None]" };
        var attributeNames = ClassAssessment.Assessments
            .SelectMany(a => a.Attributes)
            .Select(attr => attr.Name)
            .Distinct()
            .OrderBy(name => name);
        AvailableColorAttributes.AddRange(attributeNames);
        
        // Set default to [None]
        SelectedColorAttribute = "[None]";
    }

    private void InitializeDotplot()
    {
        var backgroundColor = OxyColor.FromRgb(0, 0, 0);

        DotplotModel = new PlotModel
        {
            Background = backgroundColor,
            PlotAreaBackground = backgroundColor,
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

        // Use dynamic dot size from slider
        var markerSize = DotSize;

        // Check if we're coloring by attribute
        bool colorByAttribute = !string.IsNullOrEmpty(SelectedColorAttribute) && SelectedColorAttribute != "[None]";

        if (colorByAttribute)
        {
            // Create separate series for each attribute value, split by marker type
            var seriesByValueCircle = new Dictionary<string, ScatterSeries>();
            var seriesByValueSquare = new Dictionary<string, ScatterSeries>();

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

                    // Get the attribute value for this student
                    var attributeValue = student.Attributes
                        .FirstOrDefault(attr => attr.Name == SelectedColorAttribute)?.Value ?? "Unknown";

                    // Determine if student has a comment
                    bool hasComment = !string.IsNullOrEmpty(student.Comment);
                    var seriesByValue = hasComment ? seriesByValueSquare : seriesByValueCircle;
                    var markerType = hasComment ? MarkerType.Square : MarkerType.Circle;

                    // Get or create series for this value and marker type
                    if (!seriesByValue.ContainsKey(attributeValue))
                    {
                        var color = GetOxyColorForValue(attributeValue);
                        seriesByValue[attributeValue] = new ScatterSeries
                        {
                            MarkerType = markerType,
                            MarkerSize = markerSize,
                            MarkerFill = hasComment ? OxyColors.Transparent : color,
                            MarkerStroke = color,
                            MarkerStrokeThickness = hasComment ? 1.5 : 0.5,
                            XAxisKey = "SharedX",
                            YAxisKey = "DotY",
                            TrackerFormatString = ""
                        };
                    }

                    seriesByValue[attributeValue].Points.Add(point);
                }
            }

            // Add colored series (circles then squares)
            foreach (var series in seriesByValueCircle.Values)
            {
                DotplotModel.Series.Add(series);
            }
            foreach (var series in seriesByValueSquare.Values)
            {
                DotplotModel.Series.Add(series);
            }
        }
        else
        {
            // Original white dots behavior - separate series for circles and squares
            var dotSeriesCircle = new ScatterSeries
            {
                MarkerType = MarkerType.Circle,
                MarkerSize = markerSize,
                MarkerFill = OxyColors.White,
                MarkerStroke = OxyColors.White,
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
                MarkerStroke = OxyColors.White,
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
        }

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

        // ===== Grade Region Bands in Dot Display =====
        // Alternating pattern: transparent and light gray
        // Only use cursors that have visible lines (exclude lowest grade)
        var lowestGradeForRegions = enabledCursors.OrderByDescending(c => c.Grade.Order).FirstOrDefault();
        var cursorsWithLines = enabledCursors.Where(c => c != lowestGradeForRegions).OrderBy(c => c.Score).ToList();
        
        if (cursorsWithLines.Any())
        {
            var grayColor = OxyColor.FromArgb(0x20, 255, 255, 255); // White with alpha 0x20
            var clearColor = OxyColors.Transparent;

            // Create regions from left boundary to first cursor, between cursors, and last cursor to right
            var regions = new List<(double left, double right, bool isGray)>();

            // First region: left boundary to first visible cursor
            regions.Add((minScore, cursorsWithLines[0].Score, false)); // Start with clear

            // Between cursors
            for (int i = 0; i < cursorsWithLines.Count - 1; i++)
            {
                bool isGray = (i + 1) % 2 == 1; // Alternate starting with gray for second region
                regions.Add((cursorsWithLines[i].Score, cursorsWithLines[i + 1].Score, isGray));
            }

            // Last region: last cursor to right boundary
            bool lastIsGray = cursorsWithLines.Count % 2 == 1;
            regions.Add((cursorsWithLines.Last().Score, maxScore, lastIsGray));

            // Draw region bands
            foreach (var (left, right, isGray) in regions)
            {
                // Extend boundaries slightly to be flush with cursor lines (StrokeThickness=2)
                var extendedLeft = left - 0.5;
                var extendedRight = right + 0.5; // Extend edges to align with cursor
                
                var rect = new RectangleAnnotation
                {
                    MinimumX = extendedLeft,
                    MaximumX = extendedRight,
                    MinimumY = dotYAxis.Minimum,
                    MaximumY = dotYAxis.Maximum,
                    Fill = isGray ? grayColor : clearColor,
                    Layer = AnnotationLayer.BelowSeries,
                    XAxisKey = "SharedX",
                    YAxisKey = "DotY",
                    Selectable = false
                };
                DotplotModel.Annotations.Add(rect);
            }
        }

        // ===== Vertical Cursors in Grade Cursors Area =====
        // Skip the lowest grade (highest Order) - it has no cursor, just a label
        var lowestGrade = enabledCursors.OrderByDescending(c => c.Grade.Order).FirstOrDefault();
        foreach (var cursor in enabledCursors.Where(c => c != lowestGrade))
        {
            var line = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = cursor.Score,
                Color = OxyColor.FromRgb(255, 255, 255),
                LineStyle = LineStyle.Dash,
                StrokeThickness = 2,
                XAxisKey = "SharedX",
                YAxisKey = "CursorY",
                MinimumY = 0,
                MaximumY = 1
            };
            DotplotModel.Annotations.Add(line);
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

    private void InitializeCursors()
    {
        // Create cursors for ALL grades, enabled based on DefaultCurve
        var allGrades = new DefaultCurveGenerator().GetAllGrades();
        var defaultGrades = ClassAssessment.DefaultCurve.Select(cc => cc.Grade).ToHashSet();

        foreach (var grade in allGrades)
        {
            var cutoff = ClassAssessment.CurrentCutoffs.FirstOrDefault(c => c.Grade.Equals(grade));
            bool isEnabled = defaultGrades.Contains(grade);
            int score = cutoff?.Score ?? 0; // Will be calculated when enabled

            Cursors.Add(new CursorViewModel(grade, score, isEnabled));
        }
    }

    private void InitializeComplianceGrid()
    {
        var allGrades = new DefaultCurveGenerator().GetAllGrades();

        foreach (var grade in allGrades)
        {
            var defaultEntry = ClassAssessment.DefaultCurve.FirstOrDefault(cc => cc.Grade.Equals(grade));
            var currentEntry = ClassAssessment.Current.FirstOrDefault(cc => cc.Grade.Equals(grade));

            int targetCount = defaultEntry?.Count ?? 0;
            int currentCount = currentEntry?.Count ?? 0;
            bool isEnabled = defaultEntry != null;

            ComplianceRows.Add(new ComplianceRowViewModel(
                grade,
                targetCount,
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
        // Track if any cursors were enabled/disabled
        bool cursorsChanged = false;

        foreach (var row in ComplianceRows)
        {
            var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(row.Grade));
            if (cursor != null && cursor.IsEnabled != row.IsEnabled)
            {
                cursor.IsEnabled = row.IsEnabled;
                cursorsChanged = true;
            }
        }

        // If cursors changed, recalculate ALL cursor positions from scratch
        if (cursorsChanged)
        {
            var enabledGrades = Cursors.Where(c => c.IsEnabled).Select(c => c.Grade).ToList();
            var totalStudents = ClassAssessment.Assessments.Count;
            var studentsPerGrade = totalStudents / Math.Max(1, enabledGrades.Count);
            
            // Build curve: use DefaultCurve counts where available, otherwise distribute evenly
            var enabledCurve = new List<CutoffCount>();
            foreach (var grade in enabledGrades.OrderBy(g => g.Order))
            {
                var defaultEntry = ClassAssessment.DefaultCurve.FirstOrDefault(cc => cc.Grade.Equals(grade));
                var count = defaultEntry?.Count ?? studentsPerGrade;
                enabledCurve.Add(new CutoffCount(grade, count));
            }

            // Recalculate all cutoff positions
            var newCutoffs = _initialCutoffCalculator.Calculate(ClassAssessment.Assessments, enabledCurve);

            // Get valid drag bounds to ensure cursors are placed within draggable range
            var minBound = ClassAssessment.Assessments.Min(a => a.AggregateGrade) - 1;
            var maxBound = ClassAssessment.Assessments.Max(a => a.AggregateGrade) + 1;

            // Update all enabled cursor positions, clamped to valid range
            foreach (var cutoff in newCutoffs)
            {
                var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(cutoff.Grade));
                if (cursor != null)
                {
                    // Clamp score to valid dragging bounds
                    cursor.Score = Math.Clamp(cutoff.Score, minBound, maxBound);
                }
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
    private void ToggleColorSelectionPane()
    {
        IsColorSelectionPaneOpen = !IsColorSelectionPaneOpen;
    }

    [RelayCommand]
    private void ToggleSizePane()
    {
        IsSizePaneOpen = !IsSizePaneOpen;
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

    partial void OnHoveredStudentIdChanged(int? value)
    {
        if (value.HasValue)
        {
            var student = ClassAssessment.Assessments.FirstOrDefault(s => s.Id == value.Value);
            if (student != null)
            {
                var grade = GetGradeForStudent(student);
                HoveredStudent = new StudentCardViewModel(student, grade);
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

            // Check for student hover
            var student = FindNearestStudent(e.Position);
            int? newHoveredId = student?.Id;

            if (newHoveredId != HoveredStudentId)
            {
                HoveredStudentId = newHoveredId;

                // Broadcast hover message to violin plot
                _messenger.Send(new StudentHoverMessage(
                    HoveredStudentId,
                    "dotplot",
                    null));
            }

            e.Handled = true;
            return;
        }

        var newScore = (int)Math.Round(pos.X);

        // Limit cursor movement to within 1 of actual student scores
        var minBound = ClassAssessment.Assessments.Min(a => a.AggregateGrade) - 1;
        var maxBound = ClassAssessment.Assessments.Max(a => a.AggregateGrade) + 1;

        // Validate cursor movement (include ALL enabled cursors for proper ordering constraints)
        var allCutoffs = Cursors
            .Where(c => c.IsEnabled)
            .Select(c => new GradeCutoff(c.Grade, c == _draggingCursor ? newScore : c.Score))
            .ToList();

        var validatedScore = _cursorValidation.ValidateMovement(_draggingCursor.Grade, newScore, allCutoffs, (int)minBound, (int)maxBound);

        _draggingCursor.Score = validatedScore;
        UpdateCursors();
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


    partial void OnSelectedColorAttributeChanged(string? value)
    {
        UpdateColorLegend();
        
        // Only update dotplot if it's been initialized
        if (DotplotModel != null)
        {
            UpdateDotplotPoints();
        }
    }

    partial void OnDotSizeChanged(double value)
    {
        // Only update dotplot if it's been initialized
        if (DotplotModel != null)
        {
            UpdateDotplotPoints();
        }
    }

    private void UpdateColorLegend()
    {
        ColorLegend.Clear();

        if (string.IsNullOrEmpty(SelectedColorAttribute) || SelectedColorAttribute == "[None]")
        {
            return;
        }

        // Get all distinct values for the selected attribute
        var distinctValues = ClassAssessment.Assessments
            .SelectMany(a => a.Attributes)
            .Where(attr => attr.Name == SelectedColorAttribute)
            .Select(attr => attr.Value)
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        foreach (var value in distinctValues)
        {
            var color = GetColorForValue(value);
            ColorLegend.Add(new ColorLegendItem(value, color));
        }
    }

    private string GetColorForValue(string value)
    {
        return value switch
        {
            "Yes" => "#00FF00",      // Green
            "No" => "#FF0000",       // Red
            "✓✓+" => "#BB66FF",      // Bright Purple (was too dark)
            "✓+" => "#00FF00",       // Green
            "✓" => "#FFFF00",        // Yellow
            "✓-" => "#FF0000",       // Red
            _ => "#FFFFFF"           // White (default)
        };
    }

    public OxyColor GetOxyColorForValue(string value)
    {
        var hexColor = GetColorForValue(value);
        // Remove # and parse
        var hex = hexColor.TrimStart('#');
        var r = Convert.ToByte(hex.Substring(0, 2), 16);
        var g = Convert.ToByte(hex.Substring(2, 2), 16);
        var b = Convert.ToByte(hex.Substring(4, 2), 16);
        return OxyColor.FromRgb(r, g, b);
    }

}
