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
            MarkerStrokeThickness = 1
        };

        foreach (var group in scoreGroups)
        {
            var studentsAtScore = group.OrderBy(s => s.Id).ToList();
            for (int i = 0; i < studentsAtScore.Count; i++)
            {
                // Y position: stack vertically with spacing (double the marker size)
                double yPos = i * 2;
                scatterSeries.Points.Add(new ScatterPoint(group.Key, yPos));
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
                Color = OxyColors.White,
                LineStyle = LineStyle.Dash,
                StrokeThickness = 2,
                Text = cursor.Grade.LetterGrade.ToString(),
                TextColor = OxyColors.White
            };
            DotplotModel.Annotations.Add(line);
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
                isEnabled
            ));
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
}
