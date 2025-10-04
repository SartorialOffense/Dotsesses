# Dotsesses Specification

## Overview

Dotsesses is fun user interface for the visualization of
aggregate student grades as dotplot histograms with drill-down capability
and interactive cursors to show breaks in the grade curve. The goal is to
make it easy to see the distribution of aggregate student scores, their individual components, and assign
fair letter grades that reasonable resemble to schools curve policy.

The name is a play on incorrect pluralization of "dot". The general theme is playful and
reminiscent of early grade school. Crayons, finger-paint, and recess - we are here to have fun!!!

## Data Model

### Conventions

- All []'s should be exposed as IReadOnlyCollection's unless otherwise stated. 
- immutable means use a record class or whatever is the hot new immutable way to do it

### StudentAssessment

- int Id
- int AggregateGrade - sum of Scores (converted to int)
- Score[] Scores - individual numeric scores (Quiz)
- StudentAttribute[] Attributes - like 'Accommodation'

### immutable Score

- string Name
- int? Index - for Quiz 1, Quiz 2, etc
- double Value

### immutable StudentAttribute

- string Name
- int? Index - for Attended Study Session 1, Attended Study Session 2, etc 
- string Value - Yes, No, Maybe, etc

### immutable Grade

- LetterGrade - A, A-, B+, B, B-, C+, C, D, D-,F
- Order - A=0, A-=1, etc

### immutable GradeCutoff

- Grade Grade
- int Score

### immutable CutoffCount
 
- Grade
- Count

### ClassAssessment

- StudentAssessment[] Assessments
- GradeCutoff[] CurrentCutoffs
- CutoffCount[] DefaultCurve
- CutoffCount[] Current
- Dictionary<string, GradeCutoff> SavedCutoffs

## Presentation of Data

The scores will be distributed horizontally on a
dotplot histogram (from now on referred to as the "dotplot") that stretches across the top
of the main window.

### X-Axis Positioning

The aggregate score will be used to assign a discrete x-axis position. The lowest
score will be on the far left side (with some padding). The highest score will be
on the far right with padding. There should always be numerical space
to the left of the lowest aggregate score. This is so that additional cursors for lower grades
can always be added to the left.

### Y-Axis positioning

The Y-axis position will be determined by the number of students in the x-position. 
Essentially binning by the exact score and ordering by the student id for ordinality. The spacing of the dots
should be double the marker size.

### Scaling

The dotplot should be stretched horizontally and autoscale vertically to fit the
maximum number of students in a bin. 

### Hover

Hovering the mouse over a point will display a formatted summary table of the student's
scores and attributes. These should be organized by the name and index (if present).

### Selection

Students may be toggled into selected/deselected states by clicking.

### Drill Down

The area below the dotplot will be used for drill-down and comparison of students that are selected.
The individual students will be displayed as cards that are automatically laid out into a container.
The container will fill from left to right and then down. There will be a vertical scrollbar displayed
when there are additional rows of cards that can not be fit. The drill-down container should be
stretched horizontally.

### Curve Compliance

A grid in the far right bottom corner will show the LetterGrades, the counts from the School Curve Policy,
the current CutoffCount, and the absolute deviation if > 0. 
This table should be stored in the main windows data model.

### Cursors
- Cursors are shown when LetterGrades are Enabled in the UI. IOW, if I uncheck D-, that cursor goes away.
- Dashed vertical cursors will show the selected grade cutoffs.
- LetterGrade for the cursor will be displayed as a text annotation between the cursor
and its right neighbor centered vertically and horizontally. For the highest grades (lowest index), the 
horizontal centering is between the cursor and right boundary.
- The lowest grade is special. The cursor is not displayed and its annotation is displayed horizontally 
centered between the plots lower left boundary and the second lowest Grade.
It is still vertically centered.
- The LetterGrade annotations will be semi-transparent and have a large font 
compared with other text.
- The user will be able to slide them left and right - but they can never overlap.
  - For example, cursor A can never be moved left of or on top of A-. 
  - They should always be at least 1 apart. 
- Sorting initial placement is based on the DefaultCurve

### Initial placement of cursors

This should be done such that the counts are close to the school defaults. Cursors not
in the school defaults should not be enabled by default. When they are enabled, they should always be placed
left of the letter grades higher than themselves. Perhaps at 1 point less. If cursors are enabled that are
not on the far left, all the enabled cursors should be reset to even spacing across the dataset.

## Update Behavior

When updates are made to cursors, the ClassAssessment.GradeCutoff will be built from the
current cursor selections, a background calculator will be given the StudentAssessments
and the GradeCutoff's. It will return a IReadOnlyCollection<CutoffCount>. This
will be assigned to the ClassAssessment.CutoffCount. The main windows view model will then update
the curve compliance table.

## Loading Of Data

### Grades
Eventually, we will load student, grade, and curve data from Excel files. For now,
lets just create random data with some patterning. Assume 100 students. Their grades should break down into:

- Quiz Total (20 pts)
- Participation Total (20 pts)
- Final (300 pt)

I want to have a tri-modal distribution: 5% Super-stars that score >250 in aggregate,
75% broad middle of the roaders with scores between 150-225, and 20% losers that score 50-125.

There should also be some attributes that breakdown like this by group:

- Super-Stars
  - "Submitted Outline" : "Yes"
  - "Mid-Term": "✔✔+"

- Roaders
    - "Submitted Outline" : 70% "Yes" 30% "No"
    - "Mid-Term": "✔✔+" : 70% "✔+" 20% "✔" 10% "✔-"

- Losers
    - "Submitted Outline" : 10% "Yes" 90% "No"
    - "Mid-Term": "✔✔+" : 20% "✔" 80% "✔-"

### Default School Curve

Just give me a standard curve of A, A-, B+, B, B-, C+, and C. No grades below this are mandatory. 

## Technical Stack

- **Framework**: .NET 9.0
- **UI Framework**: Avalonia 11.3.6
- **MVVM Toolkit**: CommunityToolkit.Mvvm 8.2.1
- **Plotting Library**: OxyPlot.Avalonia 2.1.0-Avalonia11
- **Theme**: Dark theme variant

## Architecture

### MVVM Pattern
- ViewModels inherit from `ViewModelBase` (extends `ObservableObject`)
- Convention-based View resolution via `ViewLocator`
- Compiled bindings enabled by default

### Current Features

#### Scatter Plot Visualization
- PlotModel with configurable data series
- Dark theme with black background and white text
- Linear axes with customizable titles and colors
- Scatter series with cyan markers for visibility
- Sample data: 8 data points demonstrating basic plotting

## Project Structure

```
Dotsesses/
├── ViewModels/       # MVVM ViewModels
├── Views/            # Avalonia UserControls and Windows
├── Models/           # Data models (currently empty)
└── Assets/           # Application resources
```

## Future Enhancements

_To be documented as features are developed_
