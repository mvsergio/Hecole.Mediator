using Microsoft.CodeAnalysis;
using static Hecole.Mediator.Analyzers.Tests.AnalyzerHarness;

namespace Hecole.Mediator.Analyzers.Tests;

/// <summary>HM1001 to HM1005: off by default; a project turns them on with <c>dotnet_diagnostic.ID.severity</c>.</summary>
public sealed class RestrictiveRuleTests
{
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
        public sealed class AnyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
        {
            public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
        }
        public sealed class QueryBehavior : IPipelineBehavior<Query, string>
        {
            public Task<string> Handle(Query request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) => next();
        }
        """;

    // ---- HM1001: assembly scanning ----

    private const string Scanning = """
        public static class Composition
        {
            public static void Configure(IServiceCollection services) => services.AddHecoleMediator(cfg =>
            {
                cfg.RegisterServicesFromAssemblyContaining<Query>();
                cfg.RegisterServicesFromAssemblyContaining(typeof(Query));
                cfg.RegisterServicesFromAssembly(typeof(Query).Assembly);
                cfg.RegisterServicesFromAssemblies(typeof(Query).Assembly);
                Func<System.Reflection.Assembly, Hecole.Mediator.HecoleMediatorConfiguration> scan = cfg.RegisterServicesFromAssembly;
            });
        }
        """;

    [Fact]
    public async Task HM1001_OffByDefault_IsNotReported()
        => Assert.Empty((await AnalyzeAsync(Types, Scanning)).WithId("HM1001"));

    [Fact]
    public async Task HM1001_On_ReportsEveryScanCallAndMethodGroup()
    {
        var reported = (await AnalyzeWithAsync(["HM1001"], Types, Scanning)).WithId("HM1001");

        Assert.Equal(5, reported.Count);
        Assert.All(reported, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        Assert.Equal(
            "'RegisterServicesFromAssemblyContaining' scans an assembly; register handlers explicitly with AddRequestHandler and AddNotificationHandler",
            reported[0].GetMessage());
        Assert.Equal("cfg.RegisterServicesFromAssembly", reported[4].Text());
    }

    [Fact]
    public async Task HM1001_On_ExplicitRegistration_IsNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1001"], Types, """
            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHecoleMediator(cfg => cfg.RequireScope = true);
                    services.AddRequestHandler<Query, string, QueryHandler>();
                    services.AddRequestHandler<Command, CommandHandler>();
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1001"));
    }

    // ---- HM1002: Singleton lifetime ----

    private const string Singleton = """
        public static class Composition
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddHecoleMediator(cfg => cfg.Lifetime = ServiceLifetime.Singleton);
                services.AddHecoleMediator(cfg => cfg.Lifetime = (ServiceLifetime)0);
            }
        }
        """;

    [Fact]
    public async Task HM1002_OffByDefault_IsNotReported()
        => Assert.Empty((await AnalyzeAsync(Types, Singleton)).WithId("HM1002"));

    [Fact]
    public async Task HM1002_On_ReportsConstantSingleton()
    {
        var reported = (await AnalyzeWithAsync(["HM1002"], Types, Singleton)).WithId("HM1002");

        Assert.Equal(["cfg.Lifetime = ServiceLifetime.Singleton", "cfg.Lifetime = (ServiceLifetime)0"], reported.Select(diagnostic => diagnostic.Text()));
    }

    [Fact]
    public async Task HM1002_On_OtherLifetimesAndOtherProperties_AreNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1002"], Types, """
            public sealed class MyOptions { public ServiceLifetime Lifetime { get; set; } }
            public static class Composition
            {
                public static void Configure(IServiceCollection services, ServiceLifetime fromSettings)
                {
                    services.AddHecoleMediator(cfg => cfg.Lifetime = ServiceLifetime.Scoped);
                    services.AddHecoleMediator(cfg => cfg.Lifetime = ServiceLifetime.Transient);
                    services.AddHecoleMediator(cfg => cfg.AddOpenBehavior(typeof(AnyBehavior<,>), ServiceLifetime.Singleton));
                    services.AddSingleton<QueryHandler>();
                    var options = new MyOptions { Lifetime = ServiceLifetime.Singleton };
                    // Not a constant: out of reach of a compile-time rule (documented limit).
                    services.AddHecoleMediator(cfg => cfg.Lifetime = fromSettings);
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1002"));
    }

    // ---- HM1003: pipeline behavior registration ----

    private const string Behaviors = """
        using Microsoft.Extensions.DependencyInjection.Extensions;
        public static class Composition
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddHecoleMediator(cfg =>
                {
                    cfg.AddBehavior<QueryBehavior>();
                    cfg.AddBehavior(typeof(IPipelineBehavior<Query, string>), typeof(QueryBehavior));
                    cfg.AddOpenBehavior(typeof(AnyBehavior<,>));
                    cfg.AddOpenBehaviors([typeof(AnyBehavior<,>)]);
                });
                services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AnyBehavior<,>));
                services.AddScoped<IPipelineBehavior<Query, string>, QueryBehavior>();
                services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(AnyBehavior<,>)));
                services.Add(new ServiceDescriptor(typeof(IPipelineBehavior<Query, string>), typeof(QueryBehavior), ServiceLifetime.Transient));
                ServiceCollectionServiceExtensions.AddSingleton<IPipelineBehavior<Query, string>, QueryBehavior>(services);
            }
        }
        """;

    [Fact]
    public async Task HM1003_OffByDefault_IsNotReported()
        => Assert.Empty((await AnalyzeAsync(Types, Behaviors)).WithId("HM1003"));

    [Fact]
    public async Task HM1003_On_ReportsEveryBehaviorRegistration()
    {
        var reported = (await AnalyzeWithAsync(["HM1003"], Types, Behaviors)).WithId("HM1003");

        Assert.Equal(
            [
                "cfg.AddBehavior<QueryBehavior>()",
                "cfg.AddBehavior(typeof(IPipelineBehavior<Query, string>), typeof(QueryBehavior))",
                "cfg.AddOpenBehavior(typeof(AnyBehavior<,>))",
                "cfg.AddOpenBehaviors([typeof(AnyBehavior<,>)])",
                "services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AnyBehavior<,>))",
                "services.AddScoped<IPipelineBehavior<Query, string>, QueryBehavior>()",
                "ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(AnyBehavior<,>))",
                "new ServiceDescriptor(typeof(IPipelineBehavior<Query, string>), typeof(QueryBehavior), ServiceLifetime.Transient)",
                "ServiceCollectionServiceExtensions.AddSingleton<IPipelineBehavior<Query, string>, QueryBehavior>(services)",
            ],
            reported.Select(diagnostic => diagnostic.Text()));
        Assert.Equal(
            "'AddTransient' registers a pipeline behavior; behaviors are registered only in the composition project",
            reported[4].GetMessage());
    }

    [Fact]
    public async Task HM1003_On_OtherRegistrationsAndBehaviorReads_AreNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1003"], Types, """
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public interface IClock;
            public sealed class Clock : IClock;
            public static class Composition
            {
                public static void Configure(IServiceCollection services, IServiceProvider provider)
                {
                    services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<Query>());
                    services.AddTransient<IClock, Clock>();
                    services.AddTransient(typeof(IClock), typeof(Clock));
                    services.RemoveAll(typeof(IPipelineBehavior<,>));
                    services.RemoveAll<IPipelineBehavior<Query, string>>();
                    var behaviors = provider.GetServices<IPipelineBehavior<Query, string>>();
                    var open = typeof(IPipelineBehavior<,>);
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1003"));
    }

    // ---- HM1004: TaskWhenAllPublisher ----

    private const string Publisher = """
        using Hecole.Mediator.NotificationPublishers;
        using Parallel = Hecole.Mediator.NotificationPublishers.TaskWhenAllPublisher;
        public sealed class MyPublisher : TaskWhenAllPublisher;
        public static class Composition
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddHecoleMediator(cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());
                services.AddHecoleMediator(cfg => cfg.NotificationPublisherType = typeof(Hecole.Mediator.NotificationPublishers.TaskWhenAllPublisher));
                services.AddSingleton<INotificationPublisher, TaskWhenAllPublisher>();
            }
        }
        """;

    [Fact]
    public async Task HM1004_OffByDefault_IsNotReported()
        => Assert.Empty((await AnalyzeAsync(Types, Publisher)).WithId("HM1004"));

    [Fact]
    public async Task HM1004_On_ReportsEveryReference()
    {
        var reported = (await AnalyzeWithAsync(["HM1004"], Types, Publisher)).WithId("HM1004");

        // Alias directive, base type, new, typeof (qualified), generic argument.
        Assert.Equal(5, reported.Count);
        Assert.All(reported, diagnostic => Assert.Equal("TaskWhenAllPublisher", diagnostic.Text()));
    }

    [Fact]
    public async Task HM1004_On_DefaultPublisherNameofDocsAndLookalike_AreNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1004"], Types, """
            using Hecole.Mediator.NotificationPublishers;
            namespace Other
            {
                public sealed class TaskWhenAllPublisher;
            }
            /// <summary>Not <see cref="TaskWhenAllPublisher"/>.</summary>
            public static class Composition
            {
                public static string Name => nameof(TaskWhenAllPublisher);
                public static void Configure(IServiceCollection services)
                {
                    services.AddHecoleMediator(cfg => cfg.NotificationPublisher = new ForeachAwaitPublisher());
                    var lookalike = new Other.TaskWhenAllPublisher();
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1004"));
    }

    // ---- HM1005: request handler without AddRequestHandler ----

    [Fact]
    public async Task HM1005_OffByDefault_IsNotReported()
        => Assert.Empty((await AnalyzeAsync(Types)).WithId("HM1005"));

    [Fact]
    public async Task HM1005_On_UnregisteredHandlers_AreReported()
    {
        var reported = (await AnalyzeWithAsync(["HM1005"], Types, """
            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<Query>());
                    // Registered straight in the container, not with AddRequestHandler.
                    services.AddTransient<IRequestHandler<Command>, CommandHandler>();
                }
            }
            """)).WithId("HM1005");

        Assert.Equal(["QueryHandler", "CommandHandler"], reported.Select(diagnostic => diagnostic.Text()));
        Assert.Equal("'QueryHandler' handles 'Query' but this compilation has no matching AddRequestHandler call", reported[0].GetMessage());
    }

    [Fact]
    public async Task HM1005_On_RegisteredHandlers_AreNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1005"], Types, """
            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHecoleMediator(_ => { });
                    services.AddRequestHandler<Query, string, QueryHandler>();
                    HecoleMediatorServiceCollectionExtensions.AddRequestHandler<Command, CommandHandler>(services);
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1005"));
    }

    [Fact]
    public async Task HM1005_On_HandlerOfTwoRequestsRegisteredForOne_IsReportedForTheOther()
    {
        var reported = (await AnalyzeWithAsync(["HM1005"], """
            public sealed record A : IRequest<int>;
            public sealed record B : IRequest;
            public sealed class Both : IRequestHandler<A, int>, IRequestHandler<B>
            {
                public Task<int> Handle(A request, CancellationToken cancellationToken) => Task.FromResult(1);
                public Task Handle(B request, CancellationToken cancellationToken) => Task.CompletedTask;
            }
            public static class Composition
            {
                public static void Configure(IServiceCollection services) => services.AddRequestHandler<A, int, Both>();
            }
            """)).WithId("HM1005");

        Assert.Equal("'Both' handles 'B' but this compilation has no matching AddRequestHandler call", Assert.Single(reported).GetMessage());
    }

    [Fact]
    public async Task HM1005_On_DecoratorsAbstractAndOpenHandlers_AreNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1005"], """
            public sealed record Query : IRequest<int>;
            public abstract class QueryHandlerBase : IRequestHandler<Query, int>
            {
                public abstract Task<int> Handle(Query request, CancellationToken cancellationToken);
            }
            public sealed class QueryHandler : QueryHandlerBase
            {
                public override Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            public sealed class CachingQueryHandler(IRequestHandler<Query, int> inner) : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => inner.Handle(request, cancellationToken);
            }
            public sealed class AnyHandler<TRequest> : IRequestHandler<TRequest, int> where TRequest : IRequest<int>
            {
                public Task<int> Handle(TRequest request, CancellationToken cancellationToken) => Task.FromResult(0);
            }
            public static class Composition
            {
                public static void Configure(IServiceCollection services) => services.AddRequestHandler<Query, int, QueryHandler>();
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1005"));
    }

    [Fact]
    public async Task HM1005_On_RegistrationInGeneratedCode_Counts()
    {
        var diagnostics = await AnalyzeWithAsync(
            ["HM1005"],
            Types,
            """
            // <auto-generated/>
            public static class GeneratedRegistrations
            {
                public static void Add(IServiceCollection services)
                {
                    services.AddRequestHandler<Query, string, QueryHandler>();
                    services.AddRequestHandler<Command, CommandHandler>();
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1005"));
    }

    // ---- All restrictive rules together ----

    [Fact]
    public async Task AllRestrictiveRules_OffByDefault_ReportNothing()
    {
        var diagnostics = await AnalyzeAsync(Types, Scanning.Replace("Composition", "Scan"), Singleton.Replace("Composition", "Single"),
            Behaviors.Replace("Composition", "Pipeline"), Publisher.Replace("Composition", "Publish"));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id.StartsWith("HM1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RestrictiveRulesInGeneratedCode_AreNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(
            ["HM1001", "HM1002", "HM1003", "HM1004"],
            Types,
            "// <auto-generated/>\n" + Scanning.Replace("Composition", "Scan"),
            "// <auto-generated/>\n" + Singleton.Replace("Composition", "Single"),
            "// <auto-generated/>\n" + Behaviors.Replace("Composition", "Pipeline"),
            "// <auto-generated/>\n" + Publisher.Replace("Composition", "Publish"));

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id.StartsWith("HM1", StringComparison.Ordinal));
    }
}
