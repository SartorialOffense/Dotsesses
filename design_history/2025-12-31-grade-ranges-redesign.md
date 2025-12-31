# Grade Ranges Redesign

**Date**: 2025-12-31

## Summary

Change grade distribution from fixed student counts to percentage-based calculation.

## New Grade Set (11 grades)

| Grade | Min % | Max % |
|-------|-------|-------|
| A     | 5%    | 10%   |
| A-    | 5%    | 15%   |
| B+    | 10%   | 20%   |
| B     | 20%   | 30%   |
| B-    | 10%   | 25%   |
| C+    | 10%   | 25%   |
| C     | 5%    | 20%   |
| C-    | —     | —     |
| D+    | —     | —     |
| D     | —     | —     |
| F     | —     | —     |

## Changes Required

1. **LetterGrade enum** (`Models/LetterGrade.cs`)
   - Remove: `DMinus`
   - Add: `CMinus`, `DPlus`
   - New order: A, AMinus, BPlus, B, BMinus, CPlus, C, CMinus, DPlus, D, F

2. **Grade.DisplayName** (`Models/Grade.cs`)
   - Add mappings for `CMinus` → "C-" and `DPlus` → "D+"
   - Remove `DMinus` mapping

3. **DefaultCurveGenerator** (`Services/DefaultCurveGenerator.cs`)
   - Update `GenerateRanges()` to take `int studentCount` parameter
   - Calculate bounds as percentage of student count, rounded to nearest int
   - Grades without ranges (C-, D+, D, F) get LowerBound=0, UpperBound=0

4. **DefaultCurveGenerator.GetAllGrades()**
   - Return new 11-grade list with correct orders

5. **MainWindowViewModel.InitializeWithSyntheticData()**
   - Pass `students.Count` to `GenerateRanges()`

6. **Update tests**
   - Fix any tests that depend on old grade structure
