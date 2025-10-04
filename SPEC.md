# Dotsesses Specification

## Overview

Dotsesses is an Avalonia UI desktop application for data visualization and analysis.

## Technical Stack

- **Framework**: .NET 9.0
- **UI Framework**: Avalonia 11.3.6
- **MVVM Toolkit**: CommunityToolkit.Mvvm 8.2.1
- **Plotting Library**: OxyPlot.Avalonia 2.1.0-Avalonia11
- **Theme**: Dark theme variant

## Architecture

### MVVM Pattern
- ViewModels inherit from `ViewModelBase` (extends `ObservableObject`)
- Convention-based View resolution via `ViewLocator`
- Compiled bindings enabled by default

### Current Features

#### Scatter Plot Visualization
- PlotModel with configurable data series
- Dark theme with black background and white text
- Linear axes with customizable titles and colors
- Scatter series with cyan markers for visibility
- Sample data: 8 data points demonstrating basic plotting

## Project Structure

```
Dotsesses/
├── ViewModels/       # MVVM ViewModels
├── Views/            # Avalonia UserControls and Windows
├── Models/           # Data models (currently empty)
└── Assets/           # Application resources
```

## Future Enhancements

_To be documented as features are developed_
