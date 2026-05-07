# SavedState is versioned with default-cleanly migration

`SavedState` carries an explicit `Version` field and uses
`System.Text.Json` defaults for missing properties (e.g. `= new()` on
collection fields) so older `.dots` files load cleanly into newer code.
First save-after-load silently rewrites them at the current version.

This is the established pattern for forward-compatible persistence and
should be reused when the schema grows again (e.g. for multi-Class).

## Considered alternatives

A side-by-side migration script per version was considered and rejected:
the data is small, single-file, and user-facing — silent migration on
first re-save is the right ergonomics.
