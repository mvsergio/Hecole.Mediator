using FluentAssertions;
using FluentValidation;
using Hecole.Mediator.Implementation;
using Hecole.Mediator.Implementation.Extensions;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

public class RegistrationTests
{
    [Fact]
    public void AddHecoleMediator_RegistersCoreMediatorDescriptor()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(typeof(CoreMediator).Assembly);

        services.Should().Contain(d =>
            d.ServiceType == typeof(ICoreMediator) &&
            d.ImplementationType == typeof(CoreMediator) &&
            d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddHecoleMediator_RegistersBehaviorDescriptors()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(typeof(CoreMediator).Assembly);

        // The mediator assembly contains LoggingBehavior, PerformanceBehavior, etc.
        var behaviorDescriptors = services
            .Where(d => d.ServiceType.IsGenericType &&
                        d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
            .ToList();

        behaviorDescriptors.Should().NotBeEmpty("Mediator assembly contains built-in behaviors");
    }

    [Fact]
    public void AddCoreMediator_RegistersCoreMediatorAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCoreMediator(typeof(CoreMediator).Assembly);

        services.Should().Contain(d =>
            d.ServiceType == typeof(ICoreMediator) &&
            d.ImplementationType == typeof(CoreMediator) &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddCoreMediator_RegistersHandlerDescriptors()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>();

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IRequestHandler<TestCommand, TestResult>));

        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(TestCommandHandler));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void ManualRegistration_HandlersAndBehaviorsResolveCorrectly()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICoreMediator, CoreMediator>();
        services.AddTransient<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>();
        services.AddTransient<INotificationHandler<TestNotification>, TestNotificationHandler>();

        using var provider = services.BuildServiceProvider();

        provider.GetService<ICoreMediator>().Should().NotBeNull();
        provider.GetService<IRequestHandler<TestCommand, TestResult>>().Should().BeOfType<TestCommandHandler>();
        provider.GetServices<INotificationHandler<TestNotification>>().Should().ContainSingle()
            .Which.Should().BeOfType<TestNotificationHandler>();
    }

    [Fact]
    public void ValidatorsResolvedViaEnumerable_WhenRegisteredManually()
    {
        var services = new ServiceCollection();
        services.AddTransient<IValidator<TestCommand>, TestCommandValidator>();
        services.AddTransient<IValidator<TestCommand>, TestCommandLengthValidator>();

        using var provider = services.BuildServiceProvider();
        var validators = provider.GetServices<IValidator<TestCommand>>().ToList();

        validators.Should().HaveCount(2);
        validators.Should().ContainSingle(v => v is TestCommandValidator);
        validators.Should().ContainSingle(v => v is TestCommandLengthValidator);
    }
}
