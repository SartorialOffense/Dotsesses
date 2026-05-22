using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CSnakes.Runtime;
using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Services;
using Dotsesses.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Dotsesses;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "dotsesses_startup.log");

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    public override void Initialize()
    {
        Log("App.Initialize() called");
        AvaloniaXamlLoader.Load(this);
        Log("App.Initialize() completed");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Log("=== APPLICATION STARTUP BEGIN ===");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log("Desktop lifetime detected");

            // Multi-window: app stays alive as long as any window is open.
            // Default OnMainWindowClose would exit when the first-opened
            // window closes, killing sibling analyses. See ADR-0012.
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            Log("Data validation disabled");

            // Handle snapshot mode - skip splash screen
            Log($"Snapshot mode: {StartupConfig.SnapshotMode}");
            if (StartupConfig.SnapshotMode)
            {
                // Set up Python environment and services synchronously for snapshot mode
                var services = new ServiceCollection();
                ConfigureServices(services);
                Services = services.BuildServiceProvider();

                var snapshotWorkspace = Services.GetRequiredService<WorkspaceFactory>().Create();
                var viewModel = snapshotWorkspace.Services.GetRequiredService<MainWindowViewModel>();

                // For snapshot mode, require source file path via StartupConfig
                if (string.IsNullOrEmpty(StartupConfig.SourceFilePath))
                {
                    Console.Error.WriteLine("Error: Source file path required for snapshot mode. Use --source <path>");
                    desktop.Shutdown(1);
                    return;
                }

                viewModel.LoadFromExcelFile(StartupConfig.SourceFilePath);

                _mainWindow = new MainWindow
                {
                    DataContext = viewModel,
                };

                desktop.MainWindow = _mainWindow;
                _mainWindow.Closed += (_, _) => snapshotWorkspace.Dispose();

                _mainWindow.Opened += async (s, e) =>
                {
                    try
                    {
                        var snapshotPath = await _mainWindow.SaveSnapshotAsync(StartupConfig.SnapshotOutputPath);
                        Console.WriteLine($"Snapshot saved to: {snapshotPath}");
                        desktop.Shutdown(0);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error capturing snapshot: {ex.Message}");
                        desktop.Shutdown(1);
                    }
                };
            }
            else
            {
                Log("Startup: Showing splash screen");

                // Show splash screen
                var splashWindow = new SplashWindow();
                desktop.MainWindow = splashWindow;
                splashWindow.Show();

                Log("Startup: Splash screen visible");

                // Initialize Python environment and services asynchronously with progress reporting
                Task.Run(async () =>
                {
                    try
                    {
                        Log("Startup: Background task started");

                        // Set up services with progress callbacks
                        var services = new ServiceCollection();

                        // Check if this is first-time Python setup
                        var pythonHome = Path.Combine(AppContext.BaseDirectory, "Python", "Violin");
                        var venvPath = Path.Combine(pythonHome, ".venv");
                        bool isFirstTimeSetup = !Directory.Exists(venvPath);
                        Log($"Startup: Checking for .venv at: {venvPath}");
                        Log($"Startup: .venv exists: {Directory.Exists(venvPath)}");
                        Log($"Startup: First-time Python setup: {isFirstTimeSetup}");

                        if (isFirstTimeSetup)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                                splashWindow.UpdateStatus("Installing Python VENV: scipy, seaborn, matplotlib, pandas, numpy, etc..."));
                            await Task.Delay(100); // Brief pause to ensure message is visible
                        }

                        await Task.Run(() =>
                        {
                            ConfigureServices(services, message =>
                            {
                                Log($"Progress: {message}");
                                // For first-time setup, show a simplified message during the long setup process
                                if (isFirstTimeSetup && message.Contains("Installing Python dependencies"))
                                {
                                    // This step takes the longest during first-time setup, keep the special message
                                    Dispatcher.UIThread.InvokeAsync(() =>
                                        splashWindow.UpdateStatus("Creating Python environment for first time use...")).Wait();
                                }
                                else if (!isFirstTimeSetup)
                                {
                                    // Show detailed progress for subsequent runs (fast)
                                    Dispatcher.UIThread.InvokeAsync(() => splashWindow.UpdateStatus(message)).Wait();
                                }
                            });
                        });

                        // Build service provider (this is where Python environment is actually initialized)
                        Log("Startup: Building service provider...");
                        await Dispatcher.UIThread.InvokeAsync(() => splashWindow.UpdateStatus("Initializing Python runtime..."));

                        Services = await Task.Run(() => services.BuildServiceProvider());
                        Log("Startup: Service provider built");

                        // Warm up ViolinPlotService on background thread to initialize Python interop
                        // During first-time setup, this is where the actual Python environment creation happens (can take 30-60 seconds)
                        if (isFirstTimeSetup)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                                splashWindow.UpdateStatus("Installing Python .venv (scipy, seaborn, matplotlib, pandas, numpy, etc.)"));
                        }
                        else
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                                splashWindow.UpdateStatus("Initializing Python interop..."));
                        }

                        Log("Startup: Warming up plot services");
                        await Task.Run(() =>
                        {
                            // Force plot services to be created, which initializes Python interop
                            var _ = Services.GetRequiredService<ViolinPlotService>();
                            Log("Startup: ViolinPlotService initialized");
                            var __ = Services.GetRequiredService<CorrelationPlotService>();
                            Log("Startup: CorrelationPlotService initialized");
                        });

                        await Dispatcher.UIThread.InvokeAsync(() => splashWindow.UpdateStatus("Select source file..."));

                        // Small delay to show the status
                        await Task.Delay(200);

                        Log("Startup: Prompting for source file");

                        // Prompt for source file on UI thread
                        string? sourceFilePath = null;
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            sourceFilePath = await PromptForSourceFile(splashWindow);
                        });

                        if (string.IsNullOrEmpty(sourceFilePath))
                        {
                            Log("Startup: No file selected, shutting down");
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                splashWindow.Close();
                                desktop.Shutdown();
                            });
                            return;
                        }

                        Log($"Startup: Source file selected: {sourceFilePath}");
                        await Dispatcher.UIThread.InvokeAsync(() => splashWindow.UpdateStatus("Creating main window..."));

                        // Switch to main window on UI thread
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            Log("Startup: Instantiating MainWindow");
                            // Build a per-window DI scope via WorkspaceFactory (ADR-0012).
                            // The scope lives as long as the window — held on _initialWorkspace
                            // so a future slice can dispose it on close.
                            _initialWorkspace = Services.GetRequiredService<WorkspaceFactory>().Create();
                            var viewModel = _initialWorkspace.Services.GetRequiredService<MainWindowViewModel>();

                            // Load based on file extension
                            var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
                            if (extension == ".dots")
                            {
                                Log("Startup: Loading .dots file");
                                await viewModel.LoadStateCommand.ExecuteAsync(sourceFilePath);
                            }
                            else
                            {
                                Log("Startup: Loading Excel file");
                                viewModel.LoadFromExcelFile(sourceFilePath);
                            }

                            _mainWindow = new MainWindow
                            {
                                DataContext = viewModel,
                            };

                            // Capture the workspace in a local so the Closed
                            // handler keeps it alive and disposes it
                            // deterministically when the window closes.
                            var workspaceForClose = _initialWorkspace!;
                            _mainWindow.Closed += (_, _) => workspaceForClose.Dispose();

                            Log("Startup: Showing MainWindow");
                            desktop.MainWindow = _mainWindow;
                            _mainWindow.Show();
                            splashWindow.Close();
                            Log("Startup: MainWindow visible, splash closed");
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"ERROR: {ex.Message}");
                        Log(ex.StackTrace ?? "No stack trace");
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            splashWindow.UpdateStatus($"Error: {ex.Message}"));
                        await Task.Delay(3000);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            desktop.Shutdown());
                    }
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Prompts the user to select an Excel or Dotsesses (.dots) file.
    /// </summary>
    /// <param name="parentWindow">The parent window for the file dialog</param>
    /// <returns>The selected file path, or null if cancelled</returns>
    private static async Task<string?> PromptForSourceFile(Window parentWindow)
    {
        var storageProvider = parentWindow.StorageProvider;

        var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Source File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Supported files") { Patterns = new[] { "*.xlsx", "*.xls", "*.dots" } },
                new FilePickerFileType("Excel files") { Patterns = new[] { "*.xlsx", "*.xls" } },
                new FilePickerFileType("Dotsesses files") { Patterns = new[] { "*.dots" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });

        if (result.Count > 0)
        {
            return result[0].Path.LocalPath;
        }

        return null;
    }

    /// <summary>
    /// Configures services with optional progress reporting.
    /// </summary>
    /// <param name="services">Service collection to configure</param>
    /// <param name="progressCallback">Optional callback to report progress</param>
    private static void ConfigureServices(ServiceCollection services, Action<string>? progressCallback = null)
    {
        // Set up Python environment with progress reporting
        Log("ConfigureServices: Starting");

        progressCallback?.Invoke("Locating Python home directory...");
        Log("ConfigureServices: Locating Python home directory");
        var pythonHome = Path.Combine(AppContext.BaseDirectory, "Python", "Violin");
        var venvPath = Path.Combine(pythonHome, ".venv");
        var pyprojectPath = Path.Combine(pythonHome, "pyproject.toml");

        progressCallback?.Invoke("Configuring Python redistributable...");
        Log("ConfigureServices: Configuring Python redistributable");
        var pythonBuilder = services.WithPython()
            .WithHome(pythonHome)
            .FromRedistributable();

        progressCallback?.Invoke("Setting up virtual environment...");
        Log("ConfigureServices: Setting up virtual environment");
        pythonBuilder = pythonBuilder.WithVirtualEnvironment(venvPath);

        progressCallback?.Invoke("Installing Python dependencies...");
        Log("ConfigureServices: Installing Python dependencies");
        pythonBuilder.WithUvInstaller(pyprojectPath);

        progressCallback?.Invoke("Registering application services...");
        Log("ConfigureServices: Registering application services");

        // Per-workspace messenger: one instance per scope, never the static
        // .Default. See ADR-0012 for why scoping is required for multi-window.
        services.AddScoped<IMessenger>(_ => new WeakReferenceMessenger());

        // Stateless Python-interop renderers — shared across all workspaces.
        services.AddSingleton<ViolinPlotService>();
        services.AddSingleton<CorrelationPlotService>();

        // Scoped: one per workspace.
        services.AddScoped<HoverDelayService>();
        services.AddScoped<StateService>();

        // Workspace factory — wraps IServiceScopeFactory to produce per-window
        // scopes. App-singleton so each Open Another call resolves the same
        // factory instance.
        services.AddSingleton<WorkspaceFactory>();

        // Per-workspace ViewModels.
        services.AddScoped<MainWindowViewModel>();
        services.AddScoped<ViolinPlotViewModel>();
        services.AddScoped<CorrelationPlotViewModel>();

        Log("ConfigureServices: Completed");
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private MainWindow? _mainWindow;
    private Workspace? _initialWorkspace;

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_mainWindow != null)
        {
            await _mainWindow.TriggerSave();
        }
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        if (_mainWindow != null)
        {
            await _mainWindow.TriggerOpenSettings();
        }
    }
}