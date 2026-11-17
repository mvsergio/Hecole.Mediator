using Microsoft.CodeAnalysis;
using static Hecole.Mediator.Analyzers.Tests.AnalyzerHarness;

namespace Hecole.Mediator.Analyzers.Tests;

/// <summary>HM0002: more than one handler for the same request in the compilation (Info, on by default).</summary>
public sealed class MultipleHandlersTests
{
    private const string Id = "HM0002";

    [Fact]
    public async Task TwoHandlersForTheSameRequest_ReportsEachHandler()
    {
        var diagnostics = await AnalyzeAsync("""
            namespace Shop;
            public sealed record Query : IRequest<int>;
            public sealed class FirstHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class SecondHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(2);
            }
            """);

        var reported = diagnostics.WithId(Id);
        Assert.Equal(["FirstHandler", "SecondHandler"], reported.Select(diagnostic => diagnostic.Text()));
        Assert.All(reported, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
            Assert.Equal("Request 'Shop.Query' has 2 handlers in this compilation: Shop.FirstHandler, Shop.SecondHandler", diagnostic.GetMessage());
        });
        Assert.Equal("SecondHandler", Assert.Single(reported[0].AdditionalLocations).SourceTree!.GetText().ToString(reported[0].AdditionalLocations[0].SourceSpan));
    }

    [Fact]
    public async Task TwoVoidHandlersForTheSameRequest_AreReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Command : IRequest;
            public sealed class FirstHandler : IRequestHandler<Command>
            {
                public Task Handle(Command request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public sealed class SecondHandler : IRequestHandler<Command>
            {
                public Task Handle(Command request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """);

        Assert.Equal(2, diagnostics.WithId(Id).Count);
    }

    [Fact]
    public async Task OneHandlerPerRequest_IsNotReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record A : IRequest<int>;
            public sealed record B : IRequest<int>;
            public sealed class AHandler : IRequestHandler<A, int>
            {
                public Task<int> Handle(A request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class BHandler : IRequestHandler<B, int>
            {
                public Task<int> Handle(B request, CancellationToken cancellationToken) => Task.FromResult(2);
            }
            """);

        Assert.Empty(diagnostics);
    }

    // The library treats a concrete subclass of a concrete handler as a second handler when scanning: same rule here.
    [Fact]
    public async Task ConcreteHandlerDerivedFromConcreteHandler_IsReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<int>;
            public class QueryHandler : IRequestHandler<Query, int>
            {
                public virtual Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class SpecialQueryHandler : QueryHandler;
            """);

        Assert.Equal(2, diagnostics.WithId(Id).Count);
    }

    // False positive 1: handlers for the same request in different assemblies (one per host, test doubles elsewhere).
    [Fact]
    public async Task HandlersInDifferentAssemblies_AreNotReported()
    {
        var contracts = Compile("Contracts", ["""
            namespace Shop;
            public sealed record Query : IRequest<int>;
            """]);
        string handler = """
            public sealed class QueryHandler : IRequestHandler<Shop.Query, int>
            {
                public Task<int> Handle(Shop.Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """;
        var first = Compile("First", [handler], extraReferences: [contracts.ToMetadataReference()]);
        var second = Compile("Second", [handler], extraReferences: [contracts.ToMetadataReference()]);

        Assert.Empty(await AnalyzeAsync(first));
        Assert.Empty(await AnalyzeAsync(second));
    }

    // False positive 2: decorator (takes the same handler interface in its constructor).
    [Fact]
    public async Task Decorator_IsNotCounted()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<int>;
            public sealed class QueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class CachingQueryHandler(IRequestHandler<Query, int> inner) : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => inner.Handle(request, cancellationToken);
            }
            """);

        Assert.Empty(diagnostics);
    }

    // False positive 3: abstract base and open generic handlers are not registered as such.
    [Fact]
    public async Task AbstractAndOpenGenericHandlers_AreNotCounted()
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
            public sealed class AnyHandler<TRequest> : IRequestHandler<TRequest, int> where TRequest : IRequest<int>
            {
                public Task<int> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """);

        Assert.Empty(diagnostics);
    }

    // False positive 4: only one of the handlers is registered, explicitly, in this compilation.
    [Fact]
    public async Task OneOfTheHandlersRegisteredWithAddRequestHandler_IsNotReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<int>;
            public sealed class QueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class LegacyQueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(0);
            }
            public static class Composition
            {
                public static void Configure(IServiceCollection services) => services.AddRequestHandler<Query, int, QueryHandler>();
            }
            """);

        Assert.Empty(diagnostics);
    }

    // False positive 5: alternative implementations chosen by configuration, each registered explicitly.
    [Fact]
    public async Task AlternativesChosenByConfiguration_AreNotReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Charge(decimal Amount) : IRequest;
            public sealed class RealChargeHandler : IRequestHandler<Charge>
            {
                public Task Handle(Charge request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public sealed class FakeChargeHandler : IRequestHandler<Charge>
            {
                public Task Handle(Charge request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public static class Composition
            {
                public static void Configure(IServiceCollection services, bool sandbox)
                {
                    if (sandbox) services.AddRequestHandler<Charge, FakeChargeHandler>();
                    else services.AddRequestHandler<Charge, RealChargeHandler>();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    // False positive 6: test doubles in a test project, each test registering one with AddRequestHandler.
    [Fact]
    public async Task TestDoublesRegisteredPerTest_AreNotReported()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<int>;
            public sealed class FakeOk : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class FakeFailing : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => throw new InvalidOperationException();
            }
            public static class Tests
            {
                public static void Ok(IServiceCollection services) => services.AddRequestHandler<Query, int, FakeOk>();
                public static void Failing(IServiceCollection services) => services.AddRequestHandler<Query, int, FakeFailing>();
            }
            """);

        Assert.Empty(diagnostics);
    }

    // Remaining false positive: the explicit registration lives in another compilation. Reported, Info by default.
    [Fact]
    public async Task RegistrationInAnotherCompilation_IsStillReported()
    {
        var handlers = Compile("Handlers", ["""
            namespace Shop;
            public sealed record Query : IRequest<int>;
            public sealed class QueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class LegacyQueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(0);
            }
            """]);
        var host = Compile("Host", ["""
            public static class Composition
            {
                public static void Configure(IServiceCollection services) => services.AddRequestHandler<Shop.Query, int, Shop.QueryHandler>();
            }
            """], extraReferences: [handlers.ToMetadataReference()]);

        var reported = (await AnalyzeAsync(handlers)).WithId(Id);
        Assert.Equal(2, reported.Count);
        Assert.All(reported, diagnostic => Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity));
        Assert.Empty(await AnalyzeAsync(host));
    }

    // Registering another request explicitly does not hide a duplicate.
    [Fact]
    public async Task AddRequestHandlerForAnotherRequest_DoesNotHideTheDuplicate()
    {
        var diagnostics = await AnalyzeAsync("""
            public sealed record Query : IRequest<int>;
            public sealed record Other : IRequest;
            public sealed class FirstHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class SecondHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(2);
            }
            public sealed class OtherHandler : IRequestHandler<Other>
            {
                public Task Handle(Other request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public static class Composition
            {
                public static void Configure(IServiceCollection services) => services.AddRequestHandler<Other, OtherHandler>();
            }
            """);

        Assert.Equal(2, diagnostics.WithId(Id).Count);
    }

    // A project that relies on assembly scanning raises the rule.
    [Fact]
    public async Task RaisedToError_IsReportedAsError()
    {
        var reported = (await AnalyzeWithAsync([Id], """
            public sealed record Query : IRequest<int>;
            public sealed class FirstHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class SecondHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(2);
            }
            """)).WithId(Id);

        Assert.Equal(2, reported.Count);
        Assert.All(reported, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    // A project with unregistered duplicates it knows about turns the rule off, explicitly.
    [Fact]
    public async Task SuppressedInProject_IsNotReported()
    {
        var compilation = Compile("Tests", ["""
            public sealed record Query : IRequest<int>;
            public sealed class FakeOk : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class FakeFailing : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => throw new InvalidOperationException();
            }
            """], options: new Dictionary<string, ReportDiagnostic> { [Id] = ReportDiagnostic.Suppress });

        Assert.Empty(await AnalyzeAsync(compilation));
    }

    [Fact]
    public async Task GeneratedDuplicate_IsReportedOnlyOnUserCode()
    {
        var diagnostics = await AnalyzeAsync(
            """
            public sealed record Query : IRequest<int>;
            public sealed class QueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """,
            """
            // <auto-generated/>
            public sealed class GeneratedQueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(2);
            }
            """);

        Assert.Equal("QueryHandler", Assert.Single(diagnostics.WithId(Id)).Text());
    }
}
