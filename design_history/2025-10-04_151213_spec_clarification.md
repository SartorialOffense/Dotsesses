# Specification Clarification Session
**Date:** 2025-10-04
**Time:** 15:12:13 UTC
**Purpose:** Clarify and refine the Dotsesses specification through Q&A

## Overview

This document captures the design decisions and clarifications made during the initial specification review. The questions were organized into two rounds to progressively refine the specification details.

---

## Round 1: Core Clarifications

### Data Model

#### 1. StudentAssessment.AggregateGrade Calculation
**Decision:** `AggregateGrade` should be a calculated property with caching, automatically computed from the Scores array.

**Rationale:** This ensures the aggregate is always in sync with individual scores and prevents data inconsistency.

---

#### 2. Score.Index Nullability
**Decision:** `Score.Index` is nullable because some scores don't have an index.

**Example:**
- "Quiz 1", "Quiz 2" have indices (1, 2)
- "Final" has no index (null)

---

#### 3. ClassAssessment.Current vs CurrentCutoffs
**Decision:** Confirmed the relationship:
- `CurrentCutoffs` (GradeCutoff[]) = actual score thresholds (e.g., "A = 285")
- `Current` (CutoffCount[]) = counts of students in each grade with current cutoffs

---

#### 4. SavedCutoffs Structure
**Decision:** User-picked names for saved cutoff configurations (e.g., "Strict", "Lenient", "First Attempt").

**Note:** Updated to `Dictionary<string, GradeCutoff[]>` in Round 2 to save complete sets.

---

### Visualization & Layout

#### 5. Dotplot Binning Behavior
**Decision:** Students with identical scores stack vertically at that x-position, ordered by student ID.

**Rationale:** Keeps dot positions stable across redraws for consistency.

---

#### 6. X-Axis Padding Amount
**Decision:** Use the total number of letter grades (10) as the padding amount to the left of the lowest score.

**Rationale:** Provides space for all possible grade cursors to be placed.

---

#### 7. Cursor Minimum Separation
**Decision:** Cursors must be at least 1 point apart on the score scale.

**Example:** If A- cursor is at 280, A cursor must be at 281 or higher.

---

#### 8. Initial Cursor Enabled State
**Decision:** Grade checkboxes control cursor visibility. Unchecked grades hide their cursors and recalculate binning.

**Implementation:** Checkboxes positioned left of the compliance table.

---

#### 9. Cursor Placement Logic (Revised)
**Decision:** When enabling a cursor:
1. If between existing cursors: place in the middle of the score range
2. If at edges: place to the left/right of existing cursors
3. If placement causes overlap: reset ALL cursors to even spacing across score range

---

### Update Behavior

#### 10. Background Calculator Timing
**Decision:** Implement smart async updates with 25ms delay and cancellation tokens.

**Algorithm:**
1. Trigger calculation on cursor move
2. Wait 25ms (async)
3. Check cancellation token on UI context
4. If cancelled (cursor moved again), abort
5. If not cancelled, perform calculation on background task
6. Check cancellation token again before UI update
7. Update UI only if not cancelled

**Rationale:** Provides smooth, responsive updates without unnecessary calculations during rapid cursor movement.

---

#### 11. Curve Compliance Deviation
**Decision:** Show absolute deviation from school default count only if > 0.

**Example:** If default expects 5 A's and current has 7, show "+2" deviation.

---

### UI/UX Behavior

#### 12. Selection Persistence
**Decision:** Student selection persists when cursors move.

**Rationale:** Allows comparison of specific students while exploring different grading schemes.

---

#### 13. Drill-Down Card Content
**Decision:** Each student card displays (top to bottom):
1. MuppetName and assigned grade (header)
2. Scores table (sorted consistently)
3. Attributes table (sorted consistently)

---

#### 14. Grade Annotation Transparency
**Decision:** Fixed transparency level for now (no dynamic fading).

---

#### 15. MuppetName Feature (New!)
**Decision:** Replace boring student IDs with fun, deterministic names.

