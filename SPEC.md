# Dotsesses Specification

## Overview

Dotsesses is an Avalonia UI desktop application for visualization of
aggregate student grades as dotplot histograms with drill-down capability
and interactive cursors to show breaks in the grade curve. The goal is to
make it easy to see the distribution of aggregate student scores, their individual components, and assign
fair letter grades that reasonable resemble to schools curve policy.

## Data Model

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

- LetterGrade - A, A-, B+, B, B-, C+, D, D-,F
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
on the far right with padding.

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

### Cursors

#### Appearance
- Dashed vertical cursors will show the selected grade cutoffs.
- GradeLetr
- The user will be able to slide them
left and right - but they can never overlap. For example, cursor A can never 

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
