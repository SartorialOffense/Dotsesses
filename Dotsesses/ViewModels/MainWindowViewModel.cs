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
        InitializeDotplot();
        InitializeCursors();
        InitializeComplianceGrid();
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
        DotplotModel = new PlotModel
        {
            Title = "Student Grade Distribution",
            Background = OxyColors.Black,
            TextColor = OxyColors.White,
            PlotAreaBorderColor = OxyColors.White
        };

        // Enable mouse events for point selection
        DotplotModel.MouseDown += OnDotplotMouseDown;

        // X-axis: Score range with padding
        var minScore = ClassAssessment.Assessments.Min(a => a.AggregateGrade);
        var maxScore = ClassAssessment.Assessments.Max(a => a.AggregateGrade);
        var padding = 10; // Left padding for cursors

        DotplotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Aggregate Score",
            Minimum = minScore - padding,
            Maximum = maxScore + padding,
            AxislineColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            TextColor = OxyColors.White
        });

        // Y-axis: Will autoscale based on maximum stack height
        DotplotModel.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Student Count",
            AxislineColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            TextColor = OxyColors.White
        });

        UpdateDotplotPoints();
        UpdateCursors();
    }

    private void UpdateDotplotPoints()
    {
        // Clear existing series
        DotplotModel.Series.Clear();

        // Group students by aggregate score and stack vertically
        var scoreGroups = ClassAssessment.Assessments
            .GroupBy(a => a.AggregateGrade)
            .OrderBy(g => g.Key);

        var scatterSeries = new ScatterSeries
        {
            MarkerType = MarkerType.Circle,
            MarkerSize = 8,
            MarkerFill = OxyColors.Cyan,
            MarkerStroke = OxyColors.White,
            MarkerStrokeThickness = 1,
            TrackerFormatString = "{Tag}\nScore: {2:0}"
        };

        foreach (var group in scoreGroups)
        {
            var studentsAtScore = group.OrderBy(s => s.Id).ToList();
            for (int i = 0; i < studentsAtScore.Count; i++)
            {
                // Y position: stack vertically with spacing (double the marker size)
                double yPos = i * 2;
                var student = studentsAtScore[i];
                var muppetName = ClassAssessment.MuppetNameMap.TryGetValue(student.Id, out var info) ? info.Name : "Unknown";
                
                scatterSeries.Points.Add(new ScatterPoint(group.Key, yPos, tag: $"{muppetName}\nScore: {student.AggregateGrade}"));
            }
        }

        DotplotModel.Series.Add(scatterSeries);
        DotplotModel.InvalidatePlot(true);
    }

    private void UpdateCursors()
    {
        // Clear existing annotations
        DotplotModel.Annotations.Clear();

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

            // Add semi-transparent text annotation for grade label
            var label = new TextAnnotation
            {
                Text = cursor.Grade.LetterGrade.ToString(),
                TextPosition = new DataPoint(cursor.Score, -1),
                TextColor = OxyColor.FromArgb(180, 255, 255, 255), // Semi-transparent white
                FontSize = 18,
                FontWeight = OxyPlot.FontWeights.Bold,
                Background = OxyColor.FromArgb(100, 0, 0, 0), // Semi-transparent black background
                Padding = new OxyThickness(4),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center
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
    }

    [RelayCommand]
    private void ToggleCompliancePane()
    {
        IsCompliancePaneOpen = !IsCompliancePaneOpen;
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

        // Find the nearest point to the click
        var point = series.InverseTransform(e.Position);
        var nearestPoint = FindNearestStudent(point.X, point.Y);
        
        if (nearestPoint != null)
        {
            ToggleStudent(nearestPoint);
            e.Handled = true;
        }
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
            for (int i = 0; i < studentsAtScore.Count; i++)
            {
                double yPos = i * 2;
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
