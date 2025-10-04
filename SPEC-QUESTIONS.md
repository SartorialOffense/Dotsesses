# Dotsesses Specification - Clarifying Questions

Please fill in your answers below each question. We'll incorporate these into the main SPEC.md once complete.

---

## Data Model

### 1. StudentAssessment.AggregateGrade Calculation
**Q:** Should `AggregateGrade` be calculated automatically from the Scores array, or can it be set independently?

**A:**

Yes, in fact it should have been defined as a calculated property with caching.

---

### 2. Score.Index Nullability
**Q:** The `Score.Index` is nullable - is this because some scores don't have an index (like a single "Final" score vs "Quiz 1", "Quiz 2")? What are the use cases for null vs non-null?

**A:**

Yup!

---

### 3. ClassAssessment.Current vs CurrentCutoffs
**Q:** Can you clarify the relationship between:
- `CurrentCutoffs` (GradeCutoff[]) - I assume these are the actual score thresholds (e.g., "A = 285")
- `Current` (CutoffCount[]) - I assume these are the counts of students in each grade with current cutoffs

Is this correct?

**A:**
Yup
---

### 4. SavedCutoffs Dictionary Key
**Q:** `SavedCutoffs` is a Dictionary<string, GradeCutoff> - what's the string key? Is it a name for saved configurations like "Strict" or "Lenient"?

**A:**
I didn't create a section for this yet. The idea is that you might want to have saved sets of cutoffs so you can review them.
So the key would just be a name the user picked.
---

## Visualization & Layout

### 5. Dotplot Binning Behavior
**Q:** When you say "binning by the exact score" - if two students both have 275, do they stack vertically at x=275? And the Y-position stacking order is determined by student ID?

**A:**
Yes, it only matters to keep the dot position stable upon redisplay.
---

### 6. X-Axis Padding Amount
**Q:** You want "numerical space to the left of the lowest aggregate score" for additional cursors. Should this be:
- A fixed amount (like 10 points)?
- Proportional to the range (like 5% of score range)?
- Something else?

**A:**
I think you could pick the total number of letter grades as the number.
---

### 7. Cursor Minimum Separation
**Q:** When you say cursors must be "at least 1 apart" - does that mean 1 point on the score scale? (e.g., A- cursor at 280 means A cursor must be at 281 or higher?)

**A:**
yup
---

### 8. Initial Cursor Enabled State
**Q:** "Cursors not in the school defaults should not be enabled by default" - so if the default curve only has A, A-, B+, B, B-, C+, C, then D, D-, F cursors start disabled and hidden? Do they become visible but ghosted, or completely hidden?

**A:**
There should be a list of letter grades on the window that can be checked/unchecked. if they are unchecked, the corresponding cursor disappears
and the binning is recalculated.
---

### 9. Even Spacing Reset Logic
**Q:** When enabling a new cursor "not on the far left", you reset all enabled cursors to "even spacing across the dataset". Does this mean:
- Even spacing across the score range?
- Spacing calculated to match the default curve percentages?
- Something else?

**A:**

I changed my mind. A cursor that is enabled should trigger placement in the middle of the cursors around it (if it is in the middle), to the right
or left if on the far left or right of existing cursors. If the placement isn't possible (it would overlap with an existing cursor), then reset
all the cursor positions to be evenly spaced.

---

## Update Behavior

### 10. Background Calculator Timing
**Q:** When cursors move, you rebuild GradeCutoffs and run a calculator. Should this be:
- Debounced (wait until dragging stops)?
- Real-time (update continuously as cursor moves)?

**A:**

Lets do something clever. Calculations triggered on move, but add in a 25 ms delay with an async await, once the 25 ms are over, 
check a cancellation token on the ui context, if it was cancelled just give up, if not - perform the recalculation on a BG task but don't
update the ui, then check the CT on the ui context, if cancelled give up, if not update the ui. then it is super slick.

---

### 11. Curve Compliance Deviation
**Q:** The compliance table shows "absolute deviation if > 0" - is this the difference between the default curve count and current count for each grade?

**A:**

Yes. show a deviation from the school default count. if 0, no display.

---

## UI/UX Behavior

### 12. Selection Persistence
**Q:** When students are selected and shown in drill-down cards, does selection persist if you move cursors? Or does cursor movement clear selection?

**A:**

Yup, persist the selection.

---

### 13. Drill-Down Card Content
**Q:** What should each student drill-down card show? Options:
- Just the scores and attributes?
- Also their assigned grade based on current cursors?
- Student ID?
- Something else?

**A:**

Yes, all that. have the student MuppetName (see below) and assigned grade at the top followed by 2 tabular displays of the scores and attributes in a consistently sorted order.

---

### 14. Grade Annotation Transparency Behavior
**Q:** The "semi-transparent" letter grade annotations - should these:
- Have a fixed transparency level?
- Fade out more when hovering over students to improve visibility?
- Adjust based on some other interaction?

**A:**

Fixed transparency level for now.

---

## Additional Questions/Notes

**Q:** Any other clarifications, edge cases, or behaviors you want to specify?

**A:**

Yes! Student id's are boring. How about you deterministically assign candy, cute animal, muppet, cartoon character names perhaps paired with
other fun descriptors like size, shape, and color? Walk through the students and pick a random pairing and just make sure it hasn't been
used in this class before. If it has, you pick another random set. just track this mapping in the ClassAssesment object. OOoh and they each get
their own set of emoji. that would be so cool. We can generically call this a MuppetName.

---
