# Implementation Decisions

This document tracks implementation decisions made during autonomous development.

## Phase 1: Model Classes

**Decision**: Use positional records with validation
- Records have implicit constructors from their positional parameters
- Added explicit constructors for null validation, but this creates duplicate constructors
- **Resolution**: Remove explicit constructors, use required keyword or init-only setters with validation in alternative approach

**Decision**: Use System.Collections.Generic namespace
- Need to add `using System.Collections.Generic;` to all files using IReadOnlyCollection, List, Dictionary
- Need to add `using System.Linq;` for LINQ operations

## Fixes Applied
- Removed duplicate constructors from records (Score, StudentAttribute, MuppetNameInfo)
- Added GlobalUsings.cs for System, System.Collections.Generic, System.Linq
- Fixed calculator logic bugs related to grade order vs score order
- Clarified that cutoff = minimum score to receive a grade

## Phase 4: Calculator Tests
- All 22 calculator tests passing
- Covers edge cases: ties, empty bins, single student, perfect distribution
- Validates cursor spacing and placement logic

## Phase 5: ViewModels - PARTIALLY COMPLETE
**Completed:**
- MainWindowViewModel with synthetic data loading (100 students)
- OxyPlot dotplot with stacked points (vertical by score)
- CursorViewModel, StudentCardViewModel, ComplianceRowViewModel
- ToggleStudent and ToggleCompliancePane commands
- All infrastructure for cursors, drill-down, compliance grid
- 31/31 tests passing

**Remaining UI Work:**
1. **Cursors on Dotplot**
   - Overlay vertical line annotations on OxyPlot
   - Make cursors draggable
   - Add semi-transparent grade labels between cursors
   - Wire up cursor drag to update cutoffs
   - Implement async 25ms debounce with cancellation tokens

2. **Full Layout**
   - Replace single PlotView with proper two-row Grid layout
   - Top row: SplitView with dotplot (left) and compliance grid (right)
   - Bottom row: ScrollViewer with wrapped student cards
   - Style SplitView with hamburger toggle button

3. **Compliance Grid UI**
   - DataGrid or ItemsControl with checkboxes
   - Show grade, target, current, deviation columns
   - Wire checkboxes to enable/disable cursors
   - Highlight deviations

4. **Student Cards**
   - Create card template with header + two-column table
   - Implement click-to-select on dotplot points
   - Wire ToggleStudent command to dotplot point clicks
   - Style cards with spacing and borders

5. **Hover Tooltips**
   - Add hover trackers to OxyPlot
   - Show formatted student info on hover
   - Include all scores and attributes

6. **Interactive Features**
   - Wire cursor movement to recalculate compliance
   - Update dotplot when cursors change
   - Persist selection across cursor changes
   - Export to Excel functionality

**Next Steps:** UI layout and XAML work to bring the ViewModels to life
