using Avalonia;
using System;

namespace Dotsesses;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ========================================");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] DOTSESSES STARTING");
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ========================================");

        // Parse command-line arguments
        StartupConfig.ParseArguments(args);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Arguments parsed");

        // Build and start the app
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Building Avalonia app...");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Application exited");
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}