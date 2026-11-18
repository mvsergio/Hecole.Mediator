using Hecole.Mediator.Internal;
using Hecole.Mediator.NotificationPublishers;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hecole.Mediator.Tests;

/// <summary>
/// 2.0.1, lot 4: integrity of the library's own registrations (the registry out of reach of a simple removal, the
/// recorded descriptors checked by <c>VerifyHecoleMediator</c>, verification against what the provider has).
/// </summary>
public class OwnRegistrationTests
{
    private const string InternalState = "the mediator's internal state";

    private static ServiceProvider Build(Action<IServiceCollection> configure, Action<HecoleMediatorConfiguration>? mediator = null)
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(mediator ?? (_ => { }));
        configure(services);
        return services.BuildServiceProvider();
    }

    private static string SingleProblem(IServiceProvider provider)
        => Assert.Single(Assert.Throws<MediatorConfigurationException>(() => provider.VerifyHecoleMediator()).Problems);

    // ---- Item 1: the registry stays associated with the collection --------------------------------------------------

    [Fact]
    public void RegistryDescriptorRemoved_AddHecoleMediatorWithOtherOptions_Throws()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.RemoveAll<HandlerRegistry>();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddHecoleMediator(cfg => cfg.RequireScope = true));

        Assert.Contains("already called with", ex.Message);
        Assert.Single(services, d => d.ServiceType == typeof(IMediator));
        Assert.Single(services, d => d.ServiceType == typeof(MediatorRuntime));
    }

    [Fact]
    public void RegistryDescriptorRemoved_AddHecoleMediatorWithSameOptions_ChangesNothingAndPutsTheDescriptorBack()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        var registry = HandlerRegistry.GetOrAdd(services);
        var before = services.Where(d => d.ServiceType != typeof(HandlerRegistry)).ToList();
        services.RemoveAll<HandlerRegistry>();

        services.AddHecoleMediator(_ => { });

        Assert.Equal(before, services.Where(d => d.ServiceType != typeof(HandlerRegistry)));
        Assert.Same(registry.Descriptor, Assert.Single(services, d => d.ServiceType == typeof(HandlerRegistry)));
        using var provider = services.BuildServiceProvider();
        provider.VerifyHecoleMediator();
    }

    [Fact]
    public void CopiedCollection_AdoptsTheRegistryFoundAmongItsDescriptors_AndKeepsItAfterTheDescriptorIsRemoved()
    {
        var original = new ServiceCollection();
        original.AddHecoleMediator(_ => { });
        var copy = new ServiceCollection();
        foreach (var descriptor in original)
            copy.Add(descriptor);

        Assert.Same(HandlerRegistry.GetOrAdd(original), HandlerRegistry.GetOrAdd(copy));

        copy.RemoveAll<HandlerRegistry>();
        Assert.Throws<InvalidOperationException>(() => copy.AddHecoleMediator(cfg => cfg.RequireScope = true));
        Assert.Single(copy, d => d.ServiceType == typeof(IMediator));
    }

    // ---- Items 2 and 3: the library's own services must be the registrations in effect -------------------------------

    private static readonly Dictionary<string, (Action<IServiceCollection> Change, string Service, string Registration)> Changes = new()
    {
        ["ISender typed"] = (s => s.AddTransient<ISender, FakeMediator>(), typeof(ISender).FullName!, $"'{typeof(FakeMediator).FullName}'"),
        ["ISender instance"] = (s => s.AddSingleton<ISender>(new FakeMediator()), typeof(ISender).FullName!, $"an instance of '{typeof(FakeMediator).FullName}'"),
        ["ISender factory"] = (s => s.AddScoped<ISender>(_ => new FakeMediator()), typeof(ISender).FullName!, "a factory registration"),
        ["ISender replace"] = (s => s.Replace(ServiceDescriptor.Transient<ISender, FakeMediator>()), typeof(ISender).FullName!, $"'{typeof(FakeMediator).FullName}'"),
        ["IMediator typed"] = (s => s.AddTransient<IMediator, FakeMediator>(), typeof(IMediator).FullName!, $"'{typeof(FakeMediator).FullName}'"),
        ["IMediator instance"] = (s => s.AddSingleton<IMediator>(new FakeMediator()), typeof(IMediator).FullName!, $"an instance of '{typeof(FakeMediator).FullName}'"),
        ["IMediator factory"] = (s => s.AddScoped<IMediator>(_ => new FakeMediator()), typeof(IMediator).FullName!, "a factory registration"),
        ["IMediator replace"] = (s => s.Replace(ServiceDescriptor.Transient<IMediator, FakeMediator>()), typeof(IMediator).FullName!, $"'{typeof(FakeMediator).FullName}'"),
        ["IPublisher typed"] = (s => s.AddTransient<IPublisher, FakeMediator>(), typeof(IPublisher).FullName!, $"'{typeof(FakeMediator).FullName}'"),
        ["IPublisher instance"] = (s => s.AddSingleton<IPublisher>(new FakeMediator()), typeof(IPublisher).FullName!, $"an instance of '{typeof(FakeMediator).FullName}'"),
        ["IPublisher factory"] = (s => s.AddScoped<IPublisher>(_ => new FakeMediator()), typeof(IPublisher).FullName!, "a factory registration"),
        ["IPublisher replace"] = (s => s.Replace(ServiceDescriptor.Transient<IPublisher, FakeMediator>()), typeof(IPublisher).FullName!, $"'{typeof(FakeMediator).FullName}'"),
        ["IRequestCatalog typed"] = (s => s.AddSingleton<IRequestCatalog, FakeCatalog>(), typeof(IRequestCatalog).FullName!, $"'{typeof(FakeCatalog).FullName}'"),
        ["IRequestCatalog instance"] = (s => s.AddSingleton<IRequestCatalog>(new FakeCatalog()), typeof(IRequestCatalog).FullName!, $"an instance of '{typeof(FakeCatalog).FullName}'"),
        ["IRequestCatalog factory"] = (s => s.AddSingleton<IRequestCatalog>(_ => new FakeCatalog()), typeof(IRequestCatalog).FullName!, "a factory registration"),
        ["IRequestCatalog replace"] = (s => s.Replace(ServiceDescriptor.Singleton<IRequestCatalog, FakeCatalog>()), typeof(IRequestCatalog).FullName!, $"'{typeof(FakeCatalog).FullName}'"),
        ["INotificationPublisher typed"] = (s => s.AddSingleton<INotificationPublisher, TaskWhenAllPublisher>(), typeof(INotificationPublisher).FullName!, $"'{typeof(TaskWhenAllPublisher).FullName}'"),
        ["INotificationPublisher instance"] = (s => s.AddSingleton<INotificationPublisher>(new TaskWhenAllPublisher()), typeof(INotificationPublisher).FullName!, $"an instance of '{typeof(TaskWhenAllPublisher).FullName}'"),
        ["INotificationPublisher factory"] = (s => s.AddTransient<INotificationPublisher>(_ => new TaskWhenAllPublisher()), typeof(INotificationPublisher).FullName!, "a factory registration"),
        ["INotificationPublisher replace"] = (s => s.Replace(ServiceDescriptor.Singleton<INotificationPublisher, TaskWhenAllPublisher>()), typeof(INotificationPublisher).FullName!, $"'{typeof(TaskWhenAllPublisher).FullName}'"),
        ["runtime replaced by a wrapping factory"] = (s => WrapRuntime(s, replace: true), InternalState, "a factory registration"),
        ["runtime registered again with a wrapping factory"] = (s => WrapRuntime(s, replace: false), InternalState, "a factory registration"),
        ["registry registered again"] = (s => s.Add(ServiceDescriptor.Singleton(HandlerRegistry.GetOrAdd(s))), InternalState, $"an instance of '{typeof(HandlerRegistry).FullName}'"),
    };

    public static TheoryData<string> ChangeNames => [.. Changes.Keys];

    private static void WrapRuntime(IServiceCollection services, bool replace)
    {
        var original = services.Single(d => d.ServiceType == typeof(MediatorRuntime));
        var wrapper = ServiceDescriptor.Singleton(typeof(MediatorRuntime), sp => original.ImplementationFactory!(sp));
        if (replace)
            services[services.IndexOf(original)] = wrapper;
        else
            services.Add(wrapper);
    }

    [Theory]
    [MemberData(nameof(ChangeNames))]
    public void OwnServiceRegisteredAgainOrReplaced_IsReportedNamingTheServiceAndWhatIsInItsPlace(string change)
    {
        var (apply, service, registration) = Changes[change];
        using var provider = Build(apply);

        var problem = SingleProblem(provider);

        Assert.Contains(service, problem);
        Assert.Contains($"the container uses {registration}", problem);
        Assert.Contains("Remove the other registration", problem);
        if (service == InternalState)
            Assert.DoesNotContain(nameof(MediatorRuntime), problem);
    }

    [Fact]
    public void OwnServiceRegisteredAfter_SaysAfterIt_AndReplaced_SaysInItsPlace()
    {
        using var after = Build(s => s.AddTransient<ISender, FakeMediator>());
        using var replaced = Build(s => s.Replace(ServiceDescriptor.Transient<ISender, FakeMediator>()));

        Assert.Contains("registered after it", SingleProblem(after));
        Assert.Contains("registered in its place", SingleProblem(replaced));
    }

    public static TheoryData<Type> OwnServiceTypes =>
    [
        typeof(ISender), typeof(IMediator), typeof(IPublisher), typeof(IRequestCatalog), typeof(INotificationPublisher), typeof(HandlerRegistry),
    ];

    [Theory]
    [MemberData(nameof(OwnServiceTypes))]
    public void OwnServiceRemoved_IsReported(Type serviceType)
    {
        using var provider = Build(s => s.RemoveAll(serviceType));

        var problem = SingleProblem(provider);

        if (serviceType == typeof(INotificationPublisher))
        {
            Assert.Contains($"No {nameof(INotificationPublisher)} is registered", problem);
        }
        else
        {
            Assert.Contains(serviceType == typeof(HandlerRegistry) ? InternalState : serviceType.FullName!, problem);
            Assert.Contains("was removed", problem);
        }
    }

    [Fact]
    public void RuntimeRemoved_IsReportedAsInternalStateRemoved()
    {
        using var provider = Build(s => s.RemoveAll<MediatorRuntime>());

        var problem = SingleProblem(provider);

        Assert.Contains(InternalState, problem);
        Assert.Contains("was removed", problem);
    }

    // ---- Item 5: a runtime that cannot be resolved or is not the library's ----------------------------------------------

    [Fact]
    public void RuntimeFactoryThatThrows_IsOneProblem()
    {
        using var provider = Build(s => s.Replace(ServiceDescriptor.Singleton<MediatorRuntime>(_ => throw new InvalidOperationException("broken factory"))));

        var problem = SingleProblem(provider);

        Assert.Contains($"could not create {InternalState}", problem);
        Assert.Contains("broken factory", problem);
    }

    [Fact]
    public void RuntimeRegistrationReturningAnotherObject_IsOneProblem()
    {
        using var provider = Build(s => s.Add(new ServiceDescriptor(typeof(MediatorRuntime), _ => new object(), ServiceLifetime.Singleton)));

        var problem = SingleProblem(provider);

        Assert.Contains(InternalState, problem);
        Assert.Contains("System.Object", problem);
    }

    // ---- Negative cases --------------------------------------------------------------------------------------------------

    [Fact]
    public void UntouchedConfiguration_Passes()
    {
        using var provider = Build(s => s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>());

        provider.VerifyHecoleMediator();
    }

    [Fact]
    public void PublisherRegisteredBeforeAddHecoleMediator_WithDefaultConfiguration_Passes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationPublisher, TaskWhenAllPublisher>();
        services.AddHecoleMediator(_ => { });
        using var provider = services.BuildServiceProvider();

        provider.VerifyHecoleMediator();
    }

    [Fact]
    public void KeyedRegistrationOfSender_DoesNotCount()
    {
        using var provider = Build(s => s.AddKeyedTransient<ISender, FakeMediator>("other"));

        provider.VerifyHecoleMediator();
    }

    [Fact]
    public void ReplacedRequestHandler_Passes()
    {
        using var provider = Build(s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            s.Replace(ServiceDescriptor.Transient<IRequestHandler<TestCommand, TestResult>, CancellationAwareHandler>());
        });

        provider.VerifyHecoleMediator();
    }

    [Fact]
    public void SeveralAddHecoleMediatorCallsWithTheSameOptions_Pass()
    {
        using var provider = Build(s =>
        {
            s.AddHecoleMediator(_ => { });
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            s.AddHecoleMediator(_ => { });
        });

        provider.VerifyHecoleMediator();
    }

    // ---- Item 4: Verify reads what the provider has -------------------------------------------------------------------

    [Fact]
    public void HandlerAddedAfterTheProviderWasBuilt_BeforeAnyResolution_IsReported()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        using var provider = services.BuildServiceProvider();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();

        var problem = SingleProblem(provider);

        Assert.Contains(typeof(TestCommand).FullName!, problem);
        Assert.Contains("added to the service collection after this provider was built", problem);
    }

    [Fact]
    public void HandlerOfAssemblyRequestAddedAfterTheProviderWasBuilt_IsReported()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        using var provider = services.BuildServiceProvider();
        services.AddTransient<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>();

        var problems = Assert.Throws<MediatorConfigurationException>(() => provider.VerifyHecoleMediator(typeof(TestCommand).Assembly)).Problems;

        Assert.Contains(problems, p => p.Contains($"'{typeof(TestCommand).FullName}' ({typeof(TestCommandHandler).FullName}) was added", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandlerRemovedAfterTheProviderWasBuilt_IsNotReported_BecauseThisProviderStillResolvesIt()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        using var provider = services.BuildServiceProvider();
        services.RemoveAll<IRequestHandler<TestCommand, TestResult>>();

        provider.VerifyHecoleMediator();
        using var scope = provider.CreateScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("x")));
    }

    [Fact]
    public void CollectionChangedAfterTheFirstVerify_SecondVerifyGivesTheSameResult()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        using var provider = services.BuildServiceProvider();

        provider.VerifyHecoleMediator();
        services.AddTransient<IRequestHandler<TestCommand, TestResult>, CancellationAwareHandler>();
        services.AddTransient<ISender, FakeMediator>();
        services.RemoveAll<INotificationPublisher>();
        provider.VerifyHecoleMediator();
    }

    [Fact]
    public void CollectionChangedAfterAFailingVerify_SecondVerifyReportsTheSameProblems()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddTransient<ISender, FakeMediator>();
        using var provider = services.BuildServiceProvider();

        var first = Assert.Throws<MediatorConfigurationException>(() => provider.VerifyHecoleMediator()).Problems;
        services.RemoveAll<ISender>();
        var second = Assert.Throws<MediatorConfigurationException>(() => provider.VerifyHecoleMediator()).Problems;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Verify_TakesTheCatalogCopy_SoTheCatalogListsWhatVerifySaw()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        using var provider = services.BuildServiceProvider();

        provider.VerifyHecoleMediator();
        services.AddTransient<IRequestHandler<TestQuery, TestResult>, TestQueryHandler>();

        Assert.Empty(provider.GetRequiredService<IRequestCatalog>().Requests);
    }

    [Fact]
    public void TwoProvidersFromTheSameCollection_EachReportItsOwnTruth()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        using var before = services.BuildServiceProvider();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        using var after = services.BuildServiceProvider();

        after.VerifyHecoleMediator();
        Assert.Contains("after this provider was built", SingleProblem(before));
    }

    // ---- Types ------------------------------------------------------------------------------------------------------

    public sealed class FakeMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => throw new NotSupportedException();
    }

    public sealed class FakeCatalog : IRequestCatalog
    {
        public IReadOnlyList<RequestDescriptor> Requests => [];

        public RequestDescriptor? Find(Type requestType) => null;
    }
}
