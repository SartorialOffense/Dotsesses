# Hover Delay Service Implementation Plan

**Date**: 2024-12-25
**Status**: Complete

## Overview

Implement velocity-based hover delay to prevent accidental point selection when traversing the UI quickly, and require explicit clear instead of auto-clearing on blank space hover.

## Design Decisions

- **Architecture**: Centralized HoverDelayService (Option A) - single timer, single velocity tracker
- **Velocity tracking**: 10 samples, linearly weighted toward most recent
- **Mouse tracking**: Global hook at MainWindow level via PointerMoved
- **Service lifetime**: Injected singleton
- **Delay range**: 50ms (slow/deliberate) to 500ms (fast/traversing)
- **Velocity thresholds**: 100 px/sec (min) to 500 px/sec (max)
- **Stability tolerance**: 5px ("hasn't moved" threshold)

## Implementation Checklist

### Phase 1: Core Service
- [x] Create `Services/HoverDelayService.cs` with:
  - [x] Position history queue (10 samples with timestamps)
  - [x] Weighted velocity calculation
  - [x] Delay interpolation based on velocity
  - [x] Timer management for delayed hover activation
  - [x] Stability check (mouse hasn't moved)
  - [x] Observable `CurrentVelocity` property for debug display
  - [x] `ReportMousePosition(Point)` method
  - [x] `ReportHoverCandidate(int? studentId, Point position)` method
  - [x] `ClearHover()` method
  - [x] `OnHoverActivated` event/callback

### Phase 2: Dependency Injection Setup
- [x] Register HoverDelayService as singleton in `App.axaml.cs`
- [x] Inject into MainWindowViewModel constructor
- [x] Wire up `OnHoverActivated` to set `HoveredStudentId`

### Phase 3: Global Mouse Tracking
- [x] Add PointerMoved handler in `MainWindow.axaml.cs`
- [x] Pass service reference to MainWindow (via DataContext or direct injection)
- [x] Call `ReportMousePosition` on every pointer move

### Phase 4: Update Hover Candidate Reporting
- [x] Modify `MainWindowViewModel.OnDotplotMouseMove`:
  - [x] Remove direct `HoveredStudentId` assignment
  - [x] Call `ReportHoverCandidate` instead
  - [x] Remove auto-clear on blank space (null candidate)
- [x] Modify `ViolinPlot` / `ViolinPlotViewModel` hover handling:
  - [x] Route hover candidates through service
  - [x] Remove auto-clear behavior

### Phase 5: Explicit Clear Button
- [x] Add `ClearCommand` to `StudentCardViewModel`
- [x] Add clear button (× icon) to student card XAML
- [x] Wire command to `HoverDelayService.ClearHover()`

### Phase 6: Debug Display
- [x] Add velocity display TextBlock to MainWindow
- [x] Bind to service's `CurrentVelocity` property
- [x] Verify velocity calculation looks reasonable during testing

### Phase 7: Testing & Tuning
- [x] Test slow deliberate hover - should activate quickly (~50ms)
- [x] Test fast traversal - should not activate or activate slowly (~500ms)
- [x] Test stability check - moving mouse should cancel pending hover
- [x] Test explicit clear button
- [x] Test cross-view hover sync still works
- [ ] Tune velocity thresholds if needed (user can adjust)
- [ ] Remove debug display when satisfied (leaving for now)

## Files to Create/Modify

| File | Action |
|------|--------|
| `Services/HoverDelayService.cs` | Create |
| `App.axaml.cs` | Modify - DI registration |
| `UI/MainWindowViewModel.cs` | Modify - use service |
| `UI/MainWindow.axaml.cs` | Modify - global mouse tracking |
| `UI/ViolinPlotViewModel.cs` | Modify - use service |
| `UI/ViolinPlot.axaml.cs` | Modify - route through service |
| `UI/StudentCardViewModel.cs` | Modify - add ClearCommand |
| `UI/StudentCard.axaml` | Modify - add clear button |

## Notes

- Existing `StudentHoverMessage` system may still be useful for cross-view sync, or we may be able to simplify it since the service is now the source of truth
- Need to handle case where mouse leaves window entirely (velocity history becomes stale)
