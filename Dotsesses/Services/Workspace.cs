namespace Dotsesses.Services;

using System;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// One loaded analysis's DI scope. Owns the scope's <see cref="IServiceProvider"/>
/// and disposes it when the owning window closes. Exposed services (IMessenger,
/// HoverDelayService, the per-analysis ViewModels) are scoped, so each Workspace
/// gets its own instances — messages and hover state cannot cross between
/// workspaces. See ADR-0012.
/// </summary>
public sealed class Workspace : IDisposable
{
    private readonly IServiceScope _scope;
    private bool _disposed;

    public IServiceProvider Services => _scope.ServiceProvider;

    public Workspace(IServiceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scope.Dispose();
    }
}
