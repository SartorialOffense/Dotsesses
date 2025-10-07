# Layout Clarification Questions

## 1. Plot Separation
The Figma shows three **separate** frames (Statistic Cursors, DotDisplay, Grade Cursors) vs. current spec where cursors overlay the dotplot. Confirm:
- Are these three independent OxyPlot instances that just share x-axis ranges?
- Or is this one combined plot with separate rendering areas?

Initially I was thinking 3 separate plots with same x-axis ranges, but using separate rendering areas makes some things simpler. Lets try this. One tricky thing,
I want the grades cursors and the stat cursors to be fixed height with the dot display height changing with the splitter changes.

## 2. Grade Region Bands
DotDisplay shows 4 alternating rectangles. Current spec mentions cursors define grades, but:
- Should grade region rectangles be drawn even when a grade's cursor is disabled?
no
- How transparent is "very very transparent gray/clear" (alpha value)?
alpha of 0x10
- Should rectangles dynamically resize based on cursor positions?
yes

## 3. Statistics Display Details
The Figma shows Mean + multiple ±σ markers, but:
- **How many std devs?** Show all that fit in range, or fixed count (±1σ, ±2σ, ±3σ)?
show as many as exist in the current dataset.

- **What if σ markers exceed data range?** (e.g., mean=200, σ=50, min score=0)
don't draw

- **Visual style:** Line thickness? Color? Dashed vs solid?
thickness 1, dashed, light gray

- **Labels:** Should markers show text ("μ", "+1σ", etc.) or just visual indicators?
show labels

## 4. Median Display
redo_layout.md says "displayed below the plot so it doesn't interfere with the mean":
- Where exactly? Below the x-axis? In a label area?
take a look at the placement in the figma design. under the stats cursor area. the other labels are above it. in oxyplot, this is
in the tick area although the regular ticks shouldn't be shown.

- What format? Text ("Median: 215")? Small marker symbol?
take a look at the figma design. single uppercase M.

- Same plot instance or separate text block?
?

## 5. Cursor Interaction
With Grade Cursors in separate frame:
- Are they still draggable? (Spec says yes)
yes
- Do crosshairs appear when dragging, or just the vertical line?
no crosshairs
- Should visual feedback connect to the DotDisplay (e.g., highlight affected region)?
just change the size of the region bands as the cursors move.

## 6. Grade Labels
Current spec: labels centered between cursors. With new layout:
- Do labels appear in Grade Cursors frame, DotDisplay frame, or both?
labels only show below the grade cursors
- Same semi-transparent style as before?
no, should be like regular text.
## 7. Border/Outline Styling
redo_layout.md says "outline faintly with a thin gray line" but current spec says "bottom border only":
- Should all three plots have full outlines (top/left/right/bottom)?
all three should have full outlines.
- Or just bottom borders as current spec?
- What about the borders shown in Figma metadata (Vector 2, Vector 3)?
yes. I renamed them in the figma design to say "border"
## 8. Clear Selections Button
Figma shows it in Drill Down frame (node 8:38), current spec says "above the dotplot":
- Which is correct?
it should be moved to above the drilldown. makes more sense.
- Should it be visible even when no students selected?
it should be disabled

## 9. Dynamic Grade Cursors
Figma shows 8 cursor instances (example data), but spec allows enabling/disabling:
- When a grade is disabled, does its region rectangle disappear too?
yes
- Does the Statistics display change when grades are enabled/disabled? (Probably not, right?)
no, the stats display is static - datawise.
## 10. X-Axis Alignment
All three plots show width=835px in Figma:
- Must they **always** stay pixel-perfect aligned during window resize?
yes
- Should they share a single x-axis component or each maintain their own?
As stated above, lets try with subplot regions.
## 11. Height Proportions
Figma shows heights: Statistics=41px, DotDisplay=145px, GradeCursors=50px:
- Are these fixed ratios or just example values?
the stats and grade cursors should be fixed. the dotdisplay should be resizeable with the below splitter.
- Can DotDisplay grow taller if many students stack vertically?
height is controlled by splitter.
- Should Statistics/GradeCursors stay fixed height?
yes

## 12. Splitter Behavior
Figma shows Middle Splitter (796x7px) between cursors and drill-down:
- Should this affect all three plots together, or just DotDisplay?
just dot display
- What are the min/max constraints for the plots area?
don't set these for now.
