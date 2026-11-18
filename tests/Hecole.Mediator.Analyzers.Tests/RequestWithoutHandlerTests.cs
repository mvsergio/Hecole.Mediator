using Microsoft.CodeAnalysis;
using static Hecole.Mediator.Analyzers.Tests.AnalyzerHarness;

namespace Hecole.Mediator.Analyzers.Tests;

/// <summary>HM0001: request without a handler in the compilation (Info, on by default).</summary>
public sealed class RequestWithoutHandlerTests
{
    private const string Id = "HM0001";

    [Fact]
    public async Task RequestWithoutHandler_NextToHandledOne_IsReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Handled : IRequest<int>;
            public sealed record Orphan : IRequest<int>;
            public sealed class HandledHandler : IRequestHandler<Handled, int>
            {
                public Task<int> Handle(Handled request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """);

        var diagnostic = Assert.Single(diagnostics.WithId(Id));
        Assert.Equal("Orphan", diagnostic.Text());
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal("Request 'Orphan' has no IRequestHandler in this compilation; sending it fails at run time", diagnostic.GetMessage());
    }

    [Fact]
    public async Task VoidRequestWithoutHandler_IsReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Ping : IRequest;
            public sealed record Orphan(string Name) : IRequest;
            public sealed class PingHandler : IRequestHandler<Ping>
            {
                public Task Handle(Ping request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal("Orphan", Assert.Single(diagnostics.WithId(Id)).Text());
    }

    [Fact]
    public async Task StructAndClassRequests_AreBothChecked()
    {
        var diagnostics = await AnalyzeAsync("""
            public readonly record struct StructOrphan : IRequest<int>;
            public sealed class ClassOrphan : IRequest;
            public sealed record Handled : IRequest;
            public sealed class HandledHandler : IRequestHandler<Handled>
            {
                public Task Handle(Handled request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal(["StructOrphan", "ClassOrphan"], diagnostics.WithId(Id).Select(diagnostic => diagnostic.Text()));
    }

    [Fact]
    public async Task EveryRequestHandled_IsNotReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<string>;
            public sealed record Command : IRequest;
            public sealed class QueryHandler : IRequestHandler<Query, string>
            {
                public Task<string> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult("");
            }
            public sealed class CommandHandler : IRequestHandler<Command>
            {
                public Task Handle(Command request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task OneClassHandlingSeveralRequests_CoversAll()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record A : IRequest<int>;
            public sealed record B : IRequest;
            public sealed class Both : IRequestHandler<A, int>, IRequestHandler<B>
            {
                public Task<int> Handle(A request, CancellationToken cancellationToken) => Task.FromResult(1);
                public Task Handle(B request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
    }

    // False positive 1: contracts assembly (requests only, handlers in another assembly).
    [Fact]
    public async Task ContractsAssemblyWithoutAnyHandler_IsNotReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record CreateOrder(string Sku) : IRequest<int>;
            public sealed record CancelOrder(int Id) : IRequest;
            """);

        Assert.Empty(diagnostics);
    }

    // False positive 1, both sides: contracts compiled alone, handlers compiled in a second assembly that references it.
    [Fact]
    public async Task RequestsAndHandlersInDifferentAssemblies_AreNotReportedInEither()
    {
        var contracts = Compile("Contracts", ["""
            namespace Shop;
            public sealed record CreateOrder(string Sku) : IRequest<int>;
            public sealed record CancelOrder(int Id) : IRequest;
            """]);
        var handlers = Compile("Handlers", ["""
            namespace Shop.Handlers;
            public sealed class CreateOrderHandler : IRequestHandler<Shop.CreateOrder, int>
            {
                public Task<int> Handle(Shop.CreateOrder request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """], extraReferences: [contracts.ToMetadataReference()]);

        Assert.Empty(await AnalyzeAsync(contracts));
        Assert.Empty(await AnalyzeAsync(handlers));
    }

    // An open generic handler class covers no request: scanning skips it, the container cannot close a one-parameter class
    // for IRequestHandler<,>, and the mediator does not support open generic handlers (VerifyHecoleMediator reports them).
    // It still marks the compilation as one that declares handlers. (Until 2.0.0 it silenced HM0001 for every request of
    // its shape.)
    [Fact]
    public async Task OpenGenericHandler_DoesNotCoverRequests()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<string>;
            public sealed record Command : IRequest;
            public sealed class GenericHandler<TRequest> : IRequestHandler<TRequest, string> where TRequest : IRequest<string>
            {
                public Task<string> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult("");
            }
            """);

        Assert.Equal(["Query", "Command"], diagnostics.WithId(Id).Select(diagnostic => diagnostic.Text()));
    }

    [Fact]
    public async Task OpenGenericVoidHandler_DoesNotCoverVoidRequests()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Command : IRequest;
            public sealed class GenericHandler<TRequest> : IRequestHandler<TRequest> where TRequest : IRequest
            {
                public Task Handle(TRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal("Command", Assert.Single(diagnostics.WithId(Id)).Text());
    }

    // The way an open generic handler class can serve a request: a closed construction registered with AddRequestHandler.
    // An AddRequestHandler call for the request covers it, directly or through a generic helper.
    [Fact]
    public async Task RequestRegisteredWithAddRequestHandler_IsCovered()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<string>;
            public sealed record Command : IRequest;
            public sealed class GenericHandler<TRequest> : IRequestHandler<TRequest, string> where TRequest : IRequest<string>
            {
                public Task<string> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult("");
            }
            public sealed class GenericVoidHandler<TRequest> : IRequestHandler<TRequest> where TRequest : IRequest
            {
                public Task Handle(TRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public static class Composition
            {
                private static IServiceCollection Void<TRequest, THandler>(this IServiceCollection services)
                    where TRequest : IRequest
                    where THandler : class, IRequestHandler<TRequest>
                    => services.AddRequestHandler<TRequest, THandler>();

                public static void Configure(IServiceCollection services)
                {
                    services.AddRequestHandler<Query, string, GenericHandler<Query>>();
                    services.Void<Command, GenericVoidHandler<Command>>();
                }
            }
            """);

        Assert.Empty(diagnostics.WithId(Id));
    }

    // False positive 3: types that are never sent as such.
    [Fact]
    public async Task AbstractGenericAndInterfaceRequests_AreNotReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public abstract record CommandBase : IRequest;
            public sealed record Paged<T>(int Page) : IRequest<List<T>>;
            public interface IQuery : IRequest<string>;
            public sealed class Outer<T>
            {
                public sealed record Inner : IRequest<int>;
            }
            public sealed record Handled : IRequest;
            public sealed class HandledHandler : IRequestHandler<Handled>
            {
                public Task Handle(Handled request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HandlerThroughGenericBaseClass_Counts()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<int>;
            public abstract class HandlerBase<TRequest> : IRequestHandler<TRequest, int> where TRequest : IRequest<int>
            {
                public Task<int> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class QueryHandler : HandlerBase<Query>;
            """);

        Assert.Empty(diagnostics);
    }

    // An abstract handler class cannot be registered, so it handles nothing by itself (until 2.0.0 it counted). It still
    // marks the compilation as one that declares handlers.
    [Fact]
    public async Task AbstractClosedHandler_DoesNotCount()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<int>;
            public abstract class QueryHandlerBase : IRequestHandler<Query, int>
            {
                public abstract Task<int> Handle(Query request, CancellationToken cancellationToken);
            }
            """);

        Assert.Equal("Query", Assert.Single(diagnostics.WithId(Id)).Text());
    }

    [Fact]
    public async Task AbstractHandlerWithConcreteSubclass_Counts()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<int>;
            public abstract class QueryHandlerBase : IRequestHandler<Query, int>
            {
                public abstract Task<int> Handle(Query request, CancellationToken cancellationToken);
            }
            public sealed class QueryHandler : QueryHandlerBase
            {
                public override Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """);

        Assert.Empty(diagnostics);
    }

    // Not a false positive: the mediator resolves the exact type, so a derived request needs its own handler.
    [Fact]
    public async Task DerivedRequestWithoutOwnHandler_IsReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public record BaseQuery : IRequest<int>;
            public sealed record DerivedQuery : BaseQuery;
            public sealed class BaseQueryHandler : IRequestHandler<BaseQuery, int>
            {
                public Task<int> Handle(BaseQuery request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """);

        Assert.Equal("DerivedQuery", Assert.Single(diagnostics.WithId(Id)).Text());
    }

    [Fact]
    public async Task RequestWithTwoRequestInterfaces_IsLeftToTheLibrary()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Ambiguous : IRequest, IRequest<int>;
            public sealed record Handled : IRequest;
            public sealed class HandledHandler : IRequestHandler<Handled>
            {
                public Task Handle(Handled request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Empty(diagnostics);
    }

    // False positive 4: handler produced by a source generator.
    [Fact]
    public async Task HandlerInGeneratedCode_Counts()
    {
        var diagnostics = await AnalyzeAsync(
            """
            public sealed record Query : IRequest<int>;
            """,
            """
            // <auto-generated/>
            public sealed class GeneratedQueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RequestInGeneratedCode_IsNotReported()
    {
        var diagnostics = await AnalyzeAsync(
            """
            public sealed record Handled : IRequest;
            public sealed class HandledHandler : IRequestHandler<Handled>
            {
                public Task Handle(Handled request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """,
            """
            // <auto-generated/>
            public sealed record GeneratedOrphan : IRequest;
            """);

        Assert.Empty(diagnostics);
    }

    // False positive 5 (remote handler, test double): the project turns the rule off or lowers it; it is Info anyway.
    [Fact]
    public async Task RuleTurnedOff_IsNotReported()
    {
        var compilation = Compile("Sample", ["""
            public sealed record Handled : IRequest;
            public sealed record Remote : IRequest;
            public sealed class HandledHandler : IRequestHandler<Handled>
            {
                public Task Handle(Handled request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """], options: new Dictionary<string, ReportDiagnostic> { [Id] = ReportDiagnostic.Suppress });

        Assert.Empty(await AnalyzeAsync(compilation));
    }

    [Fact]
    public async Task RuleRaisedToError_IsReportedAsError()
    {
        var diagnostics = await AnalyzeWithAsync([Id], """
            public sealed record Handled : IRequest;
            public sealed record Orphan : IRequest;
            public sealed class HandledHandler : IRequestHandler<Handled>
            {
                public Task Handle(Handled request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(diagnostics.WithId(Id)).Severity);
    }
}
