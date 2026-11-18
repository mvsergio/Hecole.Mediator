using Microsoft.CodeAnalysis;
using static Hecole.Mediator.Analyzers.Tests.AnalyzerHarness;

namespace Hecole.Mediator.Analyzers.Tests;

/// <summary>HM1001 to HM1006: off by default; a project turns them on with <c>dotnet_diagnostic.ID.severity</c>.</summary>
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
                    services.AddHecoleMediator(cfg => (cfg.Lifetime, _) = (ServiceLifetime.Scoped, 0));
                    services.AddHecoleMediator(cfg => cfg.Lifetime = (ServiceLifetime)1);
                    var other = new MyOptions();
                    (other.Lifetime, _) = (fromSettings, 0);
                    other.Lifetime = fromSettings;
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1002"));
    }

    private const string UnprovenLifetime = "HecoleMediatorConfiguration.Lifetime is set to a value that cannot be proven not to be "
        + "ServiceLifetime.Singleton; assign the constant ServiceLifetime.Transient or ServiceLifetime.Scoped";

    // Only a constant Transient or Scoped is accepted: a variable, a value read from settings, a deconstruction target and
    // the compound forms are reported (the value cannot be proven not to be Singleton).
    [Fact]
    public async Task HM1002_On_ValuesNotProvenNotSingleton_AreReported()
    {
        var reported = (await AnalyzeWithAsync(["HM1002"], Types, """
            public static class Composition
            {
                public static void Configure(IServiceCollection services, ServiceLifetime fromSettings)
                {
                    services.AddHecoleMediator(cfg => cfg.Lifetime = fromSettings);
                    services.AddHecoleMediator(cfg =>
                    {
                        var lifetime = ServiceLifetime.Singleton;
                        cfg.Lifetime = lifetime;
                    });
                    services.AddHecoleMediator(cfg => (cfg.Lifetime, _) = (ServiceLifetime.Singleton, 0));
                    services.AddHecoleMediator(cfg => (cfg.Lifetime, _) = (fromSettings, 0));
                    services.AddHecoleMediator(cfg => cfg.Lifetime |= ServiceLifetime.Scoped);
                    services.AddHecoleMediator(cfg => cfg.Lifetime++);
                    var configuration = new HecoleMediatorConfiguration { Lifetime = fromSettings };
                }
            }
            """)).WithId("HM1002");

        Assert.Equal(
            [
                "cfg.Lifetime = fromSettings",
                "cfg.Lifetime = lifetime",
                "cfg.Lifetime",
                "cfg.Lifetime",
                "cfg.Lifetime |= ServiceLifetime.Scoped",
                "cfg.Lifetime++",
                "Lifetime = fromSettings",
            ],
            reported.Select(diagnostic => diagnostic.Text()));
        Assert.Equal(UnprovenLifetime, reported[0].GetMessage());
        Assert.Equal("HecoleMediatorConfiguration.Lifetime is set to ServiceLifetime.Singleton", reported[2].GetMessage());
        Assert.Equal(UnprovenLifetime, reported[3].GetMessage());
    }

    // ---- HM1001, HM1002, HM1003: configuration used through dynamic ----

    private const string DynamicConfiguration = """
        public static class Composition
        {
            private static void Use(dynamic configuration) => configuration.RegisterServicesFromAssembly(typeof(Query).Assembly);

            public static void Configure(IServiceCollection services)
            {
                services.AddHecoleMediator(cfg =>
                {
                    dynamic late = cfg;
                    late.Lifetime = ServiceLifetime.Singleton;
                });
                services.AddHecoleMediator(cfg => ((dynamic)cfg).AddOpenBehavior(typeof(object)));
                services.AddHecoleMediator(cfg => Use(cfg));
                services.AddHecoleMediator(cfg => { object plain = cfg; });
            }
        }
        """;

    [Fact]
    public async Task DynamicConfiguration_On_IsReportedUnderEachRuleThatIsOn()
    {
        var reported = (await AnalyzeWithAsync(["HM1001", "HM1002", "HM1003"], Types, DynamicConfiguration))
            .Where(diagnostic => diagnostic.GetMessage().Contains("dynamic", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(
            [
                "HM1001 cfg", "HM1002 cfg", "HM1003 cfg",
                "HM1001 (dynamic)cfg", "HM1002 (dynamic)cfg", "HM1003 (dynamic)cfg",
                "HM1001 cfg", "HM1002 cfg", "HM1003 cfg",
            ],
            reported.Select(diagnostic => $"{diagnostic.Id} {diagnostic.Text()}"));
        Assert.Equal(
            "HecoleMediatorConfiguration is used through dynamic, so assembly scanning calls cannot be seen; register handlers explicitly with AddRequestHandler and AddNotificationHandler",
            reported[0].GetMessage());
        Assert.Equal("HecoleMediatorConfiguration is used through dynamic, so the Lifetime it receives cannot be seen", reported[1].GetMessage());
        Assert.Equal(
            "HecoleMediatorConfiguration is used through dynamic, so behavior registrations cannot be seen; behaviors are registered only in the composition project",
            reported[2].GetMessage());
    }

    [Fact]
    public async Task DynamicConfiguration_IsReportedOnlyUnderTheRulesThatAreOn()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1002"], Types, DynamicConfiguration);

        Assert.Equal(["cfg", "(dynamic)cfg", "cfg"], diagnostics.WithId("HM1002").Select(diagnostic => diagnostic.Text()));
        Assert.Empty(diagnostics.WithId("HM1001"));
        Assert.Empty(diagnostics.WithId("HM1003"));
        Assert.DoesNotContain(await AnalyzeAsync(Types, DynamicConfiguration), diagnostic => diagnostic.Id.StartsWith("HM1", StringComparison.Ordinal));
    }

    // ---- HM1003: pipeline behaviors outside the composition project ----

    // The behaviors live in a shared assembly; the module compiled with HM1003 on only references them.
    private static async Task<IReadOnlyList<Diagnostic>> AnalyzeModuleAsync(string[] enabledIds, params string[] sources)
    {
        var shared = Compile("Shared", [Types]);
        var module = Compile(
            "Module",
            sources,
            extraReferences: [shared.ToMetadataReference()],
            options: enabledIds.ToDictionary(id => id, _ => ReportDiagnostic.Error));
        return await AnalyzeAsync(module);
    }

    private const string BehaviorSuffix = "; behaviors are registered only in the composition project";

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
    public async Task HM1003_On_ReportsConfigurationCallsAndEveryTypeReference()
    {
        var reported = (await AnalyzeModuleAsync(["HM1003"], Behaviors)).WithId("HM1003");

        // A configuration call is reported once, as a call; elsewhere each typeof and each type argument naming a behavior
        // is its own site.
        Assert.Equal(
            [
                "cfg.AddBehavior<QueryBehavior>()",
                "cfg.AddBehavior(typeof(IPipelineBehavior<Query, string>), typeof(QueryBehavior))",
                "cfg.AddOpenBehavior(typeof(AnyBehavior<,>))",
                "cfg.AddOpenBehaviors([typeof(AnyBehavior<,>)])",
                "typeof(IPipelineBehavior<,>)", "typeof(AnyBehavior<,>)",
                "IPipelineBehavior<Query, string>", "QueryBehavior",
                "typeof(IPipelineBehavior<,>)", "typeof(AnyBehavior<,>)",
                "typeof(IPipelineBehavior<Query, string>)", "typeof(QueryBehavior)",
                "IPipelineBehavior<Query, string>", "QueryBehavior",
            ],
            reported.Select(diagnostic => diagnostic.Text()));
        Assert.Equal("'AddBehavior' registers a pipeline behavior" + BehaviorSuffix, reported[0].GetMessage());
        Assert.Equal("'typeof(IPipelineBehavior<,>)' refers to a pipeline behavior type" + BehaviorSuffix, reported[4].GetMessage());
    }

    // Forms that recognizing the shape of a call missed: every one is a reference to the behavior symbol.
    [Fact]
    public async Task HM1003_On_ReportsBehaviorReferencesInAnyPosition()
    {
        var reported = (await AnalyzeModuleAsync(["HM1003"], """
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public static class Helper
            {
                public static void Put<TService, TImplementation>(IServiceCollection services)
                    where TService : class
                    where TImplementation : class, TService
                    => services.AddTransient<TService, TImplementation>();
            }
            public static class Module
            {
                public static void Configure(IServiceCollection services, IServiceProvider provider)
                {
                    services.AddSingleton<IEnumerable<IPipelineBehavior<Query, string>>>(Array.Empty<IPipelineBehavior<Query, string>>());
                    Helper.Put<IPipelineBehavior<Query, string>, QueryBehavior>(services);
                    services.AddTransient(typeof(IPipelineBehavior<,>).MakeGenericType(typeof(Query), typeof(string)), typeof(QueryBehavior));
                    var service = typeof(IPipelineBehavior<,>);
                    var implementation = typeof(AnyBehavior<,>);
                    services.Insert(0, new ServiceDescriptor(service, implementation, ServiceLifetime.Transient));
                    services.RemoveAll(typeof(IPipelineBehavior<,>));
                    services.RemoveAll<IPipelineBehavior<Query, string>>();
                    var behaviors = provider.GetServices<IPipelineBehavior<Query, string>>();
                    List<(int, AnyBehavior<Query, string>)> nested = [];
                }
            }
            """)).WithId("HM1003");

        Assert.Equal(
            [
                "IEnumerable<IPipelineBehavior<Query, string>>", "IPipelineBehavior<Query, string>",
                "IPipelineBehavior<Query, string>", "QueryBehavior",
                "typeof(IPipelineBehavior<,>)", "typeof(QueryBehavior)",
                "typeof(IPipelineBehavior<,>)",
                "typeof(AnyBehavior<,>)",
                "typeof(IPipelineBehavior<,>)",
                "IPipelineBehavior<Query, string>",
                "IPipelineBehavior<Query, string>",
                "(int, AnyBehavior<Query, string>)",
            ],
            reported.Select(diagnostic => diagnostic.Text()));
    }

    [Fact]
    public async Task HM1003_On_BehaviorDeclaration_IsReportedOnce()
    {
        var reported = (await AnalyzeModuleAsync(["HM1003"], """
            public interface IAuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull;
            public sealed class ModuleBehavior : IPipelineBehavior<Query, string>
            {
                private readonly List<ModuleBehavior> _chain = [];
                public Type Self => typeof(ModuleBehavior);
                public Task<string> Handle(Query request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) => next();
            }
            public class Derived : AnyBehaviorBase;
            public abstract class AnyBehaviorBase : IPipelineBehavior<Query, string>
            {
                public Task<string> Handle(Query request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) => next();
            }
            """)).WithId("HM1003");

        Assert.Equal(["IAuditBehavior", "ModuleBehavior", "Derived", "AnyBehaviorBase"], reported.Select(diagnostic => diagnostic.Text()));
        Assert.Equal("'ModuleBehavior' declares a pipeline behavior" + BehaviorSuffix, reported[1].GetMessage());
    }

    [Fact]
    public async Task HM1003_On_OtherRegistrationsAndTypes_AreNotReported()
    {
        var diagnostics = await AnalyzeModuleAsync(["HM1003"], """
            using Microsoft.Extensions.DependencyInjection.Extensions;
            public interface IClock;
            public sealed class Clock : IClock;
            /// <summary>Uses <see cref="IPipelineBehavior{TRequest, TResponse}"/> in documentation only.</summary>
            public static class Module
            {
                public static string Name => nameof(QueryBehavior);
                public static void Configure(IServiceCollection services)
                {
                    services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<Query>());
                    services.AddTransient<IClock, Clock>();
                    services.AddTransient(typeof(IClock), typeof(Clock));
                    services.AddRequestHandler<Query, string, QueryHandler>();
                    var types = new List<Type> { typeof(Query), typeof(List<Clock>) };
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1003"));
    }

    // The composition project (rule off) declares and registers behaviors freely.
    [Fact]
    public async Task HM1003_Off_BehaviorDeclarationAndRegistration_AreNotReported()
    {
        var diagnostics = await AnalyzeAsync(Types, Behaviors, """
            public sealed class HostBehavior : IPipelineBehavior<Query, string>
            {
                public Task<string> Handle(Query request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) => next();
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1003"));
    }

    // Turned on for some files only (.editorconfig of a module folder): reported only in those files.
    [Fact]
    public async Task HM1003_OnInOneFileOnly_IsReportedOnlyThere()
    {
        const string module = """
            // module
            public static class Module
            {
                public static void Configure(IServiceCollection services) => services.AddScoped<IPipelineBehavior<Query, string>, QueryBehavior>();
            }
            """;
        var compilation = Compile(
            "Sample",
            [Types, module, Behaviors],
            treeOptions: new PerTreeOptions("HM1003", tree => tree.ToString().StartsWith("// module", StringComparison.Ordinal)));

        var reported = (await AnalyzeAsync(compilation)).WithId("HM1003");

        Assert.Equal(["IPipelineBehavior<Query, string>", "QueryBehavior"], reported.Select(diagnostic => diagnostic.Text()));
        Assert.All(reported, diagnostic => Assert.Equal(module, diagnostic.Location.SourceTree!.ToString()));
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

        // Alias directive, base type, new, typeof (qualified), generic argument: every occurrence of the name in the
        // sample, each reported once, at its own position.
        var occurrences = new List<int>();
        for (var at = Publisher.IndexOf("TaskWhenAllPublisher", StringComparison.Ordinal); at >= 0;
             at = Publisher.IndexOf("TaskWhenAllPublisher", at + 1, StringComparison.Ordinal))
        {
            occurrences.Add(at);
        }

        Assert.Equal(5, occurrences.Count);
        Assert.All(reported, diagnostic => Assert.Equal(Publisher, diagnostic.Location.SourceTree!.GetText().ToString()));
        Assert.Equal(occurrences, reported.Select(diagnostic => diagnostic.Location.SourceSpan.Start));
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

    // A global alias declared in another file (here marked as generated) and a file-level alias: the declaration and every
    // use of the alias are reported.
    [Fact]
    public async Task HM1004_On_UsesThroughAliases_AreReported()
    {
        var reported = (await AnalyzeWithAsync(
            ["HM1004"],
            Types,
            """
            // <auto-generated/>
            global using ParallelAlias = Hecole.Mediator.NotificationPublishers.TaskWhenAllPublisher;
            """,
            """
            using Local = Hecole.Mediator.NotificationPublishers.TaskWhenAllPublisher;
            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHecoleMediator(cfg => cfg.NotificationPublisher = new ParallelAlias());
                    services.AddHecoleMediator(cfg => cfg.NotificationPublisherType = typeof(Local));
                    services.AddSingleton<INotificationPublisher, ParallelAlias>();
                }
            }
            """)).WithId("HM1004");

        Assert.Equal(
            ["Local", "ParallelAlias", "ParallelAlias", "TaskWhenAllPublisher", "TaskWhenAllPublisher"],
            reported.Select(diagnostic => diagnostic.Text()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task HM1004_On_AliasOfAnotherType_IsNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1004"], Types, """
            using Publisher = Hecole.Mediator.NotificationPublishers.ForeachAwaitPublisher;
            using TaskWhenAllPublisher = System.DateTime;
            public static class Composition
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddHecoleMediator(cfg => cfg.NotificationPublisher = new Publisher());
                    var now = TaskWhenAllPublisher.Now;
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
    public async Task HM1005_On_HandlerWithOnlyNonPublicConstructorTakingTheInterface_IsReported()
    {
        var reported = (await AnalyzeWithAsync(["HM1005"], """
            public sealed record Query : IRequest<int>;
            public sealed class QueryHandler : IRequestHandler<Query, int>
            {
                public QueryHandler() { }
                private QueryHandler(IRequestHandler<Query, int> unused) { }
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """)).WithId("HM1005");

        Assert.Equal("QueryHandler", Assert.Single(reported).Text());
    }

    // One level of forwarding: a generic helper whose body passes its own type parameters to AddRequestHandler registers
    // the type arguments of each call.
    [Fact]
    public async Task HM1005_On_RegistrationThroughGenericHelper_IsNotReported()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1005"], Types, """
            public static class Registration
            {
                private static IServiceCollection Feature<TRequest, TResponse, THandler>(this IServiceCollection services)
                    where TRequest : IRequest<TResponse>
                    where THandler : class, IRequestHandler<TRequest, TResponse>
                    => services.AddRequestHandler<TRequest, TResponse, THandler>();

                private static void Command<THandler>(IServiceCollection services) where THandler : class, IRequestHandler<Command>
                {
                    services.AddRequestHandler<Command, THandler>();
                }

                public static void Add(IServiceCollection services)
                {
                    services.Feature<Query, string, QueryHandler>();
                    Command<CommandHandler>(services);
                }
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1005"));
    }

    [Fact]
    public async Task HM1005_On_GenericHelperCalledForAnotherHandler_IsReported()
    {
        var reported = (await AnalyzeWithAsync(["HM1005"], Types, """
            public sealed class OtherQueryHandler : IRequestHandler<Query, string>
            {
                public Task<string> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult("other");
            }
            public static class Registration
            {
                private static IServiceCollection Feature<TRequest, TResponse, THandler>(this IServiceCollection services)
                    where TRequest : IRequest<TResponse>
                    where THandler : class, IRequestHandler<TRequest, TResponse>
                    => services.AddRequestHandler<TRequest, TResponse, THandler>();

                // Two levels of forwarding are not followed.
                private static IServiceCollection Outer<TRequest, TResponse, THandler>(this IServiceCollection services)
                    where TRequest : IRequest<TResponse>
                    where THandler : class, IRequestHandler<TRequest, TResponse>
                    => services.Feature<TRequest, TResponse, THandler>();

                public static void Add(IServiceCollection services)
                {
                    services.Feature<Query, string, QueryHandler>();
                    services.Outer<Query, string, OtherQueryHandler>();
                    services.AddRequestHandler<Command, CommandHandler>();
                }
            }
            """)).WithId("HM1005");

        Assert.Equal("OtherQueryHandler", Assert.Single(reported).Text());
    }

    // HM1005 does not see reachability: an AddRequestHandler call in dead code counts, while the real registration is made
    // directly in the container. HM1006 reports that registration.
    [Fact]
    public async Task HM1005_On_RegistrationInDeadCode_CountsAndHM1006ReportsTheDirectRegistration()
    {
        var diagnostics = await AnalyzeWithAsync(["HM1005", "HM1006"], """
            public sealed record Query : IRequest<string>;
            public sealed class QueryHandler : IRequestHandler<Query, string>
            {
                public Task<string> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult("");
            }
            public static class Registration
            {
                public static void Add(IServiceCollection services) => services.AddTransient<IRequestHandler<Query, string>, QueryHandler>();

                private static void NeverCalled(IServiceCollection services) => services.AddRequestHandler<Query, string, QueryHandler>();
            }
            """);

        Assert.Empty(diagnostics.WithId("HM1005"));
        Assert.Equal("IRequestHandler<Query, string>", Assert.Single(diagnostics.WithId("HM1006")).Text());
    }

    [Fact]
    public async Task HM1005_On_UnregisteredHandlerInGeneratedCode_IsReported()
    {
        var reported = (await AnalyzeWithAsync(
            ["HM1005"],
            """
            // <auto-generated/>
            public sealed record Query : IRequest<int>;
            public sealed class QueryHandler : IRequestHandler<Query, int>
            {
                public Task<int> Handle(Query request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """)).WithId("HM1005");

        Assert.Equal("QueryHandler", Assert.Single(reported).Text());
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

    // A generated-code header must not silence a restrictive rule that is on: the same violations are reported as in
    // hand-written code.
    [Fact]
    public async Task RestrictiveRulesInGeneratedCode_AreReported()
    {
        string[] ids = ["HM1001", "HM1002", "HM1003", "HM1004", "HM1005", "HM1006"];
        string[] sources =
        [
            Types,
            Scanning.Replace("Composition", "Scan"),
            Singleton.Replace("Composition", "Single"),
            Behaviors.Replace("Composition", "Pipeline"),
            Publisher.Replace("Composition", "Publish"),
            """
            public static class Direct
            {
                public static void Add(IServiceCollection services) => services.AddTransient<IRequestHandler<Query, string>, QueryHandler>();
            }
            """,
        ];


        var handWritten = await AnalyzeWithAsync(ids, sources);
        var generated = await AnalyzeWithAsync(ids, [.. sources.Select(source => "// <auto-generated/>\n" + source)]);

        var expected = Summary(handWritten);
        Assert.Equal(expected, Summary(generated));
        Assert.All(ids, id => Assert.Contains(expected, entry => entry.StartsWith(id, StringComparison.Ordinal)));

        static List<string> Summary(IEnumerable<Diagnostic> diagnostics)
            => [.. diagnostics.Where(d => d.Id.StartsWith("HM1", StringComparison.Ordinal)).Select(d => $"{d.Id} {d.Text()}").Order(StringComparer.Ordinal)];
    }
}
