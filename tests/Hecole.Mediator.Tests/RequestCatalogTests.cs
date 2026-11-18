using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hecole.Mediator.Tests;

/// <summary>
/// <see cref="IRequestCatalog"/> reports what actually runs: the handler of the effective registration and the pipeline
/// behaviors the default container resolves, in order. Each pipeline test asserts the catalog and the execution trace of
/// the same container.
/// </summary>
public class RequestCatalogTests
{
    private static ServiceProvider BuildRoot(IServiceCollection services)
        => services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static async Task<List<Type>> SendAndTrace(ServiceProvider root, object request)
    {
        var trace = root.GetRequiredService<CatalogTrace>();
        trace.Clear();
        using var scope = root.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
        return trace.ToList();
    }

    // ---- Pipeline content and order ---------------------------------------------------------------------------------

    [Fact]
    public async Task Behaviors_MixedOpenClosedFactoryInstanceAndLifetimes_EqualWhatRuns_InOrder()
    {
        var trace = new CatalogTrace();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddTransient<IPipelineBehavior<PlaceOrder, string>, PlaceOrderClosedA>(); // closed, before the open ones
        services.AddHecoleMediator(cfg => cfg
            .AddOpenBehavior(typeof(OuterOpenBehavior<,>), ServiceLifetime.Scoped)
            .AddOpenBehavior(typeof(CommandOnlyBehavior<,>)));
        services.AddSingleton<IPipelineBehavior<PlaceOrder, string>>(sp => new PlaceOrderFactoryBehavior(sp.GetRequiredService<CatalogTrace>()));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerOpenBehavior<,>)); // open, after a closed one
        services.AddSingleton<IPipelineBehavior<PlaceOrder, string>>(new PlaceOrderInstanceBehavior(trace));
        services.AddScoped<IPipelineBehavior<PlaceOrder, string>, PlaceOrderClosedB>(); // closed, after the open ones
        services.AddRequestHandler<PlaceOrder, string, PlaceOrderHandler>();
        services.AddRequestHandler<ReadOrder, string, ReadOrderHandler>();
        using var root = BuildRoot(services);

        var catalog = root.GetRequiredService<IRequestCatalog>();
        var place = catalog.Find(typeof(PlaceOrder))!;
        var read = catalog.Find(typeof(ReadOrder))!;

        Assert.Equal(
            [
                typeof(PlaceOrderClosedA),
                typeof(OuterOpenBehavior<PlaceOrder, string>),
                typeof(CommandOnlyBehavior<PlaceOrder, string>),
                typeof(IPipelineBehavior<PlaceOrder, string>), // factory registration: the closed service type
                typeof(InnerOpenBehavior<PlaceOrder, string>),
                typeof(PlaceOrderInstanceBehavior),
                typeof(PlaceOrderClosedB),
            ],
            place.Behaviors);
        Assert.Empty(place.SkippedBehaviors);
        Assert.Equal(
            [
                typeof(PlaceOrderClosedA),
                typeof(OuterOpenBehavior<PlaceOrder, string>),
                typeof(CommandOnlyBehavior<PlaceOrder, string>),
                typeof(PlaceOrderFactoryBehavior),
                typeof(InnerOpenBehavior<PlaceOrder, string>),
                typeof(PlaceOrderInstanceBehavior),
                typeof(PlaceOrderClosedB),
                typeof(PlaceOrderHandler),
            ],
            await SendAndTrace(root, new PlaceOrder()));

        // ReadOrder is not a command: the constrained open behavior is skipped, and does not run.
        Assert.Equal([typeof(OuterOpenBehavior<ReadOrder, string>), typeof(InnerOpenBehavior<ReadOrder, string>)], read.Behaviors);
        Assert.Equal([typeof(CommandOnlyBehavior<,>)], read.SkippedBehaviors);
        Assert.Equal(
            [typeof(OuterOpenBehavior<ReadOrder, string>), typeof(InnerOpenBehavior<ReadOrder, string>), typeof(ReadOrderHandler)],
            await SendAndTrace(root, new ReadOrder()));
    }

    [Fact]
    public async Task Behaviors_OpenBehaviorWhoseConstraintIsNotSatisfied_IsSkipped_AndDoesNotRun()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CatalogTrace>();
        services.AddHecoleMediator(cfg => cfg.AddOpenBehavior(typeof(CommandOnlyBehavior<,>)).AddOpenBehavior(typeof(OuterOpenBehavior<,>)));
        services.AddRequestHandler<ReadOrder, string, ReadOrderHandler>();
        using var root = BuildRoot(services);

        var read = root.GetRequiredService<IRequestCatalog>().Find(typeof(ReadOrder))!;

        Assert.Equal([typeof(CommandOnlyBehavior<,>)], read.SkippedBehaviors);
        Assert.DoesNotContain(read.Behaviors, b => b.IsGenericType && b.GetGenericTypeDefinition() == typeof(CommandOnlyBehavior<,>));
        Assert.Equal([typeof(OuterOpenBehavior<ReadOrder, string>)], read.Behaviors);
        Assert.Equal([typeof(OuterOpenBehavior<ReadOrder, string>), typeof(ReadOrderHandler)], await SendAndTrace(root, new ReadOrder()));
    }

    [Fact]
    public async Task RequestWithoutResponse_HasUnitResponse_AndTheUnitPipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CatalogTrace>();
        services.AddHecoleMediator(cfg => cfg
            .AddOpenBehavior(typeof(OuterOpenBehavior<,>))
            .AddOpenBehavior(typeof(ResponseDeclaredBehavior<,>))
            .AddOpenBehavior(typeof(CommandOnlyBehavior<,>)));
        services.AddTransient<IPipelineBehavior<ArchiveOrder, Unit>, ArchiveOrderClosedBehavior>();
        services.AddRequestHandler<ArchiveOrder, ArchiveOrderHandler>();
        using var root = BuildRoot(services);

        var archive = Assert.Single(root.GetRequiredService<IRequestCatalog>().Requests);

        Assert.Equal(typeof(ArchiveOrder), archive.RequestType);
        Assert.Equal(typeof(Unit), archive.ResponseType);
        Assert.Equal(typeof(ArchiveOrderHandler), archive.HandlerType);
        Assert.Equal(
            [typeof(OuterOpenBehavior<ArchiveOrder, Unit>), typeof(CommandOnlyBehavior<ArchiveOrder, Unit>), typeof(ArchiveOrderClosedBehavior)],
            archive.Behaviors);
        // ArchiveOrder is an IRequest, not an IRequest<Unit>: the behavior constrained on IRequest<TResponse> is skipped.
        Assert.Equal([typeof(ResponseDeclaredBehavior<,>)], archive.SkippedBehaviors);
        Assert.Equal(
            [
                typeof(OuterOpenBehavior<ArchiveOrder, Unit>),
                typeof(CommandOnlyBehavior<ArchiveOrder, Unit>),
                typeof(ArchiveOrderClosedBehavior),
                typeof(ArchiveOrderHandler),
            ],
            await SendAndTrace(root, new ArchiveOrder()));
    }

    // ---- Requests listed and handler types ---------------------------------------------------------------------------

    [Fact]
    public async Task HandlerRegisteredDirectly_IsListedAfterTheLibraryRegisteredOnes_WithItsHandlerType()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CatalogTrace>();
        services.AddTransient<IRequestHandler<ReadOrder, string>, ReadOrderHandler>(); // direct, first in the collection
        services.AddHecoleMediator(_ => { });
        services.AddTransient<IRequestHandler<ArchiveOrder>, ArchiveOrderHandler>(); // direct, without response
        services.AddRequestHandler<PlaceOrder, string, PlaceOrderHandler>();
        // Never dispatched: not listed.
        services.AddTransient<IRequestHandler<IOrderQuery, string>, AnyOrderQueryHandler>();
        services.AddTransient<IRequestHandler<NarrowQuery, object>, WideningHandler>();
        using var root = BuildRoot(services);

        var catalog = root.GetRequiredService<IRequestCatalog>();

        Assert.Equal(
            [
                new RequestDescriptor(typeof(PlaceOrder), typeof(string), typeof(PlaceOrderHandler)),
                new RequestDescriptor(typeof(ReadOrder), typeof(string), typeof(ReadOrderHandler)),
                new RequestDescriptor(typeof(ArchiveOrder), typeof(Unit), typeof(ArchiveOrderHandler)),
            ],
            catalog.Requests);
        Assert.Equal([typeof(ReadOrderHandler)], await SendAndTrace(root, new ReadOrder()));
    }

    [Fact]
    public async Task ReplacedHandler_IsTheHandlerType_AndIsTheOneThatRuns()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CatalogTrace>();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<ReadOrder, string, ReadOrderHandler>();
        services.Replace(ServiceDescriptor.Transient<IRequestHandler<ReadOrder, string>, ReadOrderReplacementHandler>());
        using var root = BuildRoot(services);

        var read = root.GetRequiredService<IRequestCatalog>().Find(typeof(ReadOrder))!;

        Assert.Equal(typeof(ReadOrderReplacementHandler), read.HandlerType);
        Assert.Equal([typeof(ReadOrderReplacementHandler)], await SendAndTrace(root, new ReadOrder()));
    }

    [Fact]
    public void FactoryAndInstanceHandlerRegistrations_ReportTheServiceTypeAndTheInstanceType()
    {
        var trace = new CatalogTrace();
        var services = new ServiceCollection();
        services.AddSingleton(trace);
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<ReadOrder, string, ReadOrderHandler>();
        services.AddTransient<IRequestHandler<ReadOrder, string>>(sp => new ReadOrderReplacementHandler(sp.GetRequiredService<CatalogTrace>()));
        services.AddSingleton<IRequestHandler<PlaceOrder, string>>(new PlaceOrderHandler(trace));
        services.AddTransient<IRequestHandler<ArchiveOrder>>(sp => new ArchiveOrderHandler(sp.GetRequiredService<CatalogTrace>()));
        using var root = BuildRoot(services);

        var catalog = root.GetRequiredService<IRequestCatalog>();

        Assert.Equal(typeof(IRequestHandler<ReadOrder, string>), catalog.Find(typeof(ReadOrder))!.HandlerType);
        Assert.Equal(typeof(PlaceOrderHandler), catalog.Find(typeof(PlaceOrder))!.HandlerType);
        Assert.Equal(typeof(IRequestHandler<ArchiveOrder>), catalog.Find(typeof(ArchiveOrder))!.HandlerType);
    }

    // A catalog written outside the library (a test double, for example) implements only Requests.
    private sealed class RequestsOnlyCatalog(params RequestDescriptor[] requests) : IRequestCatalog
    {
        public IReadOnlyList<RequestDescriptor> Requests { get; } = requests;
    }

    [Fact]
    public void Find_DefaultImplementation_SearchesRequests_SoATypeThatOnlyHasRequestsStillCompiles()
    {
        var known = new RequestDescriptor(typeof(string), typeof(int), typeof(object));
        IRequestCatalog catalog = new RequestsOnlyCatalog(known);

        Assert.Same(known, catalog.Find(typeof(string)));
        Assert.Null(catalog.Find(typeof(int)));
        Assert.Throws<ArgumentNullException>(() => catalog.Find(null!));
    }

    [Fact]
    public void Find_KnownUnknownAndDirectRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CatalogTrace>();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<PlaceOrder, string, PlaceOrderHandler>();
        services.AddTransient<IRequestHandler<ReadOrder, string>, ReadOrderHandler>();
        using var root = BuildRoot(services);

        var catalog = root.GetRequiredService<IRequestCatalog>();

        Assert.Same(catalog.Requests[0], catalog.Find(typeof(PlaceOrder)));
        Assert.Same(catalog.Requests[1], catalog.Find(typeof(ReadOrder)));
        Assert.Equal(typeof(ReadOrderHandler), catalog.Find(typeof(ReadOrder))!.HandlerType);
        Assert.Null(catalog.Find(typeof(ArchiveOrder)));
        Assert.Null(catalog.Find(typeof(IOrderQuery)));
        Assert.Throws<ArgumentNullException>(() => catalog.Find(null!));
    }

    // ---- Snapshot, laziness, equality --------------------------------------------------------------------------------

    [Fact]
    public void RegistrationsAddedAfterTheCatalogWasResolved_DoNotChangeIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CatalogTrace>();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<PlaceOrder, string, PlaceOrderHandler>();
        using var root = BuildRoot(services);

        var catalog = root.GetRequiredService<IRequestCatalog>(); // the snapshot is taken here, before any read

        services.AddRequestHandler<ArchiveOrder, ArchiveOrderHandler>();
        services.AddTransient<IRequestHandler<ReadOrder, string>, ReadOrderHandler>();
        services.AddTransient<IPipelineBehavior<PlaceOrder, string>, PlaceOrderClosedA>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OuterOpenBehavior<,>));
        services.Replace(ServiceDescriptor.Transient<IRequestHandler<PlaceOrder, string>, PlaceOrderInstanceHandler>());

        Assert.Equal([new RequestDescriptor(typeof(PlaceOrder), typeof(string), typeof(PlaceOrderHandler))], catalog.Requests);
    }

    [Fact]
    public void Requests_ReadConcurrently_IsComputedOnce()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CatalogTrace>();
        services.AddHecoleMediator(cfg => cfg.AddOpenBehavior(typeof(OuterOpenBehavior<,>)));
        services.AddRequestHandler<PlaceOrder, string, PlaceOrderHandler>();
        using var root = BuildRoot(services);
        var catalog = root.GetRequiredService<IRequestCatalog>();

        var seen = new IReadOnlyList<RequestDescriptor>[16];
        Parallel.For(0, seen.Length, i => seen[i] = catalog.Requests);

        Assert.All(seen, list => Assert.Same(seen[0], list));
        Assert.Same(catalog.Requests[0].Behaviors, catalog.Find(typeof(PlaceOrder))!.Behaviors);
    }

    [Fact]
    public void Equality_ComparesBehaviorListsBySequence()
    {
        var first = new RequestDescriptor(typeof(PlaceOrder), typeof(string), typeof(PlaceOrderHandler))
        {
            Behaviors = [typeof(PlaceOrderClosedA), typeof(PlaceOrderClosedB)],
            SkippedBehaviors = new List<Type> { typeof(CommandOnlyBehavior<,>) },
        };
        var same = new RequestDescriptor(typeof(PlaceOrder), typeof(string), typeof(PlaceOrderHandler))
        {
            Behaviors = new List<Type> { typeof(PlaceOrderClosedA), typeof(PlaceOrderClosedB) },
            SkippedBehaviors = [typeof(CommandOnlyBehavior<,>)],
        };

        Assert.Equal(first, same);
        Assert.True(first == same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());

        Assert.NotEqual(first, same with { Behaviors = [typeof(PlaceOrderClosedB), typeof(PlaceOrderClosedA)] });
        Assert.NotEqual(first, same with { SkippedBehaviors = [] });
        Assert.NotEqual(first, same with { HandlerType = typeof(PlaceOrderInstanceHandler) });
        Assert.Equal(new RequestDescriptor(typeof(PlaceOrder), typeof(string), typeof(PlaceOrderHandler)), first with { Behaviors = [], SkippedBehaviors = [] });
        Assert.Empty(new RequestDescriptor(typeof(PlaceOrder), typeof(string), typeof(PlaceOrderHandler)).Behaviors);
        Assert.Throws<ArgumentNullException>(() => first with { Behaviors = null! });
    }

    // ---- Types ------------------------------------------------------------------------------------------------------

    public sealed class CatalogTrace : List<Type>;

    public interface ICatalogCommand;

    public sealed record PlaceOrder : IRequest<string>, ICatalogCommand;

    public sealed record ReadOrder : IRequest<string>;

    public sealed record ArchiveOrder : IRequest, ICatalogCommand;

    public interface IOrderQuery : IRequest<string>;

    public sealed record NarrowQuery : IRequest<string>;

    public sealed class PlaceOrderHandler(CatalogTrace trace) : IRequestHandler<PlaceOrder, string>
    {
        public Task<string> Handle(PlaceOrder request, CancellationToken cancellationToken)
        {
            trace.Add(GetType());
            return Task.FromResult("placed");
        }
    }

    public sealed class PlaceOrderInstanceHandler : IRequestHandler<PlaceOrder, string>
    {
        public Task<string> Handle(PlaceOrder request, CancellationToken cancellationToken) => Task.FromResult("instance");
    }

    public sealed class ReadOrderHandler(CatalogTrace trace) : IRequestHandler<ReadOrder, string>
    {
        public Task<string> Handle(ReadOrder request, CancellationToken cancellationToken)
        {
            trace.Add(GetType());
            return Task.FromResult("read");
        }
    }

    public sealed class ReadOrderReplacementHandler(CatalogTrace trace) : IRequestHandler<ReadOrder, string>
    {
        public Task<string> Handle(ReadOrder request, CancellationToken cancellationToken)
        {
            trace.Add(GetType());
            return Task.FromResult("replacement");
        }
    }

    public sealed class ArchiveOrderHandler(CatalogTrace trace) : IRequestHandler<ArchiveOrder>
    {
        public Task Handle(ArchiveOrder request, CancellationToken cancellationToken)
        {
            trace.Add(GetType());
            return Task.CompletedTask;
        }
    }

    public sealed class AnyOrderQueryHandler : IRequestHandler<IOrderQuery, string>
    {
        public Task<string> Handle(IOrderQuery request, CancellationToken cancellationToken) => Task.FromResult("any");
    }

    public sealed class WideningHandler : IRequestHandler<NarrowQuery, object>
    {
        public Task<object> Handle(NarrowQuery request, CancellationToken cancellationToken) => Task.FromResult<object>("wide");
    }

    public abstract class TracingBehavior<TRequest, TResponse>(CatalogTrace trace) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            trace.Add(GetType());
            return next();
        }
    }

    public sealed class OuterOpenBehavior<TRequest, TResponse>(CatalogTrace trace) : TracingBehavior<TRequest, TResponse>(trace)
        where TRequest : notnull;

    public sealed class InnerOpenBehavior<TRequest, TResponse>(CatalogTrace trace) : TracingBehavior<TRequest, TResponse>(trace)
        where TRequest : notnull;

    public sealed class CommandOnlyBehavior<TRequest, TResponse>(CatalogTrace trace) : TracingBehavior<TRequest, TResponse>(trace)
        where TRequest : ICatalogCommand;

    public sealed class ResponseDeclaredBehavior<TRequest, TResponse>(CatalogTrace trace) : TracingBehavior<TRequest, TResponse>(trace)
        where TRequest : IRequest<TResponse>;

    public sealed class PlaceOrderClosedA(CatalogTrace trace) : TracingBehavior<PlaceOrder, string>(trace);

    public sealed class PlaceOrderClosedB(CatalogTrace trace) : TracingBehavior<PlaceOrder, string>(trace);

    public sealed class PlaceOrderFactoryBehavior(CatalogTrace trace) : TracingBehavior<PlaceOrder, string>(trace);

    public sealed class PlaceOrderInstanceBehavior(CatalogTrace trace) : TracingBehavior<PlaceOrder, string>(trace);

    public sealed class ArchiveOrderClosedBehavior(CatalogTrace trace) : TracingBehavior<ArchiveOrder, Unit>(trace);
}
