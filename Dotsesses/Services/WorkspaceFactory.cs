namespace Dotsesses.Services;

using System;
using System.IO;
using System.Threading.Tasks;
using Dotsesses.UI;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Produces a fresh <see cref="Workspace"/> on demand, each backed by its
/// own DI scope. App-singleton — the factory itself outlives any individual
/// workspace. See ADR-0012.
/// </summary>
public sealed class WorkspaceFactory
{
    private readonly IServiceScopeFactory _scopes;

    public WorkspaceFactory(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        _scopes = scopes;
    }

    public Workspace Create() => new(_scopes.CreateScope());

    /// <summary>
    /// Creates a workspace, resolves its <see cref="MainWindowViewModel"/>,
    /// and loads <paramref name="filePath"/> into it (xlsx via
    /// <c>LoadFromExcelFile</c>, .dots via <c>LoadStateCommand</c>). The
    /// returned workspace exposes the loaded VM through
    /// <see cref="Workspace.Services"/>; the caller is responsible for
    /// constructing and showing the Window.
    /// </summary>
    public async Task<Workspace> CreateForFileAsync(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var workspace = Create();
        var vm = workspace.Services.GetRequiredService<MainWindowViewModel>();

        if (Path.GetExtension(filePath).Equals(".dots", StringComparison.OrdinalIgnoreCase))
        {
            await vm.LoadStateCommand.ExecuteAsync(filePath);
        }
        else
        {
            vm.LoadFromExcelFile(filePath);
        }

        return workspace;
    }
}
