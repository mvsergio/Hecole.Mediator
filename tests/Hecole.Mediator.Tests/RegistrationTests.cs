using Hecole.Mediator.Internal;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

public class RegistrationTests
{
    [Fact]
    public void AddHecoleMediator_DefaultLifetime_IsTransientForMediatorSenderAndPublisher()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });

        Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == typeof(IMediator)).Lifetime);
        Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == typeof(ISender)).Lifetime);
        Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == typeof(IPublisher)).Lifetime);
    }

    [Fact]
    public void AddHecoleMediator_ScopedLifetime_SenderAndPublisherAreTheScopeMediator()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.Lifetime = ServiceLifetime.Scoped);

        Assert.All(
            services.Where(d => d.ServiceType == typeof(IMediator) || d.ServiceType == typeof(ISender) || d.ServiceType == typeof(IPublisher)),
            d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));

        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        Assert.Same(mediator, scope.ServiceProvider.GetRequiredService<ISender>());
        Assert.Same(mediator, scope.ServiceProvider.GetRequiredService<IPublisher>());
    }

    [Fact]
    public void AddHecoleMediator_SingletonWithRequireScope_Throws()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddHecoleMediator(cfg =>
        {
            cfg.Lifetime = ServiceLifetime.Singleton;
            cfg.RequireScope = true;
        }));

        Assert.Contains(nameof(HecoleMediatorConfiguration.RequireScope), ex.Message);
    }

    [Fact]
    public void AddHecoleMediator_SingletonWithGuardOff_IsAccepted()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.Lifetime = ServiceLifetime.Singleton);

        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == typeof(IMediator)).Lifetime);
    }

    [Fact]
    public void AddHecoleMediator_Twice_RegistersTheMediatorOnce()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddHecoleMediator(_ => { });

        Assert.Single(services, d => d.ServiceType == typeof(IMediator));
        Assert.Single(services, d => d.ServiceType == typeof(MediatorRuntime));
    }

    [Fact]
    public void AddHecoleMediator_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddHecoleMediator(null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddHecoleMediator(_ => { }));
    }

    [Fact]
    public void AddRequestHandler_RegistersHandlerAsTransient()
    {
        var services = new ServiceCollection();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddRequestHandler<VoidCommand, VoidCommandHandler>();

        var withResponse = Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<TestCommand, TestResult>));
        Assert.Equal(typeof(TestCommandHandler), withResponse.ImplementationType);
        Assert.Equal(ServiceLifetime.Transient, withResponse.Lifetime);

        var withoutResponse = Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<VoidCommand>));
        Assert.Equal(typeof(VoidCommandHandler), withoutResponse.ImplementationType);
        Assert.Equal(ServiceLifetime.Transient, withoutResponse.Lifetime);
    }

    [Fact]
    public void AddRequestHandler_SecondHandlerForSameRequest_ThrowsWithBothNames()
    {
        var services = new ServiceCollection();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddRequestHandler<TestCommand, TestResult, CancellationAwareHandler>());

        Assert.Contains(typeof(TestCommandHandler).FullName!, ex.Message);
        Assert.Contains(typeof(CancellationAwareHandler).FullName!, ex.Message);
        Assert.Contains(typeof(TestCommand).FullName!, ex.Message);
        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<TestCommand, TestResult>));
    }

    [Fact]
    public void AddRequestHandler_SecondDifferentHandlerForSameRequestWithoutResponse_Throws()
    {
        var services = new ServiceCollection();
        services.AddRequestHandler<VoidCommand, VoidCommandHandler>();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddRequestHandler<VoidCommand, OtherVoidCommandHandler>());

        Assert.Contains(typeof(VoidCommandHandler).FullName!, ex.Message);
        Assert.Contains(typeof(OtherVoidCommandHandler).FullName!, ex.Message);
    }

    [Fact]
    public void AddRequestHandler_SameHandlerTwice_IsRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddRequestHandler<VoidCommand, VoidCommandHandler>();
        services.AddRequestHandler<VoidCommand, VoidCommandHandler>();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();

        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<VoidCommand>));
        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<TestCommand, TestResult>));
    }

    [Fact]
    public void AddRequestHandler_RequestWithBothRequestInterfaces_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddRequestHandler<AmbiguousRequest, TestResult, AmbiguousRequestHandler>());
    }

    [Fact]
    public void AddRequestHandler_BeforeOrAfterAddHecoleMediator_BothEnterTheCatalog()
    {
        var services = new ServiceCollection();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<TestQuery, TestResult, TestQueryHandler>();

        using var root = services.BuildServiceProvider();
        var catalog = root.GetRequiredService<MediatorRuntime>().Catalog;

        Assert.Equal(2, catalog.Count);
        Assert.Single(services, d => d.ServiceType == typeof(HandlerRegistry));
    }

    [Fact]
    public void Catalog_IsBuiltOnceAsSingleton()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>());

        using var other = host.Root.CreateScope();
        Assert.Same(host.GetRequiredService<MediatorRuntime>().Catalog, other.ServiceProvider.GetRequiredService<MediatorRuntime>().Catalog);
    }

    private sealed class OtherVoidCommandHandler : IRequestHandler<VoidCommand>
    {
        public Task Handle(VoidCommand request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AmbiguousRequestHandler : IRequestHandler<AmbiguousRequest, TestResult>
    {
        public Task<TestResult> Handle(AmbiguousRequest request, CancellationToken cancellationToken) => Task.FromResult(new TestResult("x"));
    }
}
