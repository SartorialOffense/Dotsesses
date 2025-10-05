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

## Phase 6: Interactive Features - COMPLETED

**Completed:**
1. **Cursors on Dotplot** ✓
   - Gold vertical line annotations (255, 215, 0) for visibility
   - Semi-transparent grade labels with dark backgrounds
   - Cursors sync with compliance grid checkboxes

2. **Full Layout** ✓
   - Two-row Grid: dotplot/compliance (top), student cards (bottom)
   - SplitView with 300px right pane for compliance grid
   - Hamburger toggle button for compliance pane
   - Dark theme throughout (#1E1E1E, #2D2D30, #252526)

3. **Compliance Grid UI** ✓
   - ItemsControl with checkboxes for each grade
   - Shows Grade, Target Count, Current Count, Deviation
   - Checkboxes wired to enable/disable cursors
   - Red deviation indicators (#FF6B6B) when not compliant

4. **Student Cards** ✓
   - Two-column card layout: Scores | Attributes
   - Click-to-select on dotplot points (within 5-unit radius)
   - Cards display in WrapPanel with 350px width
   - Assigned grade badge in blue (#007ACC)

5. **Hover Tooltips** ✓
   - Muppet name and score displayed on hover
   - OxyPlot default dark tracker styling

6. **Interactive Features** ✓
   - Checkbox changes trigger cursor visibility updates
   - Dotplot click selects/deselects students
   - Student cards populate on selection
   - All state synchronized between views

**Note:** MouseDown event on PlotModel is deprecated (CS0618 warning). Will need to migrate to newer event handling in future OxyPlot versions.

**Next Steps:** Cursor dragging, validation, debouncing, Excel export
