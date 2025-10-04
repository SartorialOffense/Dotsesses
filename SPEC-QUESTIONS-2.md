# Dotsesses Specification - Follow-up Questions

Based on your answers in SPEC-QUESTIONS.md, here are some follow-up clarifications needed.

---

## MuppetName Generation

### 1. MuppetName Determinism
**Q:** Should the MuppetName be generated deterministically from the Student ID (so the same student always gets the same name across sessions), or is it just about ensuring uniqueness within a class (could be different each time the app loads)?

**A:**

I think if you first order by student id, then use a constant seed, you can just assign randomly. IOW, a student id doesn't need
to be tied to a name/emoji across ClassAssessmets.

---

### 2. MuppetName Structure
**Q:** What's the structure of the MuppetName? Examples:
- `[Size] [Color] [Character] [Emoji]` → "Large Purple Kermit 🐸"
- `[Color] [Character] [Emoji]` → "Purple Kermit 🐸"
- `[Adjective] [Character] [Emoji]` → "Fuzzy Kermit 🐸"
- Something else?

**A:**

i would pick the size/color/adjective first and then character and emoji. really, just like you suggested.

---

### 3. Emoji Assignment
**Q:** You said "they each get their own set of emoji" - do you mean:
- Multiple emojis per student (like "🐸🎭🎪")?
- Just one emoji that's part of their MuppetName?
- Emoji that changes based on their grade/performance?

**A:**

Multiple 1-3. Keep them constant.
---

## Cursor Placement Logic

### 4. Middle Cursor Placement Calculation
**Q:** When placing a newly-enabled cursor "in the middle of cursors around it" (e.g., enabling B between A- and B+), should it be placed:
- Middle of the *score range* between those cursors (if A- is at 280 and B+ is at 260, place B at 270)?
- Middle of the *student count* between them (place where ~half the students between those cursors are on each side)?

**A:**

middle of the score range. let's not make it too complicated.
---

### 5. Even Spacing Reset Calculation
**Q:** When all cursors reset to even spacing (because a new cursor can't fit), should the spacing be:
- Even across the actual score range (min score to max score)?
- Even spacing trying to match the default curve percentages/counts?
- Something else?

**A:**

score range

---

## Async Calculation

### 6. Cancellation Token Check
**Q:** In the async calculation flow with 25ms delay - when you say "check the CT on the UI context", do you mean:
- Check if a newer calculation has started (cursor moved again)?
- Check if the user cancelled the operation somehow?
- Something else?

**A:**
Yes, they moved the cursor again.
---

## Data Model Details

### 7. SavedCutoffs Structure
**Q:** The `SavedCutoffs` is currently `Dictionary<string, GradeCutoff>`. Should it actually be:
- `Dictionary<string, GradeCutoff[]>` - each entry saves a complete set of cutoffs for all grades?
- Keep as `Dictionary<string, GradeCutoff>` - each entry saves a single cutoff (with keys like "Strict A", "Lenient A-")?

**A:**
Dictionary<string, GradeCutoff[]>`
---

## Test Data Generation

### 8. Attribute Correlation
**Q:** For the test data, should the student attributes ("Submitted Outline", "Mid-Term") be:
- Independent (roll percentages separately for each attribute)?
- Correlated (e.g., students who didn't submit outline are more likely to have lower mid-term scores)?

**A:**

Lets do something tricky. 60% are correlated. The others are not.

---

## Initial Grade Cutoff Placement

### 9. Cutoff Placement Algorithm
**Q:** The default curve has grade names with counts (e.g., "A: 5 students, A-: 8 students"). How should initial cursor placement work:
- Calculate score thresholds that get as close as possible to those exact counts?
- Use percentages (e.g., top 5% get A, next 8% get A-, etc.)?
- Start evenly spaced and let the user adjust?

**A:**

Yes, start assigning the A cutoff for 5 students, but don't worry if you need to go over because multiple students
have the same score on the boundary.

---

## UI Layout

### 10. Grade Checkbox List Position
**Q:** The list of letter grades to check/uncheck - where should this be positioned:
- Left sidebar?
- Right sidebar near the compliance table?
- Top toolbar?
- Bottom area?
- Somewhere else?

**A:**

Left of the compliance table.


## Additional Notes

**Q:** Any other thoughts, clarifications, or "oh I forgot to mention..." items?

**A:**

Yup, since this is all fancy MVVM, we need to have unit testing for the calculation classes and the ViewModel classes.

---