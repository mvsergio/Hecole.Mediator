using Hecole.Mediator.Internal;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

public class RegistrationTests
{
    // ---- Handler already registered directly in the container (D12) ----

    [Fact]
    public async Task HandlerInContainer_ThenAddRequestHandlerWithResponse_OneDescriptor_AndVerifyPasses()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddHecoleMediator(_ => { });

        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<TestCommand, TestResult>));

        using var root = services.BuildServiceProvider();
        root.VerifyHecoleMediator();
        using var scope = root.CreateScope();
        Assert.Equal("Handled: a", (await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("a"))).Value);
    }

    [Fact]
    public void HandlerInContainer_ThenAddRequestHandlerWithoutResponse_OneDescriptor_AndVerifyPasses()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddScoped<IRequestHandler<VoidCommand>, VoidCommandHandler>();
        services.AddRequestHandler<VoidCommand, VoidCommandHandler>();
        services.AddHecoleMediator(_ => { });

        // The registration already there is kept as it is, lifetime included.
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<VoidCommand>)).Lifetime);

        using var root = services.BuildServiceProvider();
        root.VerifyHecoleMediator();
    }

    [Fact]
    public void HandlerInContainerAsFactory_ThenAddRequestHandler_BothKept_AndVerifyReportsTwoHandlers()
    {
        // A factory registration has no implementation type to compare with: it is not the same registration.
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<TestCommand, TestResult>>(_ => new TestCommandHandler());
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddHecoleMediator(_ => { });

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IRequestHandler<TestCommand, TestResult>)));
        using var root = services.BuildServiceProvider();
        Assert.Throws<MediatorConfigurationException>(() => root.VerifyHecoleMediator());
    }

    [Fact]
    public void HandlerInContainer_ThenScan_OneDescriptor_AndVerifyPasses()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<Hecole.Mediator.Tests.ScanTarget.ScanQuery, string>, Hecole.Mediator.Tests.ScanTarget.ScanQueryHandler>();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<Hecole.Mediator.Tests.ScanTarget.ScanQuery>());

        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<Hecole.Mediator.Tests.ScanTarget.ScanQuery, string>));

        using var root = services.AddSingleton<Hecole.Mediator.Tests.ScanTarget.ScanRecorder>().BuildServiceProvider();
        root.VerifyHecoleMediator();
    }

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

    // ---- 2.0.1: registrations that could never be dispatched ---------------------------------------------------------

    [Fact]
    public void AddRequestHandler_WidenedResponse_ThrowsNamingRequestResponsesAndHandler()
    {
        var services = new ServiceCollection();

        // Compiles because IRequest<out TResponse> is covariant: WidenedQuery is an IRequest<object>.
        var ex = Assert.Throws<ArgumentException>(() => services.AddRequestHandler<WidenedQuery, object, WidenedQueryHandler>());

        Assert.Contains(typeof(WidenedQuery).FullName!, ex.Message);
        Assert.Contains(typeof(WidenedQueryHandler).FullName!, ex.Message);
        Assert.Contains("'System.String'", ex.Message);
        Assert.Contains("'System.Object'", ex.Message);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IRequestHandler<WidenedQuery, object>));
        Assert.Empty(HandlerRegistry.GetOrAdd(services).Registrations);
    }

    [Fact]
    public void AddRequestHandler_InterfaceRequest_Throws()
    {
        var services = new ServiceCollection();

        var withResponse = Assert.Throws<ArgumentException>(() => services.AddRequestHandler<IQueryContract, string, InterfaceQueryHandler>());
        var withoutResponse = Assert.Throws<ArgumentException>(() => services.AddRequestHandler<ICommandContract, InterfaceCommandHandler>());

        Assert.Contains(typeof(IQueryContract).FullName!, withResponse.Message);
        Assert.Contains("an interface", withResponse.Message);
        Assert.Contains(typeof(ICommandContract).FullName!, withoutResponse.Message);
        Assert.Equal([typeof(HandlerRegistry)], services.Select(d => d.ServiceType));
        Assert.Empty(HandlerRegistry.GetOrAdd(services).Registrations);
    }

    [Fact]
    public void AddRequestHandler_AbstractRequest_Throws()
    {
        var services = new ServiceCollection();

        var withResponse = Assert.Throws<ArgumentException>(() => services.AddRequestHandler<AbstractQuery, string, AbstractQueryHandler>());
        var withoutResponse = Assert.Throws<ArgumentException>(() => services.AddRequestHandler<AbstractCommand, AbstractCommandHandler>());

        Assert.Contains(typeof(AbstractQuery).FullName!, withResponse.Message);
        Assert.Contains("an abstract class", withResponse.Message);
        Assert.Contains(typeof(AbstractCommand).FullName!, withoutResponse.Message);
    }

    [Fact]
    public void AddRequestHandler_ExactResponseOfRequestOfUnit_IsAccepted()
    {
        var services = new ServiceCollection();
        services.AddRequestHandler<UnitQuery, Unit, UnitQueryHandler>();

        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<UnitQuery, Unit>));
    }

    [Fact]
    public void Scan_WidenedResponseHandler_Throws()
    {
        var assembly = EmittedHandlerAssembly.WithHandler(typeof(IRequestHandler<WidenedQuery, object>), "ScannedWidenedHandler");
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(assembly)));

        Assert.Contains(typeof(WidenedQuery).FullName!, ex.Message);
        Assert.Contains("ScannedWidenedHandler", ex.Message);
        Assert.Contains("'System.Object'", ex.Message);
    }

    [Theory]
    [InlineData(typeof(IRequestHandler<IQueryContract, string>), "an interface")]
    [InlineData(typeof(IRequestHandler<ICommandContract>), "an interface")]
    [InlineData(typeof(IRequestHandler<AbstractQuery, string>), "an abstract class")]
    [InlineData(typeof(IRequestHandler<AbstractCommand>), "an abstract class")]
    public void Scan_HandlerOfRequestThatCanNeverBeSent_Throws(Type handlerInterface, string reason)
    {
        var assembly = EmittedHandlerAssembly.WithHandler(handlerInterface);
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(assembly)));

        Assert.Contains(handlerInterface.GetGenericArguments()[0].FullName!, ex.Message);
        Assert.Contains(reason, ex.Message);
    }

    public sealed record WidenedQuery : IRequest<string>;

    public sealed class WidenedQueryHandler : IRequestHandler<WidenedQuery, object>
    {
        public Task<object> Handle(WidenedQuery request, CancellationToken cancellationToken) => Task.FromResult<object>("x");
    }

    public interface IQueryContract : IRequest<string>;

    public interface ICommandContract : IRequest;

    public sealed class InterfaceQueryHandler : IRequestHandler<IQueryContract, string>
    {
        public Task<string> Handle(IQueryContract request, CancellationToken cancellationToken) => Task.FromResult("x");
    }

    public sealed class InterfaceCommandHandler : IRequestHandler<ICommandContract>
    {
        public Task Handle(ICommandContract request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public abstract record AbstractQuery : IRequest<string>;

    public abstract record AbstractCommand : IRequest;

    public sealed class AbstractQueryHandler : IRequestHandler<AbstractQuery, string>
    {
        public Task<string> Handle(AbstractQuery request, CancellationToken cancellationToken) => Task.FromResult("x");
    }

    public sealed class AbstractCommandHandler : IRequestHandler<AbstractCommand>
    {
        public Task Handle(AbstractCommand request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed record UnitQuery : IRequest<Unit>;

    public sealed class UnitQueryHandler : IRequestHandler<UnitQuery, Unit>
    {
        public Task<Unit> Handle(UnitQuery request, CancellationToken cancellationToken) => Unit.Task;
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
