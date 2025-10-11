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
using Dotsesses.ViewModels;
using Dotsesses.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Dotsesses;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            // Handle snapshot mode - skip splash screen
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
                // Show splash screen
                var splashWindow = new SplashWindow();
                desktop.MainWindow = splashWindow;
                splashWindow.Show();

                // Initialize Python environment asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        // Update status
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            splashWindow.UpdateStatus("Configuring Python environment..."));

                        // Set up Python environment and services
                        var services = new ServiceCollection();
                        ConfigureServices(services);
                        Services = services.BuildServiceProvider();

                        await Dispatcher.UIThread.InvokeAsync(() =>
                            splashWindow.UpdateStatus("Loading main window..."));

                        // Small delay to show the status
                        await Task.Delay(500);

                        // Switch to main window on UI thread
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var mainWindow = new MainWindow
                            {
                                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
                            };

                            desktop.MainWindow = mainWindow;
                            mainWindow.Show();
                            splashWindow.Close();
                        });
                    }
                    catch (Exception ex)
                    {
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

    private void ConfigureServices(ServiceCollection services)
    {
        // Set up Python environment
        var pythonHome = Path.Combine(AppContext.BaseDirectory, "Python", "Violin");
        var venvPath = Path.Combine(pythonHome, ".venv");
        var pyprojectPath = Path.Combine(pythonHome, "pyproject.toml");

        services.WithPython()
            .WithHome(pythonHome)
            .FromRedistributable()
            .WithVirtualEnvironment(venvPath);

        // Register messenger
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // Register services
        services.AddSingleton<ViolinPlotService>();

        // Register ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<ViolinPlotViewModel>();
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