# Button Style Consolidation — 2026-05-03 (M002/S04)

Tracks the design context behind the R036 milestone: a single named-Style
resource for every standard app button, opt-in via `Classes="…"`, with
explicit escape hatches for special-case buttons.

---

## (a) Which buttons migrated, which stayed special

**Migrated to shared Styles (8 call sites):**

| File | Buttons | Class |
| --- | --- | --- |
| `Dotsesses/UI/MainWindow.axaml` | Save, Export, PPTX, Settings (toolbar) | `app` |
| `Dotsesses/UI/SettingsWindow.axaml` | Apply | `dialog-primary` |
| `Dotsesses/UI/SettingsWindow.axaml` | Dismiss (dynamic Close/Cancel label) | `dialog-secondary` |
| `Dotsesses/UI/CommentEditorWindow.axaml` | OK | `dialog-primary` |
| `Dotsesses/UI/CommentEditorWindow.axaml` | Cancel | `dialog-secondary` |

**Stayed special (intentional, marked `<!-- Special-case: <reason> -->`
where they live alongside migrated buttons):**

- `CopyDotPlotButton`, `CopyViolinPlotButton`, `CopyCorrelationPlotButton`,
  `DiagonalToggleButton` — semi-transparent overlays anchored over their
  respective plots; their hover/press feedback is tuned to read on top of
  the plot canvas, not on the toolbar background.
- Score-card "X" clear button — micro-control inside a tight card; the
  standard `Button.app` padding would dominate the cell.
- SettingsWindow column-header `All` / `None` bulk-toggle buttons —
  `FontSize=10`, near-zero padding so they fit in the column header strip.
- PlotTabContainer Distribution / Correlation tab buttons — transparent +
  selected/unselected state machine with bound brushes; not a "press to
  trigger" action button.

The shared Styles live in `Dotsesses/App.axaml` as
`<Style Selector="Button.app">`, `<Style Selector="Button.dialog-primary">`,
`<Style Selector="Button.dialog-secondary">` plus `:pointerover` /
`:pressed` / `:disabled` siblings.

## (b) Selector strategy: class-based opt-in (and why)

Three patterns were considered during S04 research:

1. **`<Style Selector="Button">`** — implicit, applies to every Button.
   Rejected: would steal styling from every overlay/tab/special button
   and force per-instance overrides everywhere we *don't* want the
   standard look.
2. **Named `<StaticResource>` Style with `Style="{StaticResource …}"`** —
   explicit per-Button. Works, but adds verbose XAML to every standard
   call site and doesn't let the consumer mix multiple style facets.
3. **Class-based opt-in with `Classes="app"` /
   `Classes="dialog-primary"`** — chosen. Standard buttons opt in by
   name; special buttons get nothing and inherit only the Fluent default.
   Reads like CSS classes, matches Avalonia idioms, and leaves room for
   future facets (e.g., `Classes="app danger"`) without breaking the
   migration.

The `Selector="Button.app"` form means the rule fires only on Buttons
that declared `Classes="app"`, so adding a new overlay or tab button
never accidentally inherits the standard look — silence is the default,
opt-in is the contract.

## (c) Fluent template-part gotcha (record for future contributors)

Avalonia 11's Fluent theme defines its hover/press setters on the inner
`ContentPresenter#PART_ContentPresenter` template part, not on the
Button itself. A naive
`<Style Selector="Button.app:pointerover"><Setter Property="Background" Value="…"/>`
*appears* to do nothing — the Fluent template-part setter wins on
specificity and overwrites the color the moment the cursor enters.

The fix used here (see `App.axaml`, all `:pointerover` / `:pressed`
siblings):

```xml
<Style Selector="Button.app:pointerover /template/ ContentPresenter#PART_ContentPresenter">
  <Setter Property="Background" Value="#3A3A3D" />
  <Setter Property="BorderBrush" Value="#5A5A60" />
</Style>
```

Reach into the template part via the `/template/` combinator and the
same specificity that Fluent uses. This is the same trick MEM039's
dynamic-label work needed; it's recorded again here because it bites
every contributor who tries to restyle a Fluent-theme Button for the
first time.

A second related gotcha: `Button.app:disabled` needs an explicit
`Opacity 0.5` setter because MEM014's permanently-disabled aggregate
commands (e.g., `AggregateNoneCommand`) otherwise render at full
opacity under the new style — looks active but isn't. The starting
opacity is in the resource and is the one knob to turn if disabled
buttons read too dim or too bright in UAT.

## (d) Starting padding numbers (and where to dial them)

All knobs live in `Dotsesses/App.axaml`. Current values (intentionally
tighter than M001's inline `Padding="12,3"`):

| Style | Padding | Notes |
| --- | --- | --- |
| `Button.app` | `12,4` | toolbar standard; +1 vertical comfort vs M001 |
| `Button.dialog-primary` | `16,6` | larger hit target for primary commit |
| `Button.dialog-secondary` | `16,6` | matches primary so dialog buttons align |

If S05 UAT reports the toolbar still feels too wide, dial only the
`Button.app` Padding setter — every consuming Button picks it up
automatically. Same for icon-text spacing (`<StackPanel Spacing>` inside
the ContentTemplate-style call sites is per-call-site for now; if it
drifts again the next refactor should hoist it into the Style as well).

The corner-radius and FontSize setters are also in `App.axaml`; treat
them as single-source-of-truth and never re-introduce inline overrides
on standard call sites — that drift is exactly what R036 was filed to
retire.
