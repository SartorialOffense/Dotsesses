# One window per loaded Class — per-window DI scope

Each loaded analysis (one Class) lives in its own top-level
`MainWindow`, backed by its own **DI scope**. Scoped services
(`IMessenger`, `HoverDelayService`, `StateService`, and all
per-analysis ViewModels) get one instance per workspace. Stateless
singletons (`ViolinPlotService`, `CorrelationPlotService`) stay shared
across workspaces because they wrap expensive Python interop init.

A `WorkspaceFactory` (app-singleton) wraps
`IServiceScopeFactory.CreateScope()` and returns a disposable
`Workspace` that owns the scope. The scope is disposed when the
window closes, releasing the loaded `ClassAssessment`,
`GradingSession`, plot ViewModels, and scoped services for GC.

The static `WeakReferenceMessenger.Default` is **not used** anywhere
in production code. Each scope receives a fresh
`new WeakReferenceMessenger()` instance via the DI factory delegate.
ViewModels expose their injected `IMessenger` as a public `Messenger`
property so View code-behind can reach it through `DataContext`. Old
code that referenced `WeakReferenceMessenger.Default` directly
(`MainWindow`, `ViolinPlotControl`, `CorrelationPlotControl`,
`StudentCardViewModel`, `ImageCopyService`,
`PowerPointExportService`) has been migrated to the scoped messenger;
`ImageCopyService` and `PowerPointExportService` now take an
`IMessenger` parameter so the invoking window's messenger flows
through.

## Why

The PRD problem statement (#25) calls out that today's app holds one
Class at a time. We want a user to load N files and keep each in its
own independent window — drag a cursor in window A, window B is
untouched. The single-file assumption was baked into the static
`WeakReferenceMessenger.Default` broadcast bus and the singleton
`HoverDelayService`: any second window would share both with the
first, crossing wires on every `StudentHoverMessage`,
`StudentEditedMessage`, and hover-activation event.

Per-window DI scoping makes cross-window contamination
**structurally impossible**: window A's `ViolinPlotViewModel` is
subscribed to *its* messenger, not the static one. A message sent
in window B's scope is sent on a different instance entirely.

## Considered alternatives

- **A — Token-based message routing.** Keep the singleton messenger,
  introduce a per-window token, register/send with the token. Caller
  obligation grows everywhere a message is sent or registered; one
  forgotten token leaks cross-window. Easy to get wrong, hard to
  catch in review.
- **B — Per-window messenger but global static `HoverDelayService`.**
  Solves message routing but the hover service tracks "the current
  hovered student" as a singleton field; two windows hovering
  simultaneously would clobber each other's hover state.
- **C — Multi-Class store inside a single window.** This is the
  Candidate #2 sketch from issue #5: one window, an active-Class
  concept, per-Class state keyed inside the store. Heavier. The
  user-facing goal (multiple files independently) is satisfied more
  directly by separate windows. A heavier Class Store is not
  precluded — it can be filed separately later.

## Consequences

- Resolving `MainWindowViewModel` from the root `IServiceProvider`
  now throws (scoped services can't be resolved from the root).
  Every resolution must go through a `Workspace` produced by
  `WorkspaceFactory.Create()` — startup, snapshot mode, and (in a
  future slice) the *Open Another File* command.
- `Workspace` is `IDisposable`. The owner — `App.axaml.cs` today, the
  per-window lifecycle handler in a later slice — must dispose it
  when the window closes. Not disposing it leaks the scoped
  services until app shutdown.
- View code-behind reaches the scoped messenger through
  `DataContext` (the per-window VM). `DataContext`-set timing means
  message registrations move from the View constructor into
  `OnDataContextChanged`, guarded by a one-shot flag.
- The test factory `MainWindowViewModel.CreateForTesting()` now
  constructs a fresh `new WeakReferenceMessenger()` rather than
  using `.Default`, which keeps test cases isolated from each other
  (a quiet bonus).
- A `WorkspaceFactoryTests` asserts the contract: two factory-produced
  workspaces have distinct `IMessenger` and `HoverDelayService`
  instances; messages sent in one don't reach the other; disposing
  one doesn't affect the other.
- This ADR's scoping decision is what slice 2 (#27) relies on to
  introduce the *Open Another File* command. Slices 3–5 build on
  it without changing the scoping shape.
