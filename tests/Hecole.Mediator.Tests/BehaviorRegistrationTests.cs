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
        const ServiceLifetime invalid = (ServiceLifetime)42;
        var cfg = new HecoleMediatorConfiguration();

        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.AddBehavior<ClosedCommandBehavior>(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.AddBehavior<IPipelineBehavior<TestCommand, TestResult>, ClosedCommandBehavior>(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.AddBehavior(typeof(ClosedCommandBehavior), invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.AddBehavior(typeof(IPipelineBehavior<TestCommand, TestResult>), typeof(ClosedCommandBehavior), invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.AddOpenBehavior(typeof(PassBehavior<,>), invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.AddOpenBehaviors([typeof(PassBehavior<,>)], invalid));
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

    // ---- 2.0.1: closed behaviors that could never run -----------------------------------------------------------------

    public static TheoryData<Type, string> NeverRunningClosedBehaviors => new()
    {
        { typeof(InterfaceRequestBehavior), "an interface" },
        { typeof(InterfaceVoidRequestBehavior), "an interface" },
        { typeof(AbstractRequestBehavior), "an abstract class" },
        { typeof(NotARequestBehavior), "does not implement" },
        { typeof(AmbiguousRequestBehavior), "more than one request interface" },
        { typeof(WidenedResponseBehavior), "declares the response 'Hecole.Mediator.Tests.Fakes.TestResult'" },
        { typeof(VoidRequestWithResponseBehavior), "declares the response 'Hecole.Mediator.Unit'" },
    };

    [Theory]
    [MemberData(nameof(NeverRunningClosedBehaviors))]
    public void AddBehavior_ClosedBehaviorThatCanNeverRun_ThrowsInEveryOverload(Type behaviorType, string reason)
    {
        var serviceType = Assert.Single(behaviorType.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
        var generic = typeof(HecoleMediatorConfiguration).GetMethods()
            .Single(m => m.Name == nameof(HecoleMediatorConfiguration.AddBehavior) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1)
            .MakeGenericMethod(behaviorType);
        var genericPair = typeof(HecoleMediatorConfiguration).GetMethods()
            .Single(m => m.Name == nameof(HecoleMediatorConfiguration.AddBehavior) && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(serviceType, behaviorType);

        Action[] calls =
        [
            () => new HecoleMediatorConfiguration().AddBehavior(behaviorType),
            () => new HecoleMediatorConfiguration().AddBehavior(serviceType, behaviorType),
            () => Unwrap(() => generic.Invoke(new HecoleMediatorConfiguration(), [ServiceLifetime.Transient])),
            () => Unwrap(() => genericPair.Invoke(new HecoleMediatorConfiguration(), [ServiceLifetime.Transient])),
        ];

        foreach (var call in calls)
        {
            var ex = Assert.Throws<InvalidOperationException>(call);
            Assert.Contains(behaviorType.FullName!, ex.Message);
            Assert.Contains(reason, ex.Message);
        }
    }

    [Fact]
    public void AddBehavior_SeveralClosedInterfaces_OneCanNeverRun_ThrowsAndRegistersNothing()
    {
        var cfg = new HecoleMediatorConfiguration();

        var ex = Assert.Throws<InvalidOperationException>(() => cfg.AddBehavior<ValidAndInterfaceBehavior>());

        Assert.Contains(typeof(ValidAndInterfaceBehavior).FullName!, ex.Message);
        Assert.Empty(cfg.BehaviorsToRegister);
    }

    [Fact]
    public void AddBehavior_ClosedBehaviorOfVoidRequestWithUnit_IsAccepted()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.AddBehavior<VoidCommandBehavior>());

        Assert.Equal(typeof(IPipelineBehavior<VoidCommand, Unit>), Assert.Single(Behaviors(services)).ServiceType);
    }

    private static void Unwrap(Action call)
    {
        try
        {
            call();
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
        }
    }

    public interface IContractCommand : IRequest<TestResult>;

    public interface IContractVoidCommand : IRequest;

    public abstract record AbstractCommandBase : IRequest<TestResult>;

    public sealed record NotARequest;

    public sealed class InterfaceRequestBehavior : IPipelineBehavior<IContractCommand, TestResult>
    {
        public Task<TestResult> Handle(IContractCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    public sealed class InterfaceVoidRequestBehavior : IPipelineBehavior<IContractVoidCommand, Unit>
    {
        public Task<Unit> Handle(IContractVoidCommand request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken) => next();
    }

    public sealed class AbstractRequestBehavior : IPipelineBehavior<AbstractCommandBase, TestResult>
    {
        public Task<TestResult> Handle(AbstractCommandBase request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    public sealed class NotARequestBehavior : IPipelineBehavior<NotARequest, TestResult>
    {
        public Task<TestResult> Handle(NotARequest request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    public sealed class AmbiguousRequestBehavior : IPipelineBehavior<AmbiguousRequest, TestResult>
    {
        public Task<TestResult> Handle(AmbiguousRequest request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    public sealed class WidenedResponseBehavior : IPipelineBehavior<TestCommand, object>
    {
        public Task<object> Handle(TestCommand request, RequestHandlerDelegate<object> next, CancellationToken cancellationToken) => next();
    }

    public sealed class VoidRequestWithResponseBehavior : IPipelineBehavior<VoidCommand, TestResult>
    {
        public Task<TestResult> Handle(VoidCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    public sealed class ValidAndInterfaceBehavior : IPipelineBehavior<TestCommand, TestResult>, IPipelineBehavior<IContractCommand, TestResult>
    {
        public Task<TestResult> Handle(TestCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();

        public Task<TestResult> Handle(IContractCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    public sealed class VoidCommandBehavior : IPipelineBehavior<VoidCommand, Unit>
    {
        public Task<Unit> Handle(VoidCommand request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken) => next();
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
