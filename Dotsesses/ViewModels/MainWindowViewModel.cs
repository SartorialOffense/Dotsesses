namespace Dotsesses.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dotsesses.Calculators;
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
    private readonly CutoffCountCalculator _cutoffCountCalculator;
    private readonly InitialCutoffCalculator _initialCutoffCalculator;
    private readonly CursorValidation _cursorValidation;
    private readonly CursorPlacementCalculator _cursorPlacementCalculator;
    private CursorViewModel? _draggingCursor;
    private bool _isDraggingCursor;

    [ObservableProperty]
    private ClassAssessment _classAssessment = null!;

    [ObservableProperty]
    private PlotModel _dotplotModel = null!;

    [ObservableProperty]
    private ObservableCollection<CursorViewModel> _cursors;

    [ObservableProperty]
    private ObservableCollection<StudentCardViewModel> _selectedStudents;

    [ObservableProperty]
    private ObservableCollection<ComplianceRowViewModel> _complianceRows;

    [ObservableProperty]
    private bool _isCompliancePaneOpen = true;


    public bool CanClearSelections => SelectedStudents.Any();

    public MainWindowViewModel()
    {
        _cutoffCountCalculator = new CutoffCountCalculator();
        _initialCutoffCalculator = new InitialCutoffCalculator();
        _cursorValidation = new CursorValidation();
        _cursorPlacementCalculator = new CursorPlacementCalculator();

        _cursors = new ObservableCollection<CursorViewModel>();
        _selectedStudents = new ObservableCollection<StudentCardViewModel>();
        _complianceRows = new ObservableCollection<ComplianceRowViewModel>();

        // Hook up collection changed to update command state
        _selectedStudents.CollectionChanged += (s, e) => ClearSelectionsCommand.NotifyCanExecuteChanged();

        InitializeWithSyntheticData();
        InitializeCursors();
        InitializeComplianceGrid();
        InitializeDotplot();
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
    }

    private void InitializeDotplot()
    {
        var backgroundColor = OxyColor.FromRgb(24, 24, 24);

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

    private void UpdateDotplotPoints()
    {
        // Clear existing series (keep axes)
        DotplotModel.Series.Clear();

        // Group students by aggregate score and stack vertically
        var scoreGroups = ClassAssessment.Assessments
            .GroupBy(a => a.AggregateGrade)
            .OrderBy(g => g.Key);

        // Marker size per spec: radius = 4
        var markerSize = 4.0;
        var crosshairSize = markerSize * 2;

        // Create series for selected students (crosshairs behind)
        var selectedSeries = new ScatterSeries
        {
            MarkerType = MarkerType.Cross,
            MarkerSize = crosshairSize,
            MarkerFill = OxyColor.FromRgb(70, 130, 180), // Medium blue
            MarkerStroke = OxyColor.FromRgb(70, 130, 180),
            MarkerStrokeThickness = 2,
            XAxisKey = "SharedX",
            YAxisKey = "DotY"
        };

        // Create series for all dots (white fill, on top)
        var dotSeries = new ScatterSeries
        {
            MarkerType = MarkerType.Circle,
            MarkerSize = markerSize,
            MarkerFill = OxyColors.White,
            MarkerStroke = OxyColors.White,
            MarkerStrokeThickness = 0.5,
            XAxisKey = "SharedX",
            YAxisKey = "DotY"
        };

        foreach (var group in scoreGroups)
        {
            var studentsAtScore = group.OrderBy(s => s.Id).ToList();

            // Apply bin offset for odd aggregate scores
            var binOffset = group.Key % 2 == 1 ? 0.1 : 0.0;

            for (int i = 0; i < studentsAtScore.Count; i++)
            {
                // Y position: stack vertically with spacing (double the marker size)
                double yPos = i * 2 + binOffset;
                var student = studentsAtScore[i];
                var muppetName = ClassAssessment.MuppetNameMap.TryGetValue(student.Id, out var info) ? info.Name : "Unknown";

                var point = new ScatterPoint(group.Key, yPos, tag: $"{muppetName}\nScore: {student.AggregateGrade}");

                // Add to both series if selected, otherwise just main series
                var isSelected = SelectedStudents.Any(s => s.Assessment.Id == student.Id);
                if (isSelected)
                {
                    selectedSeries.Points.Add(point);
                }

                dotSeries.Points.Add(point);
            }
        }

        // Add selected series first (behind), then main dots
        DotplotModel.Series.Add(selectedSeries);
        DotplotModel.Series.Add(dotSeries);
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
        // Alternating pattern: transparent and light gray RGB(36, 36, 36)
        if (enabledCursors.Any())
        {
            var grayColor = OxyColor.FromArgb(0x20, 255, 255, 255); // White with alpha 0x20
            var clearColor = OxyColors.Transparent;

            // Create regions from left boundary to first cursor, between cursors, and last cursor to right
            var regions = new List<(double left, double right, bool isGray)>();

            // First region: left boundary to first cursor
            regions.Add((minScore, enabledCursors[0].Score, false)); // Start with clear

            // Between cursors
            for (int i = 0; i < enabledCursors.Count - 1; i++)
            {
                bool isGray = (i + 1) % 2 == 1; // Alternate starting with gray for second region
                regions.Add((enabledCursors[i].Score, enabledCursors[i + 1].Score, isGray));
            }

            // Last region: last cursor to right boundary
            bool lastIsGray = enabledCursors.Count % 2 == 1;
            regions.Add((enabledCursors.Last().Score, maxScore, lastIsGray));

            // Draw region bands
            foreach (var (left, right, isGray) in regions)
            {
                var rect = new RectangleAnnotation
                {
                    MinimumX = left,
                    MaximumX = right,
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
        foreach (var cursor in enabledCursors)
        {
            var line = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = cursor.Score,
                Color = OxyColor.FromRgb(255, 215, 0), // Gold
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
        // Track newly enabled grades that need cursor placement
        var newlyEnabled = new List<Grade>();

        foreach (var row in ComplianceRows)
        {
            var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(row.Grade));
            if (cursor != null)
            {
                bool wasEnabled = cursor.IsEnabled;
                cursor.IsEnabled = row.IsEnabled;

                // If newly enabled and has no valid score, track it
                if (!wasEnabled && row.IsEnabled && cursor.Score == 0)
                {
                    newlyEnabled.Add(cursor.Grade);
                }
            }
        }

        // Calculate positions for newly enabled cursors
        if (newlyEnabled.Any())
        {
            var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
            var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);

            foreach (var grade in newlyEnabled)
            {
                var existingCutoffs = Cursors
                    .Where(c => c.IsEnabled && !newlyEnabled.Contains(c.Grade))
                    .Select(c => new GradeCutoff(c.Grade, c.Score))
                    .ToList();

                var newCutoffs = _cursorPlacementCalculator.PlaceNewCursor(
                    grade,
                    existingCutoffs,
                    minScore,
                    maxScore);

                // Update cursor with new score
                var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(grade));
                var newCutoff = newCutoffs.FirstOrDefault(c => c.Grade.Equals(grade));
                if (cursor != null && newCutoff != null)
                {
                    cursor.Score = newCutoff.Score;
                }

                // If placement reset all cursors, update them too
                foreach (var cutoff in newCutoffs)
                {
                    var c = Cursors.FirstOrDefault(cur => cur.Grade.Equals(cutoff.Grade));
                    if (c != null)
                    {
                        c.Score = cutoff.Score;
                    }
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
    private void ToggleStudent(StudentAssessment student)
    {
        var existing = SelectedStudents.FirstOrDefault(s => s.Assessment.Id == student.Id);
        if (existing != null)
        {
            SelectedStudents.Remove(existing);
        }
        else
        {
            // Determine assigned grade based on current cutoffs
            var grade = GetGradeForStudent(student);
            SelectedStudents.Add(new StudentCardViewModel(student, grade));
        }

        // Update dotplot to reflect selection color changes
        UpdateDotplotPoints();
    }

    [RelayCommand]
    private void ToggleCompliancePane()
    {
        IsCompliancePaneOpen = !IsCompliancePaneOpen;
    }

    [RelayCommand(CanExecute = nameof(CanClearSelections))]
    private void ClearSelections()
    {
        SelectedStudents.Clear();
        UpdateDotplotPoints();
    }

    private string GetGradeForStudent(StudentAssessment student)
    {
        var sortedCutoffs = ClassAssessment.CurrentCutoffs
            .OrderByDescending(c => c.Score)
            .ToList();

        foreach (var cutoff in sortedCutoffs)
        {
            if (student.AggregateGrade >= cutoff.Score)
            {
                return cutoff.Grade.LetterGrade.ToString();
            }
        }

        return "F";
    }

    private void OnDotplotMouseDown(object? sender, OxyMouseDownEventArgs e)
    {
        if (e.ChangedButton != OxyMouseButton.Left)
            return;

        var series = DotplotModel.Series.FirstOrDefault() as ScatterSeries;
        if (series == null)
            return;

        // Transform click position to data coordinates
        // This uses the series' axes (SharedX, DotY), so clickPos.Y is already in DotY space
        var clickPos = series.InverseTransform(e.Position);
        
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

        // Check if we're in the Dot Display region and find nearest student
        var dotYAxis = DotplotModel.Axes.FirstOrDefault(a => a.Key == "DotY");
        if (dotYAxis != null && clickPos.Y >= dotYAxis.Minimum && clickPos.Y <= dotYAxis.Maximum)
        {
            // Find nearest student using screen space distance
            var nearestPoint = FindNearestStudent(e.Position);
            
            if (nearestPoint != null)
            {
                ToggleStudent(nearestPoint);
                e.Handled = true;
            }
        }
    }

    private void OnDotplotMouseMove(object? sender, OxyMouseEventArgs e)
    {
        if (!_isDraggingCursor || _draggingCursor == null)
            return;

        var series = DotplotModel.Series.FirstOrDefault() as ScatterSeries;
        if (series == null)
            return;

        var pos = series.InverseTransform(e.Position);
        var newScore = (int)Math.Round(pos.X);

        // Validate cursor movement (only include enabled cursors)
        var allCutoffs = Cursors
            .Where(c => c.IsEnabled)
            .Select(c => new GradeCutoff(c.Grade, c == _draggingCursor ? newScore : c.Score))
            .ToList();

        var validatedScore = _cursorValidation.ValidateMovement(_draggingCursor.Grade, newScore, allCutoffs);

        _draggingCursor.Score = validatedScore;
        UpdateCursors();
        e.Handled = true;
    }

    private void OnDotplotMouseUp(object? sender, OxyMouseEventArgs e)
    {
        if (_isDraggingCursor && _draggingCursor != null)
        {
            // Finalize cursor drag - update cutoffs and recalculate compliance (only enabled cursors)
            var updatedCutoffs = Cursors
                .Where(c => c.IsEnabled)
                .Select(c => new GradeCutoff(c.Grade, c.Score))
                .ToList();

            ClassAssessment.CurrentCutoffs = updatedCutoffs;
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

        foreach (var cursor in Cursors.Where(c => c.IsEnabled))
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
