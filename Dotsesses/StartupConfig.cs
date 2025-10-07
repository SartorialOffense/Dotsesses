namespace Dotsesses;

/// <summary>
/// Configuration parsed from command-line arguments.
/// </summary>
public static class StartupConfig
{
    /// <summary>
    /// If true, the app should capture a snapshot and exit.
    /// </summary>
    public static bool SnapshotMode { get; set; }

    /// <summary>
    /// Optional output path for snapshot. If null, uses temp folder.
    /// </summary>
    public static string? SnapshotOutputPath { get; set; }

    /// <summary>
    /// Parses command-line arguments and populates configuration.
    /// </summary>
    public static void ParseArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--snapshot":
                case "--capture-snapshot":
                    SnapshotMode = true;
                    break;

                case "--output":
                case "-o":
                    if (i + 1 < args.Length)
                    {
                        SnapshotOutputPath = args[i + 1];
                        i++; // Skip next arg
                    }
                    break;
            }
        }
    }
}
