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
- int AggregateGrade - **calculated property with caching**, sum of Scores (converted to int)
- Score[] Scores - individual numeric scores (Quiz)
- StudentAttribute[] Attributes - like 'Accommodation'
- string MuppetName - fun whimsical identifier (see MuppetName Generation below)

### immutable Score

- string Name
- int? Index - for Quiz 1, Quiz 2, etc (null for single scores like "Final")
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
- GradeCutoff[] CurrentCutoffs - actual score thresholds for each grade (e.g., "A = 285")
- CutoffCount[] DefaultCurve - school's default grade distribution
- CutoffCount[] Current - counts of students in each grade with current cutoffs
- Dictionary<string, GradeCutoff[]> SavedCutoffs - user-named saved cutoff configurations
- Dictionary<int, MuppetNameInfo> MuppetNameMap - mapping of student ID to MuppetName data

### MuppetName Generation

To make the interface more playful, each student gets a whimsical "MuppetName" instead of showing boring numeric IDs.

**Structure:** `[Size/Color/Adjective] [Character] [Emojis]`

**Examples:**
- "Large Purple Kermit 🐸🎭🎪"
- "Tiny Blue Cookie Monster 🍪🎨🎈"
- "Fuzzy Orange Elmo 🔴🎪🎨"

**Generation Algorithm:**
1. Order students by ID
2. Use constant seed for random generator
3. For each student, randomly select unique combination:
   - Descriptor (size/color/adjective)
   - Character (candy, cute animal, muppet, cartoon character)
   - 1-3 emojis
4. Ensure uniqueness within the class (if duplicate, reroll)
5. Store mapping in ClassAssessment

**Note:** Student IDs don't need consistent MuppetNames across different ClassAssessments.

## Presentation of Data

The scores will be distributed horizontally on a
dotplot histogram (from now on referred to as the "dotplot") that stretches across the top
of the main window.

### X-Axis Positioning

The aggregate score will be used to assign a discrete x-axis position. The lowest
score will be on the far left side (with some padding). The highest score will be
on the far right with padding.

**Padding:** Use the total number of letter grades (10) as the left padding amount to ensure space for all possible grade cursors.

### Y-Axis positioning

The Y-axis position will be determined by the number of students in the x-position.
Students with identical aggregate scores stack vertically at that x-position, ordered by student ID for stable positioning across redraws. The spacing of the dots should be double the marker size.

### Scaling

The dotplot should be stretched horizontally and autoscale vertically to fit the
maximum number of students in a bin.

### Hover

Hovering the mouse over a point will display a formatted summary table of the student's
scores and attributes. These should be organized by the name and index (if present).

### Selection

Students may be toggled into selected/deselected states by clicking. **Selection persists when cursors are moved.**

### Drill Down

The area below the dotplot will be used for drill-down and comparison of students that are selected.
The individual students will be displayed as cards that are automatically laid out into a container.
The container will fill from left to right and then down. There will be a vertical scrollbar displayed
when there are additional rows of cards that can not be fit. The drill-down container should be
stretched horizontally.

**Card Content (top to bottom):**
1. MuppetName and assigned grade (header)
2. Scores table (consistently sorted)
3. Attributes table (consistently sorted)

### Curve Compliance

A grid in the far right bottom corner will show:
- LetterGrades
- Counts from the School Curve Policy (default)
- Current CutoffCount
- Absolute deviation (only if > 0)

**Grade Checkboxes:** To the left of the compliance table, display checkboxes for each letter grade. Unchecking a grade hides its cursor and recalculates binning.

### Cursors

- Cursors are shown when LetterGrades are enabled via checkboxes. Unchecking a grade removes its cursor.
- Dashed vertical cursors will show the selected grade cutoffs.
- LetterGrade for the cursor will be displayed as a text annotation between the cursor
and its right neighbor centered vertically and horizontally. For the highest grades (lowest index), the
horizontal centering is between the cursor and right boundary.
- The lowest grade is special. The cursor is not displayed and its annotation is displayed horizontally
centered between the plots lower left boundary and the second lowest Grade.
It is still vertically centered.
- The LetterGrade annotations will be **semi-transparent with fixed transparency level** and have a large font
compared with other text.
- The user will be able to slide them left and right - but they can never overlap.
  - For example, cursor A can never be moved left of or on top of A-.
  - **Cursors must be at least 1 point apart** on the score scale.

