using System;
using System.IO;
using CSnakes.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Dotsesses.Tests.Python;

/// <summary>
/// Stands up a real CSnakes <see cref="IPythonEnvironment"/> once for the whole
/// test run, pointed at the in-repo <c>Dotsesses/Python/Violin</c> directory
/// (which holds both the plot modules and the committed <c>.venv</c>). This is
/// the bridge that lets the C# test suite exercise the Python statistics logic
/// directly (ADR-0018 test strategy: drive Python via CSnakes from xUnit rather
/// than maintaining a separate pytest harness).
///
/// Initialising the Python runtime is expensive, so this is shared across every
/// Python-interop test via <see cref="PythonCollection"/>.
/// </summary>
public sealed class PythonEnvironmentFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    public IPythonEnvironment Env { get; }

    public PythonEnvironmentFixture()
    {
        var pythonHome = LocatePythonHome();
        var venvPath = Path.Combine(pythonHome, ".venv");
        var pyprojectPath = Path.Combine(pythonHome, "pyproject.toml");

        var services = new ServiceCollection();
        services.WithPython()
            .WithHome(pythonHome)
            .FromRedistributable()
            .WithVirtualEnvironment(venvPath)
            .WithUvInstaller(pyprojectPath);

        _provider = services.BuildServiceProvider();
        Env = _provider.GetRequiredService<IPythonEnvironment>();
    }

    /// <summary>
    /// Walks up from the test assembly's location until it finds the repo's
    /// <c>Dotsesses/Python/Violin</c> directory. Keeps the fixture working
    /// regardless of the test runner's current directory.
    /// </summary>
    private static string LocatePythonHome()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Dotsesses", "Python", "Violin");
            if (File.Exists(Path.Combine(candidate, "pyproject.toml")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate Dotsesses/Python/Violin (with pyproject.toml) " +
            $"walking up from {AppContext.BaseDirectory}.");
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// xUnit collection so the single <see cref="PythonEnvironmentFixture"/> is
/// shared by every Python-interop test class (one runtime per test run).
/// </summary>
[CollectionDefinition(Name)]
public sealed class PythonCollection : ICollectionFixture<PythonEnvironmentFixture>
{
    public const string Name = "Python interop";
}
