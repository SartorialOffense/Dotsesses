# ADR-0017: Ordinal columns via a `~N` sort-order suffix

## Status

Accepted

## Context

Non-numeric columns load as Categorical StudentAttributes, and the
Significance Matrix orders their Subgroup labels alphabetically. Two
problems follow from alpha order: (1) a naturally ordered scale like
`✔ / ✔✔ / ✔✔+` or `Low / Medium / High` reads in the wrong order, and
(2) such a scale is genuinely ordinal — there is a meaningful rank
behind the labels — yet it is excluded from the violin/distribution and
correlation views entirely, because Categorical data carries no numeric
value.

We wanted a way to (a) control label order and (b) let a ranked
categorical participate as a numeric distribution, without a separate
mapping file or a manual per-column configuration UI. The data already
arrives from spreadsheets the user controls, so a value-encoding
convention is the cheapest channel.

## Decision

A categorical cell value may end with a `~N` suffix (`Pass~2`), decoded
once at load time in `ScoreReader` into a stripped label (`Pass`) and a
non-negative integer **SortOrder** (`2`) stored on `StudentAttribute`.
Matching is end-anchored, whitespace-tolerant around the `~`, and
last-`~`-wins.

The suffix does double duty:

- **Significance Matrix** — SortOrder orders a column's Subgroup labels
  (ownership of the ordering moves out of the Python `sorted()` call:
  C# now passes Python a pre-ordered label list). Unsuffixed values sort
  after suffixed ones, alphabetically among themselves. Same-label /
  different-`N` conflicts resolve to the minimum `N` with a load-time
  warning; different-label / same-`N` ties break alphabetically.

- **Ordinal columns** — when *every* non-empty cell in the column
  carries a valid `~N`, the column becomes a third `ScoreColumnType`,
  `Ordinal`. `N` is its numeric value, so it can appear in the violin
  and correlation views (label shown on hover), while still acting as a
  Significance Matrix *column* (never a row) via its labels. An Ordinal
  never contributes to AggregateScore. A partially-suffixed column is
  not Ordinal — it stays Categorical and emits a mixed-column warning.

Ordinal status is auto-detected only; there is no manual type override
this slice. Ordinals are not seeded into the Display (violin) default —
they are opt-in.

## Consequences

- The ordering rule lives in one place (C#, beside the SortOrder
  decode); Python becomes a dumb renderer of a caller-provided order,
  matching how `violin_swarm` already consumes `series_order`.
- The `~N` convention is effectively a file-format contract: once users
  bake it into spreadsheets it is hard to change, which is why it is
  recorded here. The tilde was chosen as an unlikely-in-real-data
  delimiter; a literal mid-string `~` with no trailing digits is left
  untouched.
- `Ordinal` is a genuine third column kind, so each plot-builder gains
  an explicit branch (cf. ADR-0013). The violin point payload must carry
  the label alongside the numeric value so hover can show `✔✔+`, not `3`.
- Alternatives rejected: a sidecar label→rank mapping (more files, more
  UI, divorced from the data) and a Settings dialog type/rank editor
  (heavier UX for what a suffix expresses inline). Both can layer on top
  later without invalidating the convention.
