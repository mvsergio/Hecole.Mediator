using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

/// <summary>
/// The guard is optional (off by default); these tests turn it on, except the default test. It tells the root
/// provider from a scope by comparing the provider injected into the mediator with the one injected into a singleton.
/// These tests confirm that this holds in Microsoft.Extensions.DependencyInjection 10, with and without scope
/// validation, for plain, nested and async scopes, and whichever is resolved first.
/// </summary>
public class RootProviderGuardTests
{
    private static ServiceProvider BuildRoot(bool validateScopes, Action<HecoleMediatorConfiguration>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg =>
        {
            cfg.RequireScope = true;
            configure?.Invoke(cfg);
        });
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validateScopes, ValidateOnBuild = validateScopes });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Default_GuardOff_SendFromRootProviderWorks(bool validateScopes)
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validateScopes, ValidateOnBuild = validateScopes });

        Assert.False(new HecoleMediatorConfiguration().RequireScope);
        Assert.Equal("Handled: r", (await root.GetRequiredService<ISender>().Send(new TestCommand("r"))).Value);
        Assert.Equal("Handled: o", Assert.IsType<TestResult>(await root.GetRequiredService<IMediator>().Send((object)new TestCommand("o"))).Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_FromRootProvider_ThrowsWithReason(bool validateScopes)
    {
        using var root = BuildRoot(validateScopes);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => root.GetRequiredService<ISender>().Send(new TestCommand("x")));

        Assert.Contains("root IServiceProvider", ex.Message);
        Assert.Contains("CreateScope()", ex.Message);
        Assert.Contains($"{nameof(HecoleMediatorConfiguration.RequireScope)} = false", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => root.GetRequiredService<IMediator>().Send((object)new TestCommand("x")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Send_FromScopes_Works(bool validateScopes)
    {
        using var root = BuildRoot(validateScopes);

        using var scope = root.CreateScope();
        Assert.Equal("Handled: a", (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("a"))).Value);

        using var nested = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        Assert.Equal("Handled: b", (await nested.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("b"))).Value);

        await using var asyncScope = root.CreateAsyncScope();
        Assert.Equal("Handled: c", (await asyncScope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("c"))).Value);
    }

    [Fact]
    public async Task Send_RootDetectedEvenWhenFirstResolvedInsideAScope()
    {
        using var root = BuildRoot(validateScopes: true);
        using (var scope = root.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("first"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => root.GetRequiredService<ISender>().Send(new TestCommand("x")));
    }

    [Fact]
    public async Task Send_ScopedMediatorFromRoot_Throws()
    {
        using var root = BuildRoot(validateScopes: false, cfg => cfg.Lifetime = ServiceLifetime.Scoped);

        await Assert.ThrowsAsync<InvalidOperationException>(() => root.GetRequiredService<ISender>().Send(new TestCommand("x")));
    }

    [Fact]
    public async Task Send_FromRootWithGuardOff_Works()
    {
        using var root = BuildRoot(validateScopes: true, cfg => cfg.RequireScope = false);

        Assert.Equal("Handled: r", (await root.GetRequiredService<ISender>().Send(new TestCommand("r"))).Value);
    }

    [Fact]
    public async Task Send_SingletonMediatorWithGuardOff_Works()
    {
        using var root = BuildRoot(validateScopes: true, cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Singleton;
            cfg.RequireScope = false;
        });
        using var scope = root.CreateScope();

        Assert.Equal("Handled: s", (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("s"))).Value);
    }

    // Notification handlers capture scoped services from the root just like request handlers: the guard covers Publish
    // too, in both forms, before any handler or publisher is resolved.
    [Fact]
    public async Task Publish_FromRoot_WithGuard_ThrowsWithReason()
    {
        using var root = BuildRoot(validateScopes: true);
        var publisher = root.GetRequiredService<IPublisher>();

        var typed = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.Publish(new TestNotification("n")));
        var boxed = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.Publish((object)new TestNotification("n")));

        Assert.Contains("root IServiceProvider", typed.Message);
        Assert.Contains("RequireScope = false", boxed.Message);
    }

    [Fact]
    public async Task Publish_FromScope_WithGuard_Works()
    {
        var handler = new TestNotificationHandler();
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RequireScope = true);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler);
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(new TestNotification("n"));

        Assert.Equal("n", Assert.Single(handler.ReceivedMessages));
    }

    [Fact]
    public async Task Publish_FromRoot_GuardOff_Works()
    {
        var handler = new TestNotificationHandler();
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddSingleton<INotificationHandler<TestNotification>>(handler);
        using var root = services.BuildServiceProvider();

        await root.GetRequiredService<IPublisher>().Publish(new TestNotification("n"));

        Assert.Equal("n", Assert.Single(handler.ReceivedMessages));
    }

    // (c) A singleton that received IServiceProvider holds the root: the guard fires there too, even when the
    // singleton was first resolved inside a scope.
    [Fact]
    public async Task Send_FromSingletonHoldingIServiceProvider_Throws()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RequireScope = true);
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddSingleton<SingletonConsumer>();
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();

        var consumer = scope.ServiceProvider.GetRequiredService<SingletonConsumer>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Services.GetRequiredService<ISender>().Send(new TestCommand("x")));
    }

    // (e) Scoped mediator resolved from a scope.
    [Fact]
    public async Task Send_ScopedMediatorFromScope_Works()
    {
        using var root = BuildRoot(validateScopes: true, cfg => cfg.Lifetime = ServiceLifetime.Scoped);
        using var scope = root.CreateScope();

        Assert.Equal("Handled: s", (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("s"))).Value);
    }

    // Option A (provider == IServiceScopeFactory) and option B (provider == provider of a singleton) give the same
    // answer in every resolution context of Microsoft.Extensions.DependencyInjection 10.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OptionAAndOptionB_AgreeInEveryContext(bool validateScopes)
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RequireScope = true);
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddTransient<ProviderProbe>();
        services.AddSingleton<SingletonConsumer>();
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validateScopes, ValidateOnBuild = validateScopes });
        using var scope = root.CreateScope();
        using var nested = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        await using var asyncScope = root.CreateAsyncScope();

        var contexts = new (string Name, IServiceProvider Provider, bool IsRoot)[]
        {
            ("BuildServiceProvider()", root, true),
            ("CreateScope()", scope.ServiceProvider, false),
            ("nested CreateScope()", nested.ServiceProvider, false),
            ("CreateAsyncScope()", asyncScope.ServiceProvider, false),
            ("singleton's IServiceProvider", scope.ServiceProvider.GetRequiredService<SingletonConsumer>().Services, true),
        };

        foreach (var (name, provider, isRoot) in contexts)
        {
            var injected = provider.GetRequiredService<ProviderProbe>().Services;
            var optionA = ReferenceEquals(injected, injected.GetService<IServiceScopeFactory>());
            var optionB = await ThrowsGuard(provider.GetRequiredService<ISender>());

            Assert.True(optionA == isRoot, $"A wrong for {name}");
            Assert.True(optionB == isRoot, $"B wrong for {name}");
        }
    }

    // What ValidateScopes does not catch: a disposable transient handler resolved from the root is tracked by the
    // root and only disposed when the application ends.
    [Fact]
    public async Task GuardOff_DisposableHandlerFromRoot_LivesUntilRootIsDisposed_AndValidateScopesDoesNotComplain()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<DisposableQuery, DisposableHandler, DisposableHandler>();
        var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var handlers = new List<DisposableHandler>();
        for (var i = 0; i < 3; i++)
            handlers.Add(await root.GetRequiredService<ISender>().Send(new DisposableQuery()));

        Assert.Equal(3, handlers.Distinct().Count());
        Assert.All(handlers, h => Assert.False(h.Disposed));
        await root.DisposeAsync();
        Assert.All(handlers, h => Assert.True(h.Disposed));
    }

    private static async Task<bool> ThrowsGuard(ISender sender)
    {
        try
        {
            await sender.Send(new TestCommand("probe"));
            return false;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("root IServiceProvider", StringComparison.Ordinal))
        {
            return true;
        }
    }

    private sealed class ProviderProbe(IServiceProvider services)
    {
        public IServiceProvider Services { get; } = services;
    }

    private sealed class SingletonConsumer(IServiceProvider services)
    {
        public IServiceProvider Services { get; } = services;
    }

    private sealed record DisposableQuery : IRequest<DisposableHandler>;

    private sealed class DisposableHandler : IRequestHandler<DisposableQuery, DisposableHandler>, IDisposable
    {
        public bool Disposed { get; private set; }

        public Task<DisposableHandler> Handle(DisposableQuery request, CancellationToken cancellationToken) => Task.FromResult(this);

        public void Dispose() => Disposed = true;
    }
}
