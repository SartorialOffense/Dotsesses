# Save/Load State Feature

**Date**: 2025-12-31

## Summary

Add ability to save and load full application state (scores, comments, cursor positions) to/from JSON files.

## Requirements

- Save: All student data (scores with comments), cursor positions, enabled states
- Load: Restore full state from JSON file
- UI: Save/Load buttons above student card panel with icons + text
- File dialog: Always ask for location, remember last used directory
- Exit prompt: Prompt to save unsaved changes on exit

## JSON Structure

```json
{
  "version": 1,
  "savedAt": "2025-12-31T10:30:00Z",
  "sourceFile": "2025 IP Final Scores.xlsx",
  "students": [
    {
      "id": 1,
      "muppetName": "Kermit",
      "scores": [
        { "name": "Quiz", "index": 1, "value": 85.0, "comment": "Improved from midterm" },
        { "name": "Total", "index": null, "value": 275.0, "comment": null }
      ],
      "attributes": [
        { "name": "Section", "value": "A" }
      ]
    }
  ],
  "cursors": [
    { "grade": "A", "score": 285, "enabled": true },
    { "grade": "A-", "score": 270, "enabled": true }
  ]
}
```

## Implementation Checklist

### Phase 1: Make Models Mutable
- [ ] Update `Score` class - make `Value`, `Name`, `Index` settable
- [ ] Update `StudentAssessment` class - make properties settable, add parameterless constructor for deserialization

### Phase 2: Create DTOs for Serialization
- [ ] Create `SavedState.cs` - root object with version, timestamp, source file
- [ ] Create `SavedStudent.cs` - student ID, muppet name, scores, attributes
- [ ] Create `SavedScore.cs` - name, index, value, comment
- [ ] Create `SavedAttribute.cs` - name, value
- [ ] Create `SavedCursor.cs` - grade name, score, enabled

### Phase 3: Create StateService
- [ ] Create `IStateService` interface
- [ ] Create `StateService` implementation
- [ ] `SaveAsync(string filePath, ...)` - serialize state to JSON
- [ ] `LoadAsync(string filePath)` - deserialize state from JSON
- [ ] Track `HasUnsavedChanges` property
- [ ] Track `LastUsedDirectory` property

### Phase 4: Update MainWindowViewModel
- [ ] Add `SaveCommand` with async handler
- [ ] Add `LoadCommand` with async handler
- [ ] Add `HasUnsavedChanges` property
- [ ] Wire up change tracking (cursor moves, comment edits)
- [ ] Add `OnClosing` handler for exit prompt

### Phase 5: Update UI
- [ ] Add Save/Load button bar above student card in MainWindow.axaml
- [ ] Style buttons with icons (💾 📂) + text
- [ ] Wire up commands

### Phase 6: Exit Prompt
- [ ] Handle window closing event
- [ ] Show confirmation dialog if unsaved changes
- [ ] Allow save, discard, or cancel

### Phase 7: Unit Tests
- [ ] `StateServiceTests.cs`
  - [ ] `SaveAsync_SerializesAllData_ToValidJson`
  - [ ] `LoadAsync_DeserializesValidJson_ToCorrectState`
  - [ ] `LoadAsync_WithInvalidJson_ThrowsException`
  - [ ] `LoadAsync_WithMissingFields_HandlesGracefully`
  - [ ] `RoundTrip_SaveThenLoad_PreservesAllData`
- [ ] `SavedStateTests.cs`
  - [ ] `Serialization_PreservesStudentScores`
  - [ ] `Serialization_PreservesCursorPositions`
  - [ ] `Serialization_PreservesComments`

## Files to Create/Modify

### New Files
- `Models/SavedState.cs`
- `Models/SavedStudent.cs`
- `Models/SavedScore.cs`
- `Models/SavedAttribute.cs`
- `Models/SavedCursor.cs`
- `Services/IStateService.cs`
- `Services/StateService.cs`
- `Dotsesses.Tests/Services/StateServiceTests.cs`

### Modified Files
- `Models/Score.cs` - make mutable
- `Models/StudentAssessment.cs` - make mutable
- `UI/MainWindow.axaml` - add button bar
- `UI/MainWindowViewModel.cs` - add commands, change tracking
- `App.axaml.cs` - register StateService (if using DI)
