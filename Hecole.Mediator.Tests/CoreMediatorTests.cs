using FluentAssertions;
using Hecole.Mediator.Implementation;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Hecole.Mediator.Tests;

public class CoreMediatorTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICoreMediator, CoreMediator>();
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_ValidCommand_DispatchesToHandlerAndReturnsResult()
    {
        using var provider = BuildProvider(s =>
            s.AddTransient<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>());
        var mediator = provider.GetRequiredService<ICoreMediator>();

        var result = await mediator.Send(new TestCommand("hello"));

        result.Value.Should().Be("Handled: hello");
    }

    [Fact]
    public async Task Send_ValidQuery_DispatchesToHandlerAndReturnsResult()
    {
        using var provider = BuildProvider(s =>
            s.AddTransient<IRequestHandler<TestQuery, TestResult>, TestQueryHandler>());
        var mediator = provider.GetRequiredService<ICoreMediator>();

        var result = await mediator.Send(new TestQuery(42));

        result.Value.Should().Be("Query: 42");
    }

    [Fact]
    public async Task Send_NoHandlerRegistered_ThrowsInvalidOperationException()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<ICoreMediator>();

        var act = () => mediator.Send(new UnhandledCommand());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Handler not found*");
    }

    [Fact]
    public async Task Send_CancellationTokenIsPropagatedToHandler()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var provider = BuildProvider(s =>
            s.AddTransient<IRequestHandler<TestCommand, TestResult>, CancellationAwareHandler>());
        var mediator = provider.GetRequiredService<ICoreMediator>();

        var act = () => mediator.Send(new TestCommand("test"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Publish_NotifiesAllRegisteredHandlers()
    {
        var handler1 = new TestNotificationHandler();
        var handler2 = new TestNotificationHandler();

        using var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(handler1);
            s.AddSingleton<INotificationHandler<TestNotification>>(handler2);
        });
        var mediator = provider.GetRequiredService<ICoreMediator>();

        await mediator.Publish(new TestNotification("ping"));

        handler1.ReceivedMessages.Should().ContainSingle().Which.Should().Be("ping");
        handler2.ReceivedMessages.Should().ContainSingle().Which.Should().Be("ping");
    }

    [Fact]
    public async Task Publish_NoHandlersRegistered_CompletesWithoutError()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<ICoreMediator>();

        var act = () => mediator.Publish(new TestNotification("nobody"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_OneHandlerThrows_OtherHandlersStillExecute()
    {
        var successHandler = new TestNotificationHandler();

        using var provider = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(successHandler);
            s.AddSingleton<INotificationHandler<TestNotification>>(new ThrowingNotificationHandler());
        });
        var mediator = provider.GetRequiredService<ICoreMediator>();

        var act = () => mediator.Publish(new TestNotification("test"));

        // Task.WhenAll propagates the exception
        await act.Should().ThrowAsync<InvalidOperationException>();
        // But all handlers were invoked (Task.WhenAll starts all tasks before awaiting)
        successHandler.ReceivedMessages.Should().ContainSingle().Which.Should().Be("test");
    }
}
