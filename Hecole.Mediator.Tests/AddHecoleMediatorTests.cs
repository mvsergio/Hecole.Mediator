using FluentAssertions;
using Hecole.Mediator.Implementation;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

public class AddHecoleMediatorTests
{
    [Fact]
    public void OpenGenericBehavior_RegistersAsDefinitionMapping_DoesNotThrowOnBuild()
    {
        var services = new ServiceCollection();

        services.AddHecoleMediator(typeof(OpenGenericTestBehavior<,>).Assembly);

        // Build deve passar — antes do fix 1.3.0, MS DI lançava ArgumentException
        // "Cannot instantiate implementation type 'OpenGenericTestBehavior`2[TRequest,TResponse]'"
        var act = () => services.BuildServiceProvider();
        act.Should().NotThrow();
    }

    [Fact]
    public void OpenGenericBehavior_RegisteredAsDefinitionToDefinition()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(typeof(OpenGenericTestBehavior<,>).Assembly);

        var openGenericMappings = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .ToList();

        openGenericMappings.Should().Contain(d => d.ImplementationType == typeof(OpenGenericTestBehavior<,>),
            "open-generic behavior must be registered with the open-generic service type so MS DI can construct closed instances");
    }

    [Fact]
    public async Task OpenGenericBehavior_RegisteredViaScan_RunsInPipeline()
    {
        var services = new ServiceCollection();
        // TrackingBehavior (também open-generic neste assembly) tem deps custom (List<string>, string) —
        // registrar para satisfazer DI ao construir o pipeline runtime.
        services.AddSingleton<List<string>>(new List<string>());
        services.AddSingleton<string>("tracker");
        services.AddHecoleMediator(typeof(OpenGenericTestBehavior<,>).Assembly);

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICoreMediator>();

        // Usa TestQuery (handler único — TestCommand tem 2 handlers no assembly de fakes).
        var result = await mediator.Send(new TestQuery(42));
        result.Value.Should().Be("Query: 42");
    }

    [Fact]
    public void ClosedGenericHandlers_StillRegistered()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(typeof(TestCommandHandler).Assembly);

        var handlerService = services
            .Where(d => d.ServiceType == typeof(IRequestHandler<TestCommand, TestResult>))
            .ToList();

        handlerService.Should().Contain(d => d.ImplementationType == typeof(TestCommandHandler));
    }

    [Fact]
    public void NotificationHandlers_StillRegistered()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(typeof(TestNotificationHandler).Assembly);

        var notificationService = services
            .Where(d => d.ServiceType == typeof(INotificationHandler<TestNotification>))
            .ToList();

        notificationService.Should().Contain(d => d.ImplementationType == typeof(TestNotificationHandler));
    }
}