**Concept:** Generate whimsical names using random pairings of:
- Descriptors (size, color, adjective)
- Characters (candy, animals, muppets, cartoons)
- Emojis (1-3 per student)

**Example:** "Large Purple Kermit 🐸🎭🎪"

**Implementation:** Track mapping in ClassAssessment object, ensure uniqueness within class.

---

## Round 2: Follow-up Refinements

### MuppetName Generation Details

#### 1. MuppetName Determinism
**Decision:** Order students by ID, use constant seed, then assign randomly.

**Implication:** Student IDs don't need consistent names across different ClassAssessments.

---

#### 2. MuppetName Structure
**Decision:** `[Size/Color/Adjective] [Character] [Emojis]`

**Examples:**
- "Tiny Blue Cookie Monster 🍪🎪🎨"
- "Large Fuzzy Elmo 🔴🎭🎈"

---

#### 3. Emoji Assignment
**Decision:** 1-3 emojis per student, kept constant once assigned.

---

### Cursor Logic Refinements

#### 4. Middle Cursor Placement
**Decision:** Use middle of score range (simple arithmetic mean).

**Example:** A- at 280, B+ at 260 → place B at 270

**Rationale:** Keep it simple, avoid complicated count-based calculations.

---

#### 5. Even Spacing Reset
**Decision:** Even spacing across actual score range (min to max).

**Not:** Percentage-based or curve-matching spacing.

---

### Async Calculation Refinement

#### 6. Cancellation Token Semantics
**Decision:** Cancellation token detects if cursor moved again (newer calculation started).

---

### Data Model Corrections

#### 7. SavedCutoffs Type Fix
**Decision:** Change from `Dictionary<string, GradeCutoff>` to `Dictionary<string, GradeCutoff[]>`

**Rationale:** Save complete sets of cutoffs, not individual grades.

---

### Test Data Specifications

#### 8. Attribute Correlation
**Decision:** 60% of students have correlated attributes, 40% have independent attributes.

**Implication:** Adds realistic variation - some students defy patterns.

---

### Initial Cutoff Algorithm

#### 9. Cutoff Placement Strategy
**Decision:** Assign cutoffs to match default counts, but allow overflow for tied scores.

**Example:** If 5 students should get A's, but 3 students are tied at the boundary, give all 3 the grade (results in 6 A's).

**Rationale:** Fairness - don't arbitrarily split tied students.

---

### UI Layout Finalization

#### 10. Grade Checkbox Position
**Decision:** Place grade checkboxes to the left of the compliance table.

---

### Testing Requirements (New!)

#### 11. Unit Testing Mandate
**Decision:** Require unit tests for:
- Calculation classes
- ViewModel classes

**Rationale:** MVVM architecture facilitates testability, should leverage it.

---

## Summary of Key Decisions

### Architecture
- **Calculated Properties:** AggregateGrade cached and computed from Scores
- **Async Updates:** 25ms debounce with cancellation token optimization
- **Testing:** Unit tests required for calculations and ViewModels

### UX Features
- **MuppetNames:** Whimsical student identifiers with emojis
- **Cursor Logic:** Score-range-based placement with even-spacing fallback
- **Persistent Selection:** Student selection survives cursor movement
- **Grade Checkboxes:** Control cursor visibility, left of compliance table

### Data Modeling
- **SavedCutoffs:** Complete sets stored as `Dictionary<string, GradeCutoff[]>`
- **Test Data:** 60% correlated attributes, realistic variation
- **Fair Grading:** Allow count overflow for tied boundary scores

### Visual Design
- **X-Axis Padding:** 10 points (total letter grades count)
- **Cursor Separation:** Minimum 1 point apart
- **Fixed Transparency:** Grade annotations non-dynamic (for now)

---

## Implementation Implications

1. **MuppetName Generator:** Need word lists and emoji sets for random generation
2. **Async Calculator:** Implement cancellation token infrastructure
3. **Cursor Manager:** Complex placement logic with overlap detection
4. **Test Data Builder:** Correlated attribute generation algorithm
5. **Unit Test Infrastructure:** xUnit/NUnit setup for ViewModels and calculations

---
