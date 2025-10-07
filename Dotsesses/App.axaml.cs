using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using Dotsesses.ViewModels;
using Dotsesses.Views;

namespace Dotsesses;

public partial class App : Application
{
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

            var mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            desktop.MainWindow = mainWindow;

            // Handle snapshot mode
            if (StartupConfig.SnapshotMode)
            {
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
        }

        base.OnFrameworkInitializationCompleted();
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