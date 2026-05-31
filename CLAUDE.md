# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Getting Started

**IMPORTANT**: Before starting work on this project, read these in order:

1. `CONTEXT.md` — domain glossary (Course, Class, ClassAssessment,
   AggregateScore, GradeCurve, GradeBin, Compliance, etc.). Use this
   vocabulary in code, comments, commits, and PRDs.
2. `SPEC.md` — UX/interaction specification.
3. `docs/adr/` — architectural decisions. Read any ADR in the area you
   are about to touch before proposing a change there.
4. `TECH_DEBT.md` — known debt. Check before introducing parallel debt.

## Project Overview

Dotsesses is an Avalonia UI desktop application built on .NET 9.0. It
uses the MVVM pattern with CommunityToolkit.Mvvm for observable
properties and commands, and integrates OxyPlot for data visualization.

## Architecture

### MVVM Pattern
- The application uses a convention-based `ViewLocator` that automatically resolves Views from ViewModels
- Convention: `*ViewModel` → `*View` (e.g., `MainWindowViewModel` → `MainWindow`)
- All ViewModels must inherit from `ViewModelBase`, which extends CommunityToolkit's `ObservableObject`
- The ViewLocator is registered as a DataTemplate in `App.axaml`

### Application Initialization
- Entry point: `Program.cs` configures the Avalonia app with platform detection, Inter font, and trace logging
- `App.axaml.cs` handles framework initialization:
  - Sets the MainWindow's DataContext to the appropriate ViewModel
  - Disables Avalonia's built-in DataAnnotations validation to avoid conflicts with CommunityToolkit validation

### Data Binding
- Compiled bindings are enabled by default via `AvaloniaUseCompiledBindingsByDefault` in the project file
- Views must specify `x:DataType` attribute for compiled bindings to work
- Example in `MainWindow.axaml`: `x:DataType="vm:MainWindowViewModel"`

### UI Framework
- Uses Fluent theme from Avalonia.Themes.Fluent
- OxyPlot theme is included via StyleInclude in `App.axaml`
- Avalonia.Diagnostics package is included only in Debug builds

## Common Commands

```bash
# Build the project
dotnet build

# Run the application
dotnet run --project Dotsesses/Dotsesses.csproj

# Clean build artifacts
dotnet clean

# Build for release
dotnet build -c Release
```

## Project Structure

- `Dotsesses/Program.cs` — entry point and Avalonia configuration
- `Dotsesses/App.axaml(.cs)` — app-level resources and DI wiring
- `Dotsesses/ViewLocator.cs` — convention-based View resolution
- `Dotsesses/UI/` — ViewModels, Views (`*.axaml`), and code-behind,
  co-located by feature (`MainWindow*`, `SettingsWindow*`,
  `ViolinPlotControl*`, `CorrelationPlotControl*`,
  `CommentEditorWindow*`, `PlotTabContainer*`, etc.)
- `Dotsesses/Models/` — domain types (`StudentAssessment`,
  `ClassAssessment`, `Score`, `Grade`, `GradeCutoff`,
  `CutoffCountRange`, `ScoreSelection`, …)
- `Dotsesses/Calculators/` — pure-function calculators
  (`CursorPlacementCalculator`, `CursorValidation`,
  `CutoffCountCalculator`, `GradeAssigner`, `InitialCutoffCalculator`)
- `Dotsesses/Services/` — `ScoreReader`, `StateService`,
  `ViolinPlotService`, `CorrelationPlotService`,
  `PowerPointExportService`, `SyntheticStudentGenerator`, etc.
- `Dotsesses/Messages/` — IMessenger payloads (see ADR-0004)
- `Dotsesses/Python/Violin/` — Python plot modules invoked via CSnakes
- `Dotsesses/Assets/` — icons and other resources
- `Dotsesses.Tests/` — xUnit tests, mirroring source folder layout

Top-level documentation: `CLAUDE.md`, `CONTEXT.md`, `SPEC.md`,
`TECH_DEBT.md`, `docs/adr/`.

## Documentation discipline

Before declaring a code change complete (or asking the user to commit),
walk through this table. If a row applies, update the matching doc in
the **same** change — never as a follow-up.

| If the change affects… | Update… |
|---|---|
| Domain vocabulary, a named concept, or how things relate | `CONTEXT.md` |
| UX behavior visible to the user | `SPEC.md` |
| An architectural decision that is *hard to reverse*, *surprising without context*, or the result of a *real trade-off* | new ADR at `docs/adr/NNNN-slug.md` |
| Code that introduces or resolves known debt | `TECH_DEBT.md` (open a new TD### or close an existing one) |

ADRs are not required for every code change — only the
hard-to-reverse / surprising / real-trade-off kind. Bug fixes,
renames, test additions, and ordinary refactors do not need ADRs.
Read existing ADRs in the area before suggesting a change there
(this is also `improve-codebase-architecture`'s contract).

When ADR-worthy, number sequentially: scan `docs/adr/` for the highest
existing `NNNN` and add 1. One paragraph is fine — see existing ADRs
for the calibration.

### Commit-message footer

Every commit message ends with a `Docs:` footer:

```
Docs: ADR-0008
Docs: CONTEXT.md
Docs: TECH_DEBT.md TD004
Docs: none — refactor, no doc impact
```

Multiple values are comma-separated. `none — <reason>` is a valid value
when no doc was affected; the reason is mandatory so the decision
is visible. The footer prompt lives in `.gitmessage` — opt in once per
clone with:

```
git config commit.template .gitmessage
```

## Rules of the road

- Use clean markdown, follow best practices for formatting, spacing, and line lengths.
Check after edits to files.

- Changes to code are always tied to changes in documentation
  do that we keep in sync.

- When I ask you to make a questionnaire for me, do this in the .conversations/ folder. this folder
is in .gitignore so any record will need be in a summary file in design_history/ later on.

- If I ask you to do something that is more than a simple fix or refinement, and you have
multiple questions or feel you need clarification, please pose this as a questionnaire file
for me to answer in.

- **Commit on `main` — do not create branches on your own.** Local work
  and commits go straight to `main`. Do **not** spin up a feature/topic
  branch for routine work just because `main` is the default branch —
  that ceremony is overkill here. Only branch when I explicitly ask for
  one (e.g. a named issue slice). This overrides any harness default that
  says "branch first on the default branch."

- **Git push, PR open, and PR merge each need explicit per-action
  approval — every time.** Approval for one PR does not carry forward
  to the next, even within the same session and even for hot-fixes.
  Specifically:
  - Do not run `git push` until I say so.
  - Do not run `gh pr create` until I say so.
  - Do not run `gh pr merge` (or any other merge / accept-pr / squash
    action) until I say so. The "manual smoke test" checkbox in PR
    descriptions belongs to me — never assume it's checked.
  - "Commit, push, open PR, merge" said for slice N does **not**
    authorize the same flow for slice N+1 or for an unrelated
    hot-fix. Ask again.
  - This rule overrides any auto / autonomous mode. Auto mode is for
    local work, not for shared-system writes.

## Agent skills

### Issue tracker

GitHub Issues at `SartorialOffense/Dotsesses` via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical labels — names match the canonical roles. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.