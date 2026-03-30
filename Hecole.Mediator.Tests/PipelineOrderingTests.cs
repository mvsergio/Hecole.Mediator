using FluentAssertions;
using Hecole.Mediator.Implementation;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

public class PipelineOrderingTests
{
    [Fact]
    public async Task Pipeline_BehaviorsExecuteInCorrectOrder_BeforeAndAfterHandler()
    {
        var log = new List<string>();
        var handler = new TestCommandHandler();

        var behaviors = new List<object>
        {
            new TrackingBehavior<TestCommand, TestResult>(log, "First"),
            new TrackingBehavior<TestCommand, TestResult>(log, "Second")
        };

        RequestHandlerDelegate<TestResult> handlerDelegate = () =>
        {
            log.Add("handler");
            return handler.Handle(new TestCommand("test"), CancellationToken.None);
        };

        await CorePipelineExecutor.ExecutePipeline<TestCommand, TestResult>(
            new TestCommand("test"), behaviors, handlerDelegate, CancellationToken.None);

        log.Should().Equal("First:before", "Second:before", "handler", "Second:after", "First:after");
    }

    [Fact]
    public async Task Pipeline_NoBehaviors_HandlerExecutesDirectly()
    {
        var called = false;
        RequestHandlerDelegate<TestResult> handlerDelegate = () =>
        {
            called = true;
            return Task.FromResult(new TestResult("direct"));
        };

        var result = await CorePipelineExecutor.ExecutePipeline<TestCommand, TestResult>(
            new TestCommand("test"), Enumerable.Empty<object>(), handlerDelegate, CancellationToken.None);

        called.Should().BeTrue();
        result.Value.Should().Be("direct");
    }

    [Fact]
    public async Task Pipeline_IntegrationWithMediator_BehaviorsAndHandlerExecute()
    {
        var log = new List<string>();

        var services = new ServiceCollection();
        services.AddScoped<ICoreMediator, CoreMediator>();
        services.AddTransient<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>();

        // Register a tracking behavior via factory
        services.AddTransient<IPipelineBehavior<TestCommand, TestResult>>(
            _ => new TrackingBehavior<TestCommand, TestResult>(log, "Tracking"));

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICoreMediator>();

        var result = await mediator.Send(new TestCommand("integration"));

        result.Value.Should().Be("Handled: integration");
        log.Should().Equal("Tracking:before", "Tracking:after");
    }
}
