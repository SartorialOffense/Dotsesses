namespace Dotsesses.Tests.Services;

using CommunityToolkit.Mvvm.Messaging;
using Dotsesses.Messages;
using Dotsesses.Services;
using Microsoft.Extensions.DependencyInjection;

public class WorkspaceFactoryTests
{
    private static WorkspaceFactory BuildFactoryWithMinimalRegistrations()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMessenger>(_ => new WeakReferenceMessenger());
        services.AddScoped<HoverDelayService>();
        services.AddSingleton<WorkspaceFactory>();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<WorkspaceFactory>();
    }

    [Fact]
    public void TwoWorkspaces_HaveDistinctMessengers()
    {
        var factory = BuildFactoryWithMinimalRegistrations();
        using var a = factory.Create();
        using var b = factory.Create();

        var m1 = a.Services.GetRequiredService<IMessenger>();
        var m2 = b.Services.GetRequiredService<IMessenger>();

        Assert.NotSame(m1, m2);
    }

    [Fact]
    public void TwoWorkspaces_HaveDistinctHoverDelayServices()
    {
        var factory = BuildFactoryWithMinimalRegistrations();
        using var a = factory.Create();
        using var b = factory.Create();

        var h1 = a.Services.GetRequiredService<HoverDelayService>();
        var h2 = b.Services.GetRequiredService<HoverDelayService>();

        Assert.NotSame(h1, h2);
    }

    [Fact]
    public void MessageSentInOneWorkspace_DoesNotReachAnother()
    {
        var factory = BuildFactoryWithMinimalRegistrations();
        using var a = factory.Create();
        using var b = factory.Create();

        var m1 = a.Services.GetRequiredService<IMessenger>();
        var m2 = b.Services.GetRequiredService<IMessenger>();

        var receiver = new object();
        bool received = false;
        m2.Register<StudentEditedMessage>(receiver, (_, _) => received = true);

        m1.Send(new StudentEditedMessage(42));

        Assert.False(received);
        GC.KeepAlive(receiver);
    }

    [Fact]
    public void DisposingOneWorkspace_DoesNotAffectAnother()
    {
        var factory = BuildFactoryWithMinimalRegistrations();
        var a = factory.Create();
        using var b = factory.Create();

        var m2 = b.Services.GetRequiredService<IMessenger>();
        a.Dispose();

        var receiver = new object();
        bool received = false;
        m2.Register<StudentEditedMessage>(receiver, (_, _) => received = true);
        m2.Send(new StudentEditedMessage(42));

        Assert.True(received);
        GC.KeepAlive(receiver);
    }
}
