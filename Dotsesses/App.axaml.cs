using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
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

                var mainWindow = new MainWindow
                {
                    DataContext = Services.GetRequiredService<MainWindowViewModel>(),
                };

                desktop.MainWindow = mainWindow;

                mainWindow.Opened += async (s, e) =>
                {
                    try
                    {
                        var snapshotPath = await mainWindow.SaveSnapshotAsync(StartupConfig.SnapshotOutputPath);
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

                        Log("Startup: Warming up ViolinPlotService");
                        await Task.Run(() =>
                        {
                            // Force ViolinPlotService to be created, which initializes Python interop
                            var _ = Services.GetRequiredService<ViolinPlotService>();
                            Log("Startup: ViolinPlotService initialized");
                        });

                        await Dispatcher.UIThread.InvokeAsync(() => splashWindow.UpdateStatus("Creating main window..."));

                        // Small delay to show the status
                        await Task.Delay(200);

                        Log("Startup: Creating MainWindow");

                        // Switch to main window on UI thread
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            Log("Startup: Instantiating MainWindow");
                            var mainWindow = new MainWindow
                            {
                                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
                            };

                            Log("Startup: Showing MainWindow");
                            desktop.MainWindow = mainWindow;
                            mainWindow.Show();
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

        // Register messenger
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // Register services
        services.AddSingleton<ViolinPlotService>();

        // Register ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ViolinPlotViewModel>();

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
}