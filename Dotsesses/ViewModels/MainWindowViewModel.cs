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

    public MainWindowViewModel()
    {
        _cutoffCountCalculator = new CutoffCountCalculator();
        _initialCutoffCalculator = new InitialCutoffCalculator();
        _cursorValidation = new CursorValidation();

        _cursors = new ObservableCollection<CursorViewModel>();
        _selectedStudents = new ObservableCollection<StudentCardViewModel>();
        _complianceRows = new ObservableCollection<ComplianceRowViewModel>();

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
            PlotAreaBorderThickness = new OxyThickness(0, 0, 0, 1), // Bottom border only
            PlotAreaBorderColor = OxyColors.White
        };

        // Enable mouse events for point selection and cursor dragging
        DotplotModel.MouseDown += OnDotplotMouseDown;
        DotplotModel.MouseMove += OnDotplotMouseMove;
        DotplotModel.MouseUp += OnDotplotMouseUp;

        // Calculate Y-axis padding based on max students in a bin
        var scoreGroups = ClassAssessment.Assessments.GroupBy(a => a.AggregateGrade);
        var maxStudentsInBin = scoreGroups.Max(g => g.Count());
        var yPadding = maxStudentsInBin * 0.1;

        // X-axis: Score range with padding
        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var xPadding = 10; // Left padding for cursors

        DotplotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = minScore - xPadding,
            Maximum = maxScore + xPadding,
            AxislineColor = OxyColors.White,
            AxislineStyle = LineStyle.Solid,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = OxyColors.Transparent // Hide labels
        });

        // Y-axis: Hidden completely
        DotplotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = -yPadding,
            Maximum = (maxStudentsInBin - 1) * 2 + yPadding, // Account for spacing and top padding
            AxislineStyle = LineStyle.None,
            TickStyle = TickStyle.None,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = OxyColors.Transparent
        });

        UpdateDotplotPoints();
        UpdateCursors();
    }

    private void UpdateDotplotPoints()
    {
        // Clear existing series and annotations
        DotplotModel.Series.Clear();

        // Keep cursor annotations, but remove selection bars
        var cursorAnnotations = DotplotModel.Annotations
            .Where(a => a is LineAnnotation || a is TextAnnotation)
            .ToList();
        DotplotModel.Annotations.Clear();
        foreach (var annotation in cursorAnnotations)
        {
            DotplotModel.Annotations.Add(annotation);
        }

        // Group students by aggregate score and stack vertically
        var scoreGroups = ClassAssessment.Assessments
            .GroupBy(a => a.AggregateGrade)
            .OrderBy(g => g.Key);

        // Marker size: 3x smaller than original (8) means ~2.67
        var markerSize = 8.0 / 3.0;

        // Create series for unselected dots (white fill)
        var dotSeries = new ScatterSeries
        {
            MarkerType = MarkerType.Circle,
            MarkerSize = markerSize,
            MarkerFill = OxyColors.White,
            MarkerStroke = OxyColors.White,
            MarkerStrokeThickness = 0.5,
            TrackerFormatString = "{Tag}"
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
                dotSeries.Points.Add(point);

                // Add selection indicator (medium blue vertical bar behind dot)
                var isSelected = SelectedStudents.Any(s => s.Assessment.Id == student.Id);
                if (isSelected)
                {
                    var selectionBar = new RectangleAnnotation
                    {
                        MinimumX = group.Key - 0.15,
                        MaximumX = group.Key + 0.15,
                        MinimumY = yPos - markerSize / 2,
                        MaximumY = yPos + markerSize / 2 * 2, // 2x dot height
                        Fill = OxyColor.FromRgb(70, 130, 180), // Medium blue (steel blue)
                        Stroke = OxyColors.Transparent,
                        Layer = AnnotationLayer.BelowSeries
                    };
                    DotplotModel.Annotations.Add(selectionBar);
                }
            }
        }

        DotplotModel.Series.Add(dotSeries);
        DotplotModel.InvalidatePlot(true);
    }

    private void UpdateCursors()
    {
        // Clear cursor annotations only (keep selection bars)
        var selectionBars = DotplotModel.Annotations
            .Where(a => a is RectangleAnnotation)
            .ToList();

        DotplotModel.Annotations.Clear();

        foreach (var bar in selectionBars)
        {
            DotplotModel.Annotations.Add(bar);
        }

        // Add vertical line annotations for each enabled cursor
        foreach (var cursor in Cursors.Where(c => c.IsEnabled))
        {
            var line = new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = cursor.Score,
                Color = OxyColor.FromRgb(255, 215, 0), // Gold color for visibility
                LineStyle = LineStyle.Dash,
                StrokeThickness = 2
            };
            DotplotModel.Annotations.Add(line);
        }

        // Add grade labels between cursors
        var enabledCursors = Cursors.Where(c => c.IsEnabled).OrderBy(c => c.Score).ToList();
        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade) - 10;
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade) + 10;

        for (int i = 0; i < enabledCursors.Count; i++)
        {
            var cursor = enabledCursors[i];
            double labelX;

            if (i == 0)
            {
                // Lowest grade: centered between left boundary and first cursor
                labelX = (minScore + cursor.Score) / 2;
            }
            else
            {
                // Other grades: centered between this cursor and previous cursor
                labelX = (enabledCursors[i - 1].Score + cursor.Score) / 2;
            }

            var label = new TextAnnotation
            {
                Text = cursor.Grade.DisplayName,
                TextPosition = new DataPoint(labelX, 0),
                TextColor = OxyColor.FromArgb(180, 255, 255, 255), // Semi-transparent white
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                TextHorizontalAlignment = HorizontalAlignment.Center
            };
            DotplotModel.Annotations.Add(label);
        }

        // Add label for highest grade (between last cursor and right boundary)
        if (enabledCursors.Any())
        {
            var lastCursor = enabledCursors.Last();
            var labelX = (lastCursor.Score + maxScore) / 2;

            // Find the highest grade (lowest order number) to display
            var highestGrade = enabledCursors.OrderBy(c => c.Grade.Order).First().Grade;

            var label = new TextAnnotation
            {
                Text = highestGrade.DisplayName,
                TextPosition = new DataPoint(labelX, 0),
                TextColor = OxyColor.FromArgb(180, 255, 255, 255), // Semi-transparent white
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                TextHorizontalAlignment = HorizontalAlignment.Center
            };
            DotplotModel.Annotations.Add(label);
        }

        DotplotModel.InvalidatePlot(true);
    }

    private void InitializeCursors()
    {
        // Create cursors for grades in DefaultCurve (enabled by default)
        var defaultGrades = ClassAssessment.DefaultCurve.Select(cc => cc.Grade).ToHashSet();

        foreach (var cutoff in ClassAssessment.CurrentCutoffs)
        {
            bool isEnabled = defaultGrades.Contains(cutoff.Grade);
            Cursors.Add(new CursorViewModel(cutoff.Grade, cutoff.Score, isEnabled));
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
        // Sync cursor enabled state with compliance grid checkboxes
        foreach (var row in ComplianceRows)
        {
            var cursor = Cursors.FirstOrDefault(c => c.Grade.Equals(row.Grade));
            if (cursor != null)
            {
                cursor.IsEnabled = row.IsEnabled;
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

    [RelayCommand]
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

        // Check if clicking near a cursor first (within 3 units)
        var clickPos = series.InverseTransform(e.Position);
        var nearestCursor = FindNearestCursor(clickPos.X);

        if (nearestCursor.cursor != null && nearestCursor.distance < 3)
        {
            // Start dragging cursor
            _draggingCursor = nearestCursor.cursor;
            _isDraggingCursor = true;
            e.Handled = true;
            return;
        }

        // Otherwise, try to select a student
        var nearestPoint = FindNearestStudent(clickPos.X, clickPos.Y);
        
        if (nearestPoint != null)
        {
            ToggleStudent(nearestPoint);
            e.Handled = true;
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

        // Validate cursor movement
        var allCutoffs = Cursors
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
            // Finalize cursor drag - update cutoffs and recalculate compliance
            var updatedCutoffs = Cursors
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

    private StudentAssessment? FindNearestStudent(double clickX, double clickY)
    {
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
                double distance = Math.Sqrt(Math.Pow(group.Key - clickX, 2) + Math.Pow(yPos - clickY, 2));

                if (distance < minDistance && distance < 5) // Within 5 units
                {
                    minDistance = distance;
                    nearest = studentsAtScore[i];
                }
            }
        }

        return nearest;
    }
}
