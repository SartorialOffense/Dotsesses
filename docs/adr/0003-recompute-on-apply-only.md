# Plot recompute fires on Apply, never live

When the user toggles ScoreSelection checkboxes in the Settings dialog,
recompute (violin regen, correlation regen, dotplot rebuild,
Compliance refresh) fires only on **Apply** or **Close** — never on
each individual checkbox toggle. There is also no dirty-state UI
indicator and no "discard changes?" warning on Cancel.

## Why

Python regeneration of the violin plot and correlation matrix takes
hundreds of milliseconds each. Live recompute on every checkbox click
would lock the UI. The user has explicitly chosen this timing and
explicitly opted out of dirty-state indicators — they trust themselves
to remember what they toggled.

## Consequences

Any future feature that introduces user-toggleable inputs that drive
expensive recompute (filters, multi-Class switching, statistical
options) should follow the same pattern: stage in dialog state, commit
on Apply.
