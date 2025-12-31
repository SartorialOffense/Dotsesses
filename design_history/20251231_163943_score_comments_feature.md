# Score Comments Feature

**Date:** 2024-12-31
**Status:** In Progress

## Overview

Scores can now have comments. Columns ending with `(Notes)` in the Excel file contain semicolon-delimited comments for the corresponding score column.

## Tasks

- [ ] Modify Score model to add Comment property with change notification
- [ ] Remove Comment property from StudentAssessment
- [ ] Update ScoreReader to parse (Notes) columns
- [ ] Update Student Card UI with auto-sizing comment TextBoxes per score
- [ ] Update Violin Plot hover tooltip to show student ID + score comment
- [ ] Update Violin Plot square logic to use Score.Comment
- [ ] Update ViolinDataPoint creation to pass Score.Comment
- [ ] Update file path to 2025 IP Final Scores.xlsx
- [ ] Add/modify tests for score comments

## Design Details

### Score Model
- Add mutable `Comment` property to `Score` record
- Support property change notification for V/VM binding

### ScoreReader Changes
- Detect columns ending with `(Notes)`
- Match to corresponding score column (e.g., `Q1ab(Notes)` -> `Q1ab`)
- Store text as `Comment` on the matching `Score`
- Skip `(Notes)` columns when creating Score entries
- Replace `;` with `\n` in comments

### StudentAssessment Changes
- Remove `Comment` property (Total score's comment replaces it)

### Student Card UI
- For each score (including Total):
  - Row: Score name + value
  - Below: Editable TextBox for comment
- TextBox features:
  - V/VM binding for two-way editing
  - Auto-size vertically (AcceptsReturn, TextWrapping, no fixed height)
- Remove separate "Comment" section at bottom

### Violin Plot Changes
- Hover tooltip: Show student ID, newline, then comment for that score
- Square logic: Check if specific `Score.Comment` is non-empty
- ViolinDataPoint: Pass `Score.Comment` when creating data points

### Test File
- Switch from 2024 to `2025 IP Final Scores.xlsx`

## Files to Modify

1. `Models/Score.cs`
2. `Models/StudentAssessment.cs`
3. `Services/ScoreReader.cs`
4. `UI/MainWindow.axaml`
5. `UI/ViolinPlotControl.axaml.cs` or `ViolinPlotViewModel.cs`
6. `MainWindowViewModel.cs` (ViolinDataPoint creation)
7. File path reference
8. Test files
