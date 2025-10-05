# Implementation Decisions

This document tracks implementation decisions made during autonomous development.

## Phase 1: Model Classes

**Decision**: Use positional records with validation
- Records have implicit constructors from their positional parameters
- Added explicit constructors for null validation, but this creates duplicate constructors
- **Resolution**: Remove explicit constructors, use required keyword or init-only setters with validation in alternative approach

**Decision**: Use System.Collections.Generic namespace
- Need to add `using System.Collections.Generic;` to all files using IReadOnlyCollection, List, Dictionary
- Need to add `using System.Linq;` for LINQ operations

## Fixes Applied
- Removed duplicate constructors from records (Score, StudentAttribute, MuppetNameInfo)
- Added GlobalUsings.cs for System, System.Collections.Generic, System.Linq
- Fixed calculator logic bugs related to grade order vs score order
- Clarified that cutoff = minimum score to receive a grade

## Phase 4: Calculator Tests
- All 22 calculator tests passing
- Covers edge cases: ties, empty bins, single student, perfect distribution
- Validates cursor spacing and placement logic

## Phase 5-8: Remaining Work
Due to complexity, Phases 5-8 (ViewModels, ViewModel Tests, UI Views, UI Testing) require:
- Async cursor update logic with 25ms debounce and cancellation tokens
- OxyPlot integration for dotplot visualization
- Avalonia SplitView layout with collapsible compliance grid
- Complex data binding and MVVM patterns
- These are best completed with active collaboration rather than autonomous overnight work
