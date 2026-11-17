using Hecole.Mediator.Internal;
using Hecole.Mediator.NotificationPublishers;
using Hecole.Mediator.Tests.Fakes;
using Hecole.Mediator.Tests.ScanTarget;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hecole.Mediator.Tests;

/// <summary>
/// <see cref="IRequestCatalog"/> and <c>VerifyHecoleMediator</c> (lote 5, item 9 of the 1.3.0 analysis).
/// </summary>
public class VerificationTests
{
    private static readonly System.Reflection.Assembly ScanAssembly = typeof(ScanQuery).Assembly;

    private static ServiceProvider Build(Action<IServiceCollection> configure, Action<HecoleMediatorConfiguration>? mediator = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ScanRecorder>();
        services.AddHecoleMediator(mediator ?? (_ => { }));
        configure(services);
        return services.BuildServiceProvider();
    }

    private static MediatorConfigurationException VerifyFails(IServiceProvider provider, params System.Reflection.Assembly[] assemblies)
        => Assert.Throws<MediatorConfigurationException>(() => provider.VerifyHecoleMediator(assemblies));

    // ---- IRequestCatalog --------------------------------------------------------------------------------------------

    [Fact]
    public void Catalog_ListsRequestsOfScanAndAddRequestHandler_InRegistrationOrder()
    {
        using var provider = Build(s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            s.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(ScanAssembly));
            s.AddRequestHandler<VoidCommand, VoidCommandHandler>();
            s.AddTransient<IRequestHandler<TestQuery, TestResult>, TestQueryHandler>();
        });

        var catalog = provider.GetRequiredService<IRequestCatalog>();

        Assert.Equal(
            [
                new RequestDescriptor(typeof(TestCommand), typeof(TestResult), typeof(TestCommandHandler)),
                new RequestDescriptor(typeof(ScanQuery), typeof(string), typeof(ScanQueryHandler)),
                new RequestDescriptor(typeof(ScanCommand), typeof(Unit), ScanCommandHandlerType),
                new RequestDescriptor(typeof(VoidCommand), typeof(Unit), typeof(VoidCommandHandler)),
            ],
            catalog.Requests);
        Assert.Same(catalog, provider.CreateScope().ServiceProvider.GetRequiredService<IRequestCatalog>());
        Assert.Throws<NotSupportedException>(() => ((IList<RequestDescriptor>)catalog.Requests).Add(catalog.Requests[0]));
    }

    private static Type ScanCommandHandlerType => ScanAssembly.GetType("Hecole.Mediator.Tests.ScanTarget.ScanCommandHandler", throwOnError: true)!;

    // ---- Correct configuration ---------------------------------------------------------------------------------------

    [Fact]
    public void CorrectConfiguration_Passes()
    {
        using var provider = Build(
            s =>
            {
                s.AddRequestHandler<ScanSkippedQuery, string, ScanSkippedQueryHandler>();
                s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
                s.AddNotificationHandler<TestNotification, TestNotificationHandler>();
            },
            cfg => cfg.RegisterServicesFromAssembly(ScanAssembly).AddOpenBehavior(typeof(OpenGenericTestBehavior<,>)));

        provider.VerifyHecoleMediator(ScanAssembly, ScanAssembly);
        provider.VerifyHecoleMediator();
    }

    [Fact]
    public void RequestsOfAssemblyWithHandlersOnlyInContainer_Pass()
    {
        using var provider = Build(s =>
        {
            s.AddTransient<IRequestHandler<ScanQuery, string>, ScanQueryHandler>();
            s.AddTransient(typeof(IRequestHandler<ScanCommand>), ScanCommandHandlerType);
            s.AddTransient<IRequestHandler<ScanSkippedQuery, string>, ScanSkippedQueryHandler>();
        });

        provider.VerifyHecoleMediator(ScanAssembly);
    }

    [Fact]
    public void Verification_RunsOnlyWhenCalled()
    {
        // No handler for anything in the assembly: building and resolving work; only the verification reports it.
        using var provider = Build(_ => { });

        Assert.NotNull(provider.CreateScope().ServiceProvider.GetRequiredService<IMediator>());
        VerifyFails(provider, ScanAssembly);
    }

    // ---- (a) Requests of the catalog: exactly one handler --------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HandlerInContainerConflictingWithAddRequestHandler_IsReportedWithBothNames(bool directFirst)
    {
        using var provider = Build(s =>
        {
            if (directFirst)
                s.AddTransient<IRequestHandler<TestCommand, TestResult>, CancellationAwareHandler>();
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            if (!directFirst)
                s.AddTransient<IRequestHandler<TestCommand, TestResult>, CancellationAwareHandler>();
        });

        var ex = VerifyFails(provider);

        var problem = Assert.Single(ex.Problems);
        Assert.Contains(typeof(TestCommand).FullName!, problem);
        Assert.Contains(typeof(TestCommandHandler).FullName!, problem);
        Assert.Contains(typeof(CancellationAwareHandler).FullName!, problem);
        Assert.Contains("2 handlers", problem);
    }

    [Fact]
    public void HandlerInContainerConflictingWithScan_IsReported()
    {
        using var provider = Build(
            s => s.AddTransient<IRequestHandler<ScanQuery, string>, OtherScanQueryHandler>(),
            cfg => cfg.RegisterServicesFromAssembly(ScanAssembly));

        var ex = VerifyFails(provider);

        var problem = Assert.Single(ex.Problems);
        Assert.Contains(typeof(ScanQueryHandler).FullName!, problem);
        Assert.Contains(typeof(OtherScanQueryHandler).FullName!, problem);
    }

    [Fact]
    public void RegisteredHandlerRemovedFromContainer_IsReported()
    {
        using var provider = Build(s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            s.RemoveAll<IRequestHandler<TestCommand, TestResult>>();
        });

        var problem = Assert.Single(VerifyFails(provider).Problems);

        Assert.Contains(typeof(TestCommandHandler).FullName!, problem);
        Assert.Contains("no longer in the container", problem);
    }

    // ---- (b) Request types of the assemblies given ----------------------------------------------------------------------

    [Fact]
    public void RequestOfAssemblyWithoutHandler_IsReported_OpenGenericAndAbstractAreSkipped()
    {
        using var provider = Build(_ => { }, cfg => cfg.RegisterServicesFromAssembly(ScanAssembly));

        var problem = Assert.Single(VerifyFails(provider, ScanAssembly).Problems);

        Assert.Contains($"'{typeof(ScanSkippedQuery).FullName}' has no handler", problem);
    }

    [Fact]
    public void RequestOfAssemblyWithTwoHandlersInContainer_IsReported()
    {
        using var provider = Build(s =>
        {
            s.AddTransient<IRequestHandler<ScanQuery, string>, ScanQueryHandler>();
            s.AddTransient<IRequestHandler<ScanQuery, string>, OtherScanQueryHandler>();
            s.AddTransient(typeof(IRequestHandler<ScanCommand>), ScanCommandHandlerType);
            s.AddTransient<IRequestHandler<ScanSkippedQuery, string>, ScanSkippedQueryHandler>();
        });

        var problem = Assert.Single(VerifyFails(provider, ScanAssembly).Problems);

        Assert.Contains(typeof(ScanQueryHandler).FullName!, problem);
        Assert.Contains(typeof(OtherScanQueryHandler).FullName!, problem);
    }

    [Fact]
    public void RequestWithTwoRequestInterfacesInAssembly_IsReported()
    {
        using var provider = Build(_ => { });

        var ex = VerifyFails(provider, typeof(AmbiguousRequest).Assembly);

        Assert.Contains(ex.Problems, p => p.Contains($"'{typeof(AmbiguousRequest).FullName}' implements more than one request interface", StringComparison.Ordinal));
        Assert.Contains(ex.Problems, p => p.Contains($"'{typeof(UnhandledCommand).FullName}' has no handler", StringComparison.Ordinal));
    }

    // ---- Notifications ---------------------------------------------------------------------------------------------------

    [Fact]
    public void PublisherRemoved_IsReported()
    {
        using var provider = Build(s => s.RemoveAll<INotificationPublisher>());

        var problem = Assert.Single(VerifyFails(provider).Problems);

        Assert.Contains(nameof(INotificationPublisher), problem);
    }

    [Fact]
    public void NotificationHandlerRemoved_IsReported()
    {
        using var provider = Build(s =>
        {
            s.AddNotificationHandler<TestNotification, TestNotificationHandler>();
            s.RemoveAll<INotificationHandler<TestNotification>>();
        });

        var problem = Assert.Single(VerifyFails(provider).Problems);

        Assert.Contains(typeof(TestNotificationHandler).FullName!, problem);
        Assert.Contains(typeof(TestNotification).FullName!, problem);
    }

    // ---- All problems at once ------------------------------------------------------------------------------------------

    [Fact]
    public void EveryProblem_IsListedInOneException()
    {
        using var provider = Build(s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            s.AddTransient<IRequestHandler<TestCommand, TestResult>, CancellationAwareHandler>();
            s.AddRequestHandler<TestQuery, TestResult, TestQueryHandler>();
            s.RemoveAll<IRequestHandler<TestQuery, TestResult>>();
            s.RemoveAll<INotificationPublisher>();
        }, cfg => cfg.RegisterServicesFromAssembly(ScanAssembly));

        var ex = VerifyFails(provider, ScanAssembly);

        Assert.Equal(4, ex.Problems.Count);
        Assert.StartsWith("The Hecole.Mediator configuration has 4 problem(s):", ex.Message);
        Assert.All(ex.Problems, p => Assert.Contains("- " + p, ex.Message));
        Assert.Contains(ex.Problems, p => p.Contains(typeof(CancellationAwareHandler).FullName!, StringComparison.Ordinal));
        Assert.Contains(ex.Problems, p => p.Contains(typeof(TestQueryHandler).FullName!, StringComparison.Ordinal));
        Assert.Contains(ex.Problems, p => p.Contains(typeof(ScanSkippedQuery).FullName!, StringComparison.Ordinal));
        Assert.Contains(ex.Problems, p => p.Contains(nameof(INotificationPublisher), StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_OneEntryPerProblem_InMessageOrder_AndReadOnly()
    {
        using var provider = Build(s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            s.AddTransient<IRequestHandler<TestCommand, TestResult>, CancellationAwareHandler>();
            s.RemoveAll<INotificationPublisher>();
        }, cfg => cfg.RegisterServicesFromAssembly(ScanAssembly));

        var ex = VerifyFails(provider, ScanAssembly);

        var lines = ex.Message.Split(Environment.NewLine).Skip(1).ToList();
        Assert.Equal(3, ex.Problems.Count);
        Assert.Equal(lines, ex.Problems.Select(p => "- " + p));

        var asList = Assert.IsAssignableFrom<IList<string>>(ex.Problems);
        Assert.True(asList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => asList.Add("x"));
        Assert.Throws<NotSupportedException>(() => asList[0] = "x");
        Assert.Throws<NotSupportedException>(() => asList.Clear());
        Assert.IsNotType<string[]>(ex.Problems);
        Assert.IsNotType<List<string>>(ex.Problems);
    }

    // ---- Nothing is created ------------------------------------------------------------------------------------------

    [Fact]
    public void Verification_CreatesNoHandlerBehaviorOrPublisher()
    {
        var counter = new ConstructionCounter();
        using var provider = Build(
            s =>
            {
                s.AddSingleton(counter);
                s.AddRequestHandler<ExplodingQuery, string, ExplodingQueryHandler>();
                s.AddNotificationHandler<TestNotification, ExplodingNotificationHandler>();
            },
            cfg =>
            {
                cfg.AddOpenBehavior(typeof(ExplodingBehavior<,>));
                cfg.NotificationPublisherType = typeof(ExplodingPublisher);
            });

        provider.VerifyHecoleMediator();

        Assert.Equal(0, counter.Count);

        // The same container does create (and fail) once a request is actually sent.
        using var scope = provider.CreateScope();
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ISender>().Send(new ExplodingQuery()).GetAwaiter().GetResult());
        Assert.Equal(1, counter.Count);
    }

    // ---- Not registered / arguments ----------------------------------------------------------------------------------

    [Fact]
    public void WithoutAddHecoleMediator_IsReported()
    {
        var onlyHandlers = new ServiceCollection();
        onlyHandlers.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        using var withHandlers = onlyHandlers.BuildServiceProvider();
        using var empty = new ServiceCollection().BuildServiceProvider();

        Assert.Contains("AddHecoleMediator", Assert.Single(VerifyFails(withHandlers).Problems));
        Assert.Contains("AddHecoleMediator", Assert.Single(VerifyFails(empty).Problems));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        using var provider = Build(_ => { });

        Assert.Throws<ArgumentNullException>(() => ((IServiceProvider)null!).VerifyHecoleMediator());
        Assert.Throws<ArgumentNullException>(() => provider.VerifyHecoleMediator(null!));
    }

    // ---- (c) Open behaviors that do not close for a request -------------------------------------------------------------

    [Fact]
    public void OpenBehaviorThatDoesNotFitARequest_IsFoundButIsNotAProblem()
    {
        using var provider = Build(
            s =>
            {
                s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
                s.AddRequestHandler<VoidCommand, VoidCommandHandler>();
            },
            cfg => cfg.AddOpenBehavior(typeof(OpenGenericTestBehavior<,>)).AddOpenBehavior(typeof(PassThroughBehavior<,>)));

        provider.VerifyHecoleMediator();
        var skipped = MediatorVerification.FindSkippedOpenBehaviors(provider);

        // OpenGenericTestBehavior requires TRequest : IRequest<TResponse>; VoidCommand is an IRequest (TResponse = Unit).
        Assert.Equal([(typeof(VoidCommand), typeof(OpenGenericTestBehavior<,>))], skipped);
    }

    // ---- Types ------------------------------------------------------------------------------------------------------

    public sealed record VoidCommand : IRequest;

    public sealed class VoidCommandHandler : IRequestHandler<VoidCommand>
    {
        public Task Handle(VoidCommand request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ScanSkippedQueryHandler : IRequestHandler<ScanSkippedQuery, string>
    {
        public Task<string> Handle(ScanSkippedQuery request, CancellationToken cancellationToken) => Task.FromResult("skipped");
    }

    public sealed class OtherScanQueryHandler : IRequestHandler<ScanQuery, string>
    {
        public Task<string> Handle(ScanQuery request, CancellationToken cancellationToken) => Task.FromResult("other");
    }

    public sealed class PassThroughBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public sealed class ConstructionCounter
    {
        private int _count;

        public int Count => _count;

        public void Increment() => Interlocked.Increment(ref _count);
    }

    public sealed record ExplodingQuery : IRequest<string>;

    public sealed class ExplodingQueryHandler : IRequestHandler<ExplodingQuery, string>
    {
        public ExplodingQueryHandler(ConstructionCounter counter)
        {
            counter.Increment();
            throw new InvalidOperationException("handler constructed");
        }

        public Task<string> Handle(ExplodingQuery request, CancellationToken cancellationToken) => Task.FromResult("never");
    }

    public sealed class ExplodingNotificationHandler : INotificationHandler<TestNotification>
    {
        public ExplodingNotificationHandler(ConstructionCounter counter)
        {
            counter.Increment();
            throw new InvalidOperationException("notification handler constructed");
        }

        public Task Handle(TestNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ExplodingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public ExplodingBehavior(ConstructionCounter counter)
        {
            counter.Increment();
            throw new InvalidOperationException("behavior constructed");
        }

        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public sealed class ExplodingPublisher : INotificationPublisher
    {
        private readonly ForeachAwaitPublisher _inner = new();

        public ExplodingPublisher(ConstructionCounter counter)
        {
            counter.Increment();
            throw new InvalidOperationException("publisher constructed");
        }

        public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
            => _inner.Publish(handlerExecutors, notification, cancellationToken);
    }
}
