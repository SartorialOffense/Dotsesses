# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Dotsesses is an Avalonia UI desktop application built on .NET 9.0. It uses the MVVM pattern with CommunityToolkit.Mvvm for observable properties and commands, and integrates OxyPlot for data visualization.

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

- `Program.cs` - Application entry point and Avalonia configuration
- `App.axaml` / `App.axaml.cs` - Application-level resources and initialization
- `ViewLocator.cs` - Convention-based View resolution for MVVM
- `ViewModels/` - ViewModels inheriting from ViewModelBase
- `Views/` - Avalonia UserControls and Windows (XAML + code-behind)
- `Models/` - Data models (currently empty)
- `Assets/` - Application resources (icons, images, etc.)

## Rules of the road

- **NEVER** commit code unless I explicitly tell you to do this.

- Use clean markdown, follow best practices for formatting, spacing, and line lengths.
Check after edits to files.

- When I ask you to make a questionnaire for me, do this in the .conversations/ folder. this folder
is in .gitignore so any record will need be in a summary file in design_history/ later on.