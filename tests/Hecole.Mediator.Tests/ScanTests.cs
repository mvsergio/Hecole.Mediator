using System.Reflection;
using Hecole.Mediator.Internal;
using Hecole.Mediator.Tests.Fakes;
using Hecole.Mediator.Tests.ScanConflict;
using Hecole.Mediator.Tests.ScanTarget;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

/// <summary>
/// <c>RegisterServicesFrom...</c>: handlers of the assemblies given, transient, no duplicates, conflicts detected.
/// The scanned assembly is <c>Hecole.Mediator.Tests.ScanTarget</c>; <c>ScanConflict</c> has a second handler for one of its requests.
/// </summary>
public class ScanTests
{
    private static readonly Assembly Target = typeof(ScanQuery).Assembly;
    private static readonly Assembly Conflict = typeof(ConflictingScanQueryHandler).Assembly;

    private static ServiceProvider Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ScanRecorder>();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static TheoryData<string> EntryPoints => ["Assembly", "Assemblies", "ContainingT", "ContainingType"];

    private static void Register(HecoleMediatorConfiguration cfg, string entryPoint)
    {
        switch (entryPoint)
        {
            case "Assembly": cfg.RegisterServicesFromAssembly(Target); break;
            case "Assemblies": cfg.RegisterServicesFromAssemblies(Target); break;
            case "ContainingT": cfg.RegisterServicesFromAssemblyContaining<ScanQuery>(); break;
            case "ContainingType": cfg.RegisterServicesFromAssemblyContaining(typeof(ScanNote)); break;
        }
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public async Task Scan_RegistersRequestHandlerVoidHandlerAndNotificationHandlers(string entryPoint)
    {
        using var root = Build(s => s.AddHecoleMediator(cfg => Register(cfg, entryPoint)));
        using var scope = root.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var recorder = root.GetRequiredService<ScanRecorder>();

        Assert.Equal("scan:7", await mediator.Send(new ScanQuery(7)));
        await mediator.Send(new ScanCommand("a"));
        await mediator.Publish(new ScanNote("n"));

        Assert.Equal(["audit:n", "command:a", "note:n"], recorder.Entries.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Scan_RegistersHandlersAsTransient_AndRequestHandlersEnterTheCatalog()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target));

        var handlers = services.Where(d =>
            d.ServiceType.IsGenericType
            && (d.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                || d.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<>)
                || d.ServiceType.GetGenericTypeDefinition() == typeof(INotificationHandler<>))).ToList();

        Assert.Equal(4, handlers.Count);
        Assert.All(handlers, d => Assert.Equal(ServiceLifetime.Transient, d.Lifetime));

        using var root = services.AddSingleton<ScanRecorder>().BuildServiceProvider();
        Assert.Equal(2, root.GetRequiredService<MediatorRuntime>().Catalog.Count);
    }

    [Fact]
    public void Scan_SkipsAbstractAndOpenGenericTypes_AndDoesNotRegisterBehaviors()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target));

        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(AbstractScanSkippedHandler));
        Assert.DoesNotContain(services, d => d.ImplementationType == typeof(OpenGenericScanHandler<>));
        Assert.DoesNotContain(services, d =>
            d.ServiceType == typeof(IPipelineBehavior<,>)
            || (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)));
    }

    [Fact]
    public void AddHecoleMediator_WithoutAssemblies_IsValid()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });

        Assert.DoesNotContain(services, d => d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
    }

    [Fact]
    public async Task Scan_SameAssemblyTwiceInOneCall_RegistersOnce()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg
            .RegisterServicesFromAssembly(Target)
            .RegisterServicesFromAssemblyContaining<ScanQuery>()
            .RegisterServicesFromAssemblies(Target, Target));

        Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<ScanQuery, string>));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(INotificationHandler<ScanNote>)));

        using var root = services.AddSingleton<ScanRecorder>().BuildServiceProvider();
        using var scope = root.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(new ScanNote("x"));

        Assert.Equal(["audit:x", "note:x"], root.GetRequiredService<ScanRecorder>().Entries.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Scan_SameAssemblyInTwoAddHecoleMediatorCalls_NotificationHandledOnce()
    {
        using var root = Build(s =>
        {
            s.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target));
            s.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<ScanNote>());
        });
        using var scope = root.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(new ScanNote("once"));

        Assert.Equal(["audit:once", "note:once"], root.GetRequiredService<ScanRecorder>().Entries.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Scan_NotificationHandlerAlreadyInContainer_IsNotAddedAgain()
    {
        var services = new ServiceCollection();
        services.AddTransient<INotificationHandler<ScanNote>, ScanNoteHandler>();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target));

        Assert.Single(services, d => d.ServiceType == typeof(INotificationHandler<ScanNote>) && d.ImplementationType == typeof(ScanNoteHandler));
    }

    [Fact]
    public async Task AddHecoleMediator_OncePerModule_RegistersEachModuleAndTheMediatorOnce()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ScanRecorder>();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target));
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<TestQuery, TestResult, TestQueryHandler>();

        Assert.Single(services, d => d.ServiceType == typeof(IMediator));
        Assert.Single(services, d => d.ServiceType == typeof(ISender));
        Assert.Single(services, d => d.ServiceType == typeof(IPublisher));
        Assert.Single(services, d => d.ServiceType == typeof(MediatorRuntime));

        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        Assert.Equal("scan:1", await mediator.Send(new ScanQuery(1)));
        Assert.Equal("Query: 2", (await mediator.Send(new TestQuery(2))).Value);
    }

    [Fact]
    public void AddHecoleMediator_SecondCallWithDifferentLifetime_ThrowsNamingBothValues()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.Lifetime = ServiceLifetime.Scoped);

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target)));

        Assert.Contains("Lifetime = Scoped", ex.Message);
        Assert.Contains("Transient", ex.Message);
    }

    [Fact]
    public void AddHecoleMediator_SecondCallWithDifferentRequireScope_Throws()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RequireScope = true);

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddHecoleMediator(_ => { }));

        Assert.Contains(nameof(HecoleMediatorConfiguration.RequireScope), ex.Message);
    }

    [Fact]
    public void AddHecoleMediator_SecondCallWithSameOptions_IsAccepted()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => { cfg.Lifetime = ServiceLifetime.Scoped; cfg.RequireScope = true; });
        services.AddHecoleMediator(cfg => { cfg.Lifetime = ServiceLifetime.Scoped; cfg.RequireScope = true; });

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IMediator)).Lifetime);
    }

    [Fact]
    public void Scan_TwoAssembliesWithDifferentHandlersForSameRequest_ThrowsWithBothNames()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblies(Target, Conflict)));

        Assert.Contains(typeof(ScanQuery).FullName!, ex.Message);
        Assert.Contains(typeof(ScanQueryHandler).FullName!, ex.Message);
        Assert.Contains(typeof(ConflictingScanQueryHandler).FullName!, ex.Message);
    }

    [Fact]
    public void Scan_ThenAddRequestHandlerWithDifferentHandler_ThrowsWithBothNames()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddRequestHandler<ScanQuery, string, ConflictingScanQueryHandler>());

        Assert.Contains(typeof(ScanQueryHandler).FullName!, ex.Message);
        Assert.Contains(typeof(ConflictingScanQueryHandler).FullName!, ex.Message);
    }

    [Fact]
    public void AddRequestHandler_ThenScanWithDifferentHandler_ThrowsWithBothNames()
    {
        var services = new ServiceCollection();
        services.AddRequestHandler<ScanQuery, string, ConflictingScanQueryHandler>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target)));

        Assert.Contains(typeof(ScanQueryHandler).FullName!, ex.Message);
        Assert.Contains(typeof(ConflictingScanQueryHandler).FullName!, ex.Message);
    }

    [Fact]
    public async Task SameHandlerByScanAndByAddRequestHandler_IsNotAnError_AndRegisteredOnce()
    {
        var before = new ServiceCollection();
        before.AddRequestHandler<ScanQuery, string, ScanQueryHandler>();
        before.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target));

        var after = new ServiceCollection();
        after.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(Target));
        after.AddRequestHandler<ScanQuery, string, ScanQueryHandler>();

        foreach (var services in new[] { before, after })
        {
            Assert.Single(services, d => d.ServiceType == typeof(IRequestHandler<ScanQuery, string>));

            using var root = services.AddSingleton<ScanRecorder>().BuildServiceProvider();
            using var scope = root.CreateScope();
            Assert.Equal("scan:3", await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ScanQuery(3)));
        }
    }

    [Fact]
    public async Task Scan_AssemblyWithTypesThatFailToLoad_ScansTheLoadableOnes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ScanRecorder>();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(new PartiallyLoadableAssembly()));

        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();

        Assert.Equal("scan:5", await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ScanQuery(5)));
    }

    [Fact]
    public void RegisterServicesFrom_NullArguments_Throw()
    {
        var cfg = new HecoleMediatorConfiguration();

        Assert.Throws<ArgumentNullException>(() => cfg.RegisterServicesFromAssembly(null!));
        Assert.Throws<ArgumentNullException>(() => cfg.RegisterServicesFromAssemblies(null!));
        Assert.Throws<ArgumentNullException>(() => cfg.RegisterServicesFromAssemblyContaining(null!));
    }

    /// <summary>An assembly whose <c>GetTypes</c> fails for one type, as when a dependency is missing.</summary>
    private sealed class PartiallyLoadableAssembly : Assembly
    {
        public override Type[] GetTypes()
            => throw new ReflectionTypeLoadException([typeof(ScanQueryHandler), null], [new TypeLoadException("missing dependency")]);
    }
}
