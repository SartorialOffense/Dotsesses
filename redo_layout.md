# Redo of layout

## Goal

Remove some UI issues with cursor selection, add statistics display, refine resizing

## Figma MAIN_WINDOW_LAYOUT.md

Formalizes basic outline of main window.

### Dot Display

This is where the student score histograms are plotted (like we do currently). The difference is that
the cursors aren't shown in draggable form. The grade regions are shown in alternating bands of
very very transparent gray/clear. Basically like you would subtlely differenciate rows in a clean
table.

This display should be outline faintly with a thin gray line.

### Grade Cursors

This is a plot whose x-axis with and range match the dot display **exactly**. This is where the cursors
are displayed and are draggable. The letter grades are displayed below the cursors. The border should be
this gray line.

### Statistics Cursors

This is a new plot with an x-axis matching the dot display **exactly**. I want to show the mean aggregate score along with as many std deviations above and below.
Also want to show the median but this needs to be displayed below the plot so it doesn't interfere with the mean.