# Implementation Questionnaire

Please answer these questions to help guide autonomous implementation through the night.

## Project Structure

**Q1:** Should I create a separate test project (e.g., `Dotsesses.Tests`) or keep tests in the main project initially?

**A1:**

Yes, separate project.

---

**Q2:** What test framework should I use? (xUnit, NUnit, MSTest)

**A2:**

xUnit FTW! And add this to the spec.


---

## Dependencies & Libraries

**Q3:** For Excel export, which library should I use?
- EPPlus
- ClosedXML
- NPOI
- CSV is fine for now
- Other: ___________

**A3:**
ClosedXML - add to spec
---

**Q4:** Should I set up Dependency Injection infrastructure from the start, or add it later when ViewModels need it?

**A4:**

just when you need it
---

**Q5:** For logging (ILog interface), should I use a specific library (Serilog, NLog, log4net) or implement a simple wrapper initially?

**A5:**
Serilog - add to spec
---

## MuppetName Generation

**Q6:** Should I create actual word/emoji lists for MuppetName generation, or use simple placeholders initially?
- Full lists (descriptors, characters, emojis)
- Simple placeholders (e.g., "Student 1", "Student 2")
- Moderate lists (10-20 options each)

**A6:**
So I had a change of heart. if you look at the Muppet Wiki, there are like 1000 muppet names. why not just use these instead of random combinations of size and color?
---

**Q7:** What constant seed value should I use for the MuppetName random generator? (e.g., 42, 12345, etc.)

**A7:**
42
---

## Implementation Details

**Q8:** For the `LetterGrade` enum, should I include all possible grades (A, A-, B+, B, B-, C+, C, C-, D+, D, D-, F) or just the ones mentioned in the spec (A, A-, B+, B, B-, C+, C, D, D-, F)?

**A8:**
just the ones mentioned. and this should be flexible eventually. IOW, when the user imports the
student raw data they would enter this with the school curve. for now we can make it static.
---

**Q9:** The spec mentions "MuppetNameInfo" but doesn't define its structure. Should I:
- Create a simple record with just the name string
- Include generation metadata (descriptor, character, emojis as separate properties)
- Wait for your guidance

**A9:**
MuppetNameInfo should be the string name (see clarification above) and the emojis.
---

**Q10:** For StudentAssessment.AggregateGrade, should the caching be:
- Lazy-loaded on first access
- Calculated on construction
- Recalculated when Scores change (with change tracking)

**A10:**
on construction.
---

## Decision-Making Authority

**Q11:** If I encounter ambiguities or spec gaps, should I:
- Make reasonable choices and document in DECISIONS.md
- Make reasonable choices and document in code comments only
- Stop and create a QUESTIONS.md file for morning review
- Other: ___________

**A11:**
make reasonable choices and track in decisions.md.
---

**Q12:** How far should I go? (Check phases to complete)
- [ ] Phase 1: Model Classes
- [ ] Phase 2: Calculators
- [ ] Phase 3: Synthetic Data Generators
- [ ] Phase 4: Calculator Unit Tests
- [ ] Phase 5: ViewModels
- [ ] Phase 6: ViewModel Tests
- [ ] Phase 7: UI Views
- [ ] Phase 8: UI Testing Harness

**A12:**

Try and get as far as you can. fortune favors the bold.

---

## Commit Strategy

**Q13:** How frequently should I commit?
- After each class file
- After each logical group (e.g., all records, all calculators)
- After each phase completes
- Other: ___________

**A13:**

After each phase completes.

---

**Q14:** Should commit messages follow a specific format beyond what you've been using?

**A14:**
just like the commmits you have been making. only need more documentation when it is an active collaboration with me.
---

## Edge Cases & Validation

**Q15:** Should I add validation to model classes (e.g., Score.Value >= 0, non-null names) or keep them simple POCOs initially?

**A15:**
no validation, but prohibit null names.
---

**Q16:** For cursor validation (1 point minimum apart), should this be:
- Hard enforcement (prevent movement)
- Soft enforcement (allow but warn)
- Automatic snap to valid position

**A16:**
hard
---

## Additional Notes

**Q17:** Any other guidance, preferences, or constraints I should know about?

**A17:**
make it pretty!
---

**Status:** ⏳ Awaiting answers before starting autonomous implementation
