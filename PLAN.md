# Dotsesses Implementation Plan

## Phase 1: Foundation - Model Classes

Implement immutable data model classes from SPEC.md.

### 1.1 Core Records
- `Grade` - Letter grade enumeration and ordering
- `Score` - Individual score with name, optional index, and value
- `StudentAttribute` - Non-numeric student data with name, optional index, and value
- `GradeCutoff` - Grade threshold pairing
- `CutoffCount` - Grade count pairing

### 1.2 Mutable Classes
- `StudentAssessment` - Student data with caching for aggregate grade
- `ClassAssessment` - Root class containing all student data, cutoffs, and curves
- `MuppetNameInfo` - Data for MuppetName generation (structure TBD)

**Deliverable**: All model classes in `Models/` directory

## Phase 2: Business Logic - Calculators

Implement grade calculation and cursor placement logic.

### 2.1 Grade Calculators
- `AggregateGradeCalculator` - Sum scores to compute aggregate
- `CutoffCountCalculator` - Given cutoffs, calculate student counts per grade
- `InitialCutoffCalculator` - Place cursors to match DefaultCurve (with tie handling)

### 2.2 Cursor Logic
- `CursorPlacementCalculator` - Handle enabling new cursors (midpoint or reset logic)
- `CursorValidation` - Ensure cursors are at least 1 point apart, no overlaps

**Deliverable**: All calculator classes in `Calculators/` directory

## Phase 3: Test Data - Synthetic Data Generators

Generate realistic test data per SPEC.md requirements.

### 3.1 Data Generation
- `SyntheticStudentGenerator` - Generate 100 students with tri-modal distribution
  - 5% high performers (>250)
  - 75% middle (150-225)
  - 20% low performers (50-125)
- `SyntheticAttributeGenerator` - Generate correlated attributes (60% correlation, 40% independent)
- `DefaultCurveGenerator` - Generate standard school curve (A, A-, B+, B, B-, C+, C)

### 3.2 MuppetName Generation
- `MuppetNameGenerator` - Generate unique whimsical names with emojis
  - Seeded random for consistency
  - Ensure uniqueness within class

**Deliverable**: All generator classes in `Services/` directory

## Phase 4: Verification - Calculator Unit Tests

Comprehensive testing of all calculator logic.

### 4.1 Calculator Tests
- Test aggregate grade calculation
- Test cutoff count calculation with various distributions
- Test initial cursor placement algorithm (including tie handling)
- Test cursor enabling logic (midpoint placement, reset on overlap)
- Test cursor validation (spacing, overlap prevention)

### 4.2 Edge Cases
- Single student
- All students tied
- Empty grade bins
- Maximum spread (min score = 0, max score = 340)

**Deliverable**: Test project with all calculator tests passing

## Phase 5: MVVM Layer - ViewModels

Implement ViewModels with CommunityToolkit.Mvvm.

### 5.1 Main ViewModel
- `MainWindowViewModel` - Root ViewModel, coordinates all child ViewModels

### 5.2 Feature ViewModels
- `DotplotViewModel` - Dotplot visualization state (points, scaling, hover, selection)
- `CursorViewModel` - Individual cursor state (position, grade, enabled, dragging)
- `CursorCollectionViewModel` - Manages all cursors with async update logic (25ms debounce)
- `StudentCardViewModel` - Individual student card in drill-down
- `ComplianceGridViewModel` - Grade compliance table with checkboxes

### 5.3 Commands
- Selection toggle command
- Cursor drag commands
- Grade enable/disable commands
- Export command

**Deliverable**: All ViewModels in `ViewModels/` directory, inheriting from `ViewModelBase`

## Phase 6: Verification - ViewModel Tests

Test ViewModel logic and async behavior.

### 6.1 ViewModel Tests
- Test cursor movement updates compliance grid
- Test async update with cancellation (rapid cursor movement)
- Test grade enable/disable updates cursor visibility
- Test selection persistence across cursor changes
- Test export data generation

### 6.2 Performance Tests
- Test rapid cursor movement (verify cancellation works)
- Test various speeds of cursor changes

**Deliverable**: ViewModel tests in test project, all passing

## Phase 7: Presentation - UI Views

Implement Avalonia views with OxyPlot integration.

### 7.1 Main Window
- `MainWindow` - Root window with layout (dotplot, drill-down, compliance grid)

### 7.2 Controls
- `DotplotView` - OxyPlot scatter plot with custom rendering
  - Horizontal score axis with padding
  - Vertical stacking for ties
  - Hover tooltips
  - Selection highlighting
- `CursorView` - Draggable vertical cursors with labels
- `StudentCardView` - Card layout for selected students
- `ComplianceGridView` - Table with checkboxes and deviation highlighting

### 7.3 Styling
- Apply dark theme variant
- Configure OxyPlot theme
- Style cursors (dashed lines, semi-transparent labels)

**Deliverable**: All views in `Views/` directory with `x:DataType` for compiled bindings

## Phase 8: Visual Verification - UI Testing Harness

Create executable for visual testing and snapshot generation.

### 8.1 Test Harness
- Command-line executable accepting arguments for initial state
- Generates PNG snapshots to temp folder
- Returns file path for programmatic verification

### 8.2 Test Scenarios
- Default state (100 students, initial cursors)
- All cursors enabled
- Various selection states
- Edge cases (single student, all tied)

**Deliverable**: Test harness executable with snapshot generation

## Additional Infrastructure

These should be implemented as needed throughout phases:

- **Dependency Injection** - Configure services in single DI configuration class
- **Logging** - ILog interface with rolling file appenders (debug/info, 100K/30 days)
- **Exception Handling** - Global handler with clipboard copy functionality
- **Export** - Excel export with all student data and final grades

## Notes

- Each phase builds on the previous
- Run tests after each phase before proceeding
- Commit after each completed phase
- ViewModels should not be started until calculators are fully tested
- UI work should not begin until ViewModels are tested
