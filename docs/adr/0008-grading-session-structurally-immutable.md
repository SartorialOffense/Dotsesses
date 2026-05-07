# GradingSession is structurally immutable; replace, don't restructure

A `GradingSession`'s structural shape — the set of Grades it manages
and therefore the set of `CutoffSlot`s it exposes — is fixed at
construction time from the loaded `ClassAssessment`. Within its
lifetime, only per-`CutoffSlot.Score`, per-Grade `EnabledGrades`
membership, and derived `CutoffCount`s mutate. Structural changes
(different Grade set, different `GradeCurve`) mean constructing a
fresh `GradingSession` and rebuilding the dependent view models —
never in-place restructuring.

## Why

The session has multiple consumers — `CutoffSlot` bindings on the
dotplot and violin plot, `ComplianceGridViewModel`, the dotplot's
Region Bands, and the Drill-down's assigned-Grade lookup. Stable slot
identity across the session lifetime is what lets `ItemsControl`s
avoid teardown/rebuild churn during cursor moves and lets observers
cache slot references safely. If the slot collection could grow or
shrink mid-session, every observer would need to defensively
re-subscribe on each structural mutation — for no user-visible
benefit, since structural change in this app is a file-load event,
not a per-cursor-move event.

## Considered alternatives

In-place `AddGrade` / `RemoveGrade` methods on `GradingSession` were
considered. Rejected: every observer would have to handle "my slot
disappeared" as a runtime case, the API surface for mutators would
double, and reseed-on-structural-change behavior would become
ambiguous (what should happen to existing cursor positions if a Grade
is removed?). A clean rebuild dodges all of that.

## Consequences

- `GradingSession` exposes `EnableGrade` / `DisableGrade` (toggling
  visibility within a fixed slot set) but no `AddGrade` / `RemoveGrade`.
- File load and any future "different `GradeCurve` from this Excel
  file" flow construct a fresh session and reassign downstream view
  models via `MainWindowViewModel.GradingSession = new GradingSession(...)`.
  XAML bindings and `ObservableProperty` `PropertyChanged` refresh
  consumers.
- Tests that exercise structural change construct a new session
  rather than calling restructuring methods.