### Initial Placement of Cursors

Algorithm:
1. Start with grades in the DefaultCurve
2. For each grade (starting from highest), assign cutoff to match target count
3. Allow overflow if multiple students are tied at boundary (fairness - don't split tied students)
4. Grades not in DefaultCurve start disabled

### Enabling Additional Cursors

When a disabled cursor is enabled:

1. **If between existing cursors:** Place at middle of score range between neighbors
   - Example: A- at 280, B+ at 260 → place B at 270
2. **If at edge:** Place to the left/right of existing cursors
3. **If placement causes overlap:** Reset ALL enabled cursors to even spacing across score range (min to max)

## Update Behavior

### Cursor Movement Calculation

**Smart async updates with 25ms delay and cancellation:**

1. Trigger calculation on cursor move
2. Wait 25ms (async)
3. Check cancellation token on UI context (cursor moved again?)
4. If cancelled, abort
5. If not cancelled, perform recalculation on background task
6. Check cancellation token again before UI update
7. Update UI only if not cancelled

**Result:** Smooth, responsive updates without unnecessary calculations during rapid cursor movement.

### Update Flow

When updates are made to cursors:
1. Build GradeCutoff[] from current cursor positions
2. Background calculator receives StudentAssessments and GradeCutoffs
3. Returns IReadOnlyCollection<CutoffCount>
4. Assign to ClassAssessment.Current
5. Update curve compliance table in UI

## Loading Of Data

### Grades

Eventually, we will load student, grade, and curve data from Excel files. For now,
lets just create random data with some patterning. Assume 100 students. Their grades should break down into:

- Quiz Total (20 pts)
- Participation Total (20 pts)
- Final (300 pt)

I want to have a tri-modal distribution: 5% Super-stars that score >250 in aggregate,
75% broad middle of the roaders with scores between 150-225, and 20% losers that score 50-125.

### Attributes (Synthetic Test Data)

Attributes should be generated with **60% correlation, 40% independent** to add realistic variation:

- **Super-Stars (5%)**
  - "Submitted Outline" : "Yes"
  - "Mid-Term": "✔✔+"

- **Roaders (75%)**
  - "Submitted Outline" : 70% "Yes" 30% "No"
  - "Mid-Term": 70% "✔✔+", 20% "✔+", 10% "✔"

- **Losers (20%)**
  - "Submitted Outline" : 10% "Yes" 90% "No"
  - "Mid-Term": 20% "✔", 80% "✔-"

**Implementation:** For 60% of students, make attributes correlate with performance group. For 40%, roll independently.

### Default School Curve

Just give me a standard curve of A, A-, B+, B, B-, C+, and C. No grades below this are mandatory.

## Technical Stack

- **Framework**: .NET 9.0
- **UI Framework**: Avalonia 11.3.6
- **MVVM Toolkit**: CommunityToolkit.Mvvm 8.2.1
- **Plotting Library**: OxyPlot.Avalonia 2.1.0-Avalonia11
- **Theme**: Dark theme variant
- **Testing**: Unit tests required for calculation classes and ViewModels

## Architecture

### MVVM Pattern
- ViewModels inherit from `ViewModelBase` (extends `ObservableObject`)
- Convention-based View resolution via `ViewLocator`
- Compiled bindings enabled by default
- **Unit testing required** for ViewModels and calculation classes

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
├── Models/           # Data models
├── Calculators/      # Grade calculation logic
├── Services/         # Data generation, MuppetName generation
└── Assets/           # Application resources
```

## Design History

See `design_history/` folder for detailed design decisions and clarifications.
