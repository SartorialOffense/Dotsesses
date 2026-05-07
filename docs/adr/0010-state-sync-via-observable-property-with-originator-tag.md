# Cross-component state sync via observable state property with originator tagging

When multiple UI surfaces both *read and write* the same shared
state — the canonical case being the dotplot's and violin plot's
draggable cursor strips, which both display and mutate
`GradeCutoff`s — the producer (e.g. `GradingSession`) exposes:

1. An INPC-notifying property whose value carries both the new
   immutable state snapshot and an `Originator` reference (the
   object that initiated the change).
2. A small `IsFrom(object?)` predicate on that payload so consumers
   can bail on self-initiated change events without string-typed
   source identifiers.

```csharp
public sealed record GradingStateChange(object? Originator, GradingState State)
{
    public bool IsFrom(object? obj) => obj is not null && ReferenceEquals(obj, Originator);
}

// On GradingSession:
public GradingStateChange LastChange { get; private set; }   // INPC
```

Mutator methods accept an `object originator` parameter; pass-through
callers (`Settings → ApplyScoreSelections`, file load) pass `null`.

## Why

This codebase has multiple consumer shapes for the same notification:

- **Read-only consumers** (Compliance rows, Region Bands, Drill-down,
  Grade labels) bind via plain XAML — they need INPC, never need to
  know who wrote.
- **Read-and-write consumers** (the dotplot's and violin's drag
  handlers) need to bail on changes they themselves originated, to
  avoid feedback during continuous-drag commits.

Three alternatives were considered and rejected:

- **`IMessenger` broadcast.** Contradicts ADR-0004 (IMessenger is
  reserved for many-to-many across unrelated components; this is
  many-readers / one-state-owner — INPC is exactly that).
- **Dual-channel: INPC for bindings + a separate `Action<T>` event for
  originator-aware consumers.** Two notification paths to keep
  in sync; discoverable surface doubles for marginal benefit.
- **String / enum source identifiers (`Source = "dotplot"`).** Brittle,
  doesn't compose, doesn't survive renaming.

Embedding the originator in the INPC payload via a single property is
the simplest shape that serves both consumer types. Consumers that
don't care about origin ignore the field; consumers that do call
`IsFrom(this)`.

## Consequences

- The `LastChange` property and the `GradingStateChange` payload type
  are part of the public surface of every state-owner that needs
  this pattern. Other future state objects with the same shape
  (FilteredAssessmentView, …) follow this convention.
- Mutator methods on state objects carry an `originator` parameter
  (default `null` for non-drag callers).
- Tests assert the originator round-trip (`session.MoveCutoff(g, n,
  obj); Assert.True(session.LastChange.IsFrom(obj))`).
