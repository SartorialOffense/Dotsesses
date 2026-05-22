namespace Dotsesses.Services;

using System;
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
}
