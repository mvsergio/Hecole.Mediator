using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

/// <summary>
/// <c>cfg.AddBehavior</c>, <c>AddOpenBehavior</c> and <c>AddOpenBehaviors</c>: descriptors in the container, validation, no duplicates.
/// </summary>
public class BehaviorRegistrationTests
{
    private static List<ServiceDescriptor> Behaviors(IServiceCollection services)
        => services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>)
            || (d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))).ToList();

    [Fact]
    public void AddBehavior_ServiceAndImplementation_RegistersClosedBehaviorTransientByDefault()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.AddBehavior<IPipelineBehavior<TestCommand, TestResult>, ClosedCommandBehavior>());

        var descriptor = Assert.Single(Behaviors(services));
        Assert.Equal(typeof(IPipelineBehavior<TestCommand, TestResult>), descriptor.ServiceType);
        Assert.Equal(typeof(ClosedCommandBehavior), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    [Fact]
    public void AddBehavior_ImplementationOnly_RegistersOncePerClosedBehaviorInterface()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.AddBehavior<TwoRequestsBehavior>(ServiceLifetime.Scoped));

        var descriptors = Behaviors(services);
        Assert.Equal(
            [typeof(IPipelineBehavior<TestCommand, TestResult>), typeof(IPipelineBehavior<TestQuery, TestResult>)],
            descriptors.Select(d => d.ServiceType).OrderBy(t => t.GenericTypeArguments[0].Name));
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }

    [Fact]
    public void AddBehavior_TypeOverloads_MatchGenericOverloads()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg
            .AddBehavior(typeof(ClosedCommandBehavior))
            .AddBehavior(typeof(IPipelineBehavior<TestQuery, TestResult>), typeof(TwoRequestsBehavior), ServiceLifetime.Singleton)
            .AddBehavior(typeof(IPipelineBehavior<,>), typeof(PassBehavior<,>)));

        Assert.Equal(
            [
                (typeof(IPipelineBehavior<TestCommand, TestResult>), typeof(ClosedCommandBehavior), ServiceLifetime.Transient),
                (typeof(IPipelineBehavior<TestQuery, TestResult>), typeof(TwoRequestsBehavior), ServiceLifetime.Singleton),
                (typeof(IPipelineBehavior<,>), typeof(PassBehavior<,>), ServiceLifetime.Transient),
            ],
            Behaviors(services).Select(d => (d.ServiceType, d.ImplementationType!, d.Lifetime)));
    }

    [Fact]
    public void AddBehavior_TypeThatIsNotABehavior_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new HecoleMediatorConfiguration().AddBehavior(typeof(TestCommandHandler)));

        Assert.Contains(typeof(TestCommandHandler).FullName!, ex.Message);
        Assert.Contains("IPipelineBehavior", ex.Message);
    }

    [Fact]
    public void AddBehavior_OpenGenericType_ThrowsPointingToAddOpenBehavior()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new HecoleMediatorConfiguration().AddBehavior(typeof(PassBehavior<,>)));

        Assert.Contains("AddOpenBehavior", ex.Message);
    }

    [Fact]
    public void AddBehavior_ServiceTypeNotAClosedBehavior_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HecoleMediatorConfiguration().AddBehavior(typeof(IRequestHandler<TestCommand, TestResult>), typeof(ClosedCommandBehavior)));

        Assert.Contains("is not a closed", ex.Message);
    }

    [Fact]
    public void AddBehavior_ImplementationNotAssignableToServiceType_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new HecoleMediatorConfiguration().AddBehavior<IPipelineBehavior<TestQuery, TestResult>, ClosedCommandBehavior>());

        Assert.Contains(typeof(ClosedCommandBehavior).FullName!, ex.Message);
        Assert.Contains("does not implement", ex.Message);
    }

    [Fact]
    public void AddOpenBehavior_RegistersOpenDescriptor_WithLifetime()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.AddOpenBehavior(typeof(PassBehavior<,>), ServiceLifetime.Scoped));

        var descriptor = Assert.Single(Behaviors(services));
        Assert.Equal(typeof(IPipelineBehavior<,>), descriptor.ServiceType);
        Assert.Equal(typeof(PassBehavior<,>), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void AddOpenBehaviors_RegistersInOrder()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.AddOpenBehaviors([typeof(PassBehavior<,>), typeof(OtherPassBehavior<,>)]));

        Assert.Equal([typeof(PassBehavior<,>), typeof(OtherPassBehavior<,>)], Behaviors(services).Select(d => d.ImplementationType));
    }

    [Theory]
    [InlineData(typeof(ClosedCommandBehavior), "open generic type definition")]
    [InlineData(typeof(List<>), "does not implement")]
    [InlineData(typeof(AbstractOpenBehavior<,>), "concrete class")]
    public void AddOpenBehavior_InvalidType_ThrowsWithReason(Type type, string reason)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new HecoleMediatorConfiguration().AddOpenBehavior(type));

        Assert.Contains(reason, ex.Message);
    }

    [Theory]
    [InlineData(typeof(OneParameterBehavior<>), "1 type parameter")]
    [InlineData(typeof(ThreeParameterBehavior<,,>), "3 type parameter")]
    [InlineData(typeof(SwappedParameterBehavior<,>), "2 type parameter")]
    [InlineData(typeof(WrappedResponseBehavior<,>), "2 type parameter")]
    public void AddOpenBehavior_TypeParametersThatDoNotMapToTheInterface_ThrowsClearly(Type type, string detail)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new HecoleMediatorConfiguration().AddOpenBehavior(type));

        Assert.Contains(type.FullName!, ex.Message);
        Assert.Contains(detail, ex.Message);
        Assert.Contains("do not map", ex.Message);
    }

    [Fact]
    public void BehaviorOfOtherArity_RegisteredDirectlyInContainer_IsRefusedByTheContainerAtBuild()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OneParameterBehavior<>));

        var ex = Assert.Throws<ArgumentException>(() => services.BuildServiceProvider());

        Assert.Contains(typeof(OneParameterBehavior<>).Name, ex.Message);
    }

    [Fact]
    public void AddBehavior_InvalidLifetime_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HecoleMediatorConfiguration().AddOpenBehavior(typeof(PassBehavior<,>), (ServiceLifetime)42));
    }

    [Fact]
    public void Behaviors_NullArguments_Throw()
    {
        var cfg = new HecoleMediatorConfiguration();

        Assert.Throws<ArgumentNullException>(() => cfg.AddBehavior(null!));
        Assert.Throws<ArgumentNullException>(() => cfg.AddBehavior(null!, typeof(ClosedCommandBehavior)));
        Assert.Throws<ArgumentNullException>(() => cfg.AddBehavior(typeof(IPipelineBehavior<TestCommand, TestResult>), null!));
        Assert.Throws<ArgumentNullException>(() => cfg.AddOpenBehavior(null!));
        Assert.Throws<ArgumentNullException>(() => cfg.AddOpenBehaviors(null!));
    }

    [Fact]
    public void SameBehaviorTwice_InOneCall_InTwoCallsOrAlreadyInContainer_IsRegisteredOnce()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(OtherPassBehavior<,>));
        services.AddHecoleMediator(cfg => cfg
            .AddOpenBehavior(typeof(PassBehavior<,>))
            .AddOpenBehavior(typeof(PassBehavior<,>))
            .AddOpenBehavior(typeof(OtherPassBehavior<,>))
            .AddBehavior<ClosedCommandBehavior>());
        services.AddHecoleMediator(cfg => cfg
            .AddOpenBehavior(typeof(PassBehavior<,>))
            .AddBehavior<ClosedCommandBehavior>());

        Assert.Equal(
            [typeof(OtherPassBehavior<,>), typeof(PassBehavior<,>), typeof(ClosedCommandBehavior)],
            Behaviors(services).Select(d => d.ImplementationType));
    }

    public sealed class ClosedCommandBehavior : IPipelineBehavior<TestCommand, TestResult>
    {
        public Task<TestResult> Handle(TestCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    public sealed class TwoRequestsBehavior : IPipelineBehavior<TestCommand, TestResult>, IPipelineBehavior<TestQuery, TestResult>
    {
        public Task<TestResult> Handle(TestCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();

        public Task<TestResult> Handle(TestQuery request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    public sealed class PassBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public sealed class OtherPassBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public abstract class AbstractOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public abstract Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }

    public sealed class OneParameterBehavior<TRequest> : IPipelineBehavior<TRequest, Unit>
        where TRequest : notnull
    {
        public Task<Unit> Handle(TRequest request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken) => next();
    }

    public sealed class ThreeParameterBehavior<TRequest, TResponse, TExtra> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public sealed class SwappedParameterBehavior<TResponse, TRequest> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public sealed class WrappedResponseBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, List<TResponse>>
        where TRequest : notnull
    {
        public Task<List<TResponse>> Handle(TRequest request, RequestHandlerDelegate<List<TResponse>> next, CancellationToken cancellationToken) => next();
    }
}
