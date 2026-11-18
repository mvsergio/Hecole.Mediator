using Microsoft.CodeAnalysis;
using static Hecole.Mediator.Analyzers.Tests.AnalyzerHarness;

namespace Hecole.Mediator.Analyzers.Tests;

/// <summary>HM1006: request handler interface used outside a handler declaration (off by default).</summary>
public sealed class HandlerReferenceTests
{
    private const string Id = "HM1006";

    private const string Types = """
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
        """;

    private const string Uses = """
        public sealed class Caller(IRequestHandler<Query, string> handler)
        {
            private readonly IRequestHandler<Command>? _command = null;
            public IRequestHandler<Query, string>? Property { get; set; }
            public void Run(IServiceProvider provider, IServiceCollection services)
            {
                IRequestHandler<Query, string> local = handler;
                var open = typeof(IRequestHandler<,>);
                var openVoid = typeof(Hecole.Mediator.IRequestHandler<>);
                var resolved = provider.GetService<IRequestHandler<Query, string>>();
                services.AddTransient<IRequestHandler<Command>, CommandHandler>();
                var nested = new List<IEnumerable<IRequestHandler<Query, string>>>();
            }
            public static IRequestHandler<Command> Create() => new CommandHandler();
        }
        """;

    [Fact]
    public async Task OffByDefault_IsNotReported()
        => Assert.Empty((await AnalyzeAsync(Types, Uses)).WithId(Id));

    [Fact]
    public async Task On_ReportsEveryReferenceOutsideAHandlerDeclaration()
    {
        var reported = (await AnalyzeWithAsync([Id], Types, Uses)).WithId(Id);

        Assert.Equal(
            [
                "IRequestHandler<Query, string>",
                "IRequestHandler<Command>",
                "IRequestHandler<Query, string>",
                "IRequestHandler<Query, string>",
                "IRequestHandler<,>",
                "IRequestHandler<>",
                "IRequestHandler<Query, string>",
                "IRequestHandler<Command>",
                "IRequestHandler<Query, string>",
                "IRequestHandler<Command>",
            ],
            reported.Select(diagnostic => diagnostic.Text()));
        Assert.All(reported, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        Assert.Equal(
            "'IRequestHandler<Query, string>' refers to the request handler interface outside a handler declaration; send requests through ISender and register handlers with AddRequestHandler",
            reported[0].GetMessage());
    }

    [Fact]
    public async Task On_UsesThroughAnAlias_AreReported()
    {
        var reported = (await AnalyzeWithAsync([Id], Types, """
            using QueryHandlerAlias = Hecole.Mediator.IRequestHandler<Query, string>;
            public static class Resolver
            {
                public static QueryHandlerAlias? Resolve(IServiceProvider provider) => provider.GetService<QueryHandlerAlias>();
            }
            """)).WithId(Id);

        Assert.Equal(["IRequestHandler<Query, string>", "QueryHandlerAlias", "QueryHandlerAlias"], reported.Select(diagnostic => diagnostic.Text()));
    }

    // Handlers, decorators, explicit implementations, generic constraints and explicit registration are legitimate.
    [Fact]
    public async Task On_HandlerDeclarationsDecoratorsAndRegistration_AreNotReported()
    {
        var diagnostics = await AnalyzeWithAsync([Id], Types, """
            public sealed record Other : IRequest<int>;
            public sealed class ExplicitHandler : IRequestHandler<Other, int>
            {
                Task<int> IRequestHandler<Other, int>.Handle(Other request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public interface IQueryHandler : Hecole.Mediator.IRequestHandler<Query, string>;
            public sealed class CachingQueryHandler : IRequestHandler<Query, string>
            {
                private readonly IRequestHandler<Query, string> _inner;
                public CachingQueryHandler(IRequestHandler<Query, string> inner) => _inner = inner;
                private IRequestHandler<Query, string> Inner => _inner;
                public Task<string> Handle(Query request, CancellationToken cancellationToken) => Inner.Handle(request, cancellationToken);
            }
            public sealed class LoggingCommandHandler(IRequestHandler<Command> inner) : IRequestHandler<Command>
            {
                public Task Handle(Command request, CancellationToken cancellationToken) => inner.Handle(request, cancellationToken);
            }
            /// <summary>Wraps <see cref="IRequestHandler{TRequest, TResponse}"/>.</summary>
            public static class Registration
            {
                private static IServiceCollection Feature<TRequest, TResponse, THandler>(this IServiceCollection services)
                    where TRequest : IRequest<TResponse>
                    where THandler : class, IRequestHandler<TRequest, TResponse>
                    => services.AddRequestHandler<TRequest, TResponse, THandler>();

                public static void Add(IServiceCollection services)
                {
                    services.Feature<Query, string, QueryHandler>();
                    services.AddRequestHandler<Command, CommandHandler>();
                    services.AddRequestHandler<Other, int, ExplicitHandler>();
                }
            }
            """);

        Assert.Empty(diagnostics.WithId(Id));
    }

    // Only a public constructor makes a decorator: a non-public one taking the interface is an ordinary use. A field of the
    // interface in a class that is not a decorator is reported as well.
    [Fact]
    public async Task On_NonPublicConstructorParameterAndNonDecoratorField_AreReported()
    {
        var reported = (await AnalyzeWithAsync([Id], Types, """
            public sealed class FakeDecorator : IRequestHandler<Command>
            {
                private readonly IRequestHandler<Command>? _unused;
                public FakeDecorator() { }
                private FakeDecorator(IRequestHandler<Command> unused) => _unused = unused;
                public Task Handle(Command request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public sealed class WrongInterface(IRequestHandler<Query, string> inner) : IRequestHandler<Command>
            {
                public Task Handle(Command request, CancellationToken cancellationToken) => inner.Handle(new Query(), cancellationToken);
            }
            """)).WithId(Id);

        Assert.Equal(["IRequestHandler<Command>", "IRequestHandler<Command>", "IRequestHandler<Query, string>"], reported.Select(diagnostic => diagnostic.Text()));
    }

    [Fact]
    public async Task On_InGeneratedCode_IsReported()
    {
        var reported = (await AnalyzeWithAsync([Id], Types, """
            // <auto-generated/>
            public static class Generated
            {
                public static void Add(IServiceCollection services) => services.AddScoped<IRequestHandler<Query, string>, QueryHandler>();
            }
            """)).WithId(Id);

        Assert.Equal("IRequestHandler<Query, string>", Assert.Single(reported).Text());
    }
}
