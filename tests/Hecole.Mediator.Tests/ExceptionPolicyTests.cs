using System.ComponentModel.DataAnnotations;
using Hecole.Mediator.Behaviors;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Tests;

/// <summary>
/// <c>Send</c> and <c>Publish</c> throw synchronously only for argument errors; every other failure arrives in the
/// returned task. Each test first calls the method without awaiting, so a synchronous throw fails the test.
/// </summary>
public class ExceptionPolicyTests
{
    public sealed class ConstructorException() : Exception("constructor failed");

    public sealed record ValidatedCommand([property: Required] string? Name) : IRequest<string>;

    private sealed class ValidatedCommandHandler : IRequestHandler<ValidatedCommand, string>
    {
        public Task<string> Handle(ValidatedCommand request, CancellationToken cancellationToken) => Task.FromResult(request.Name!);
    }

    private sealed class ThrowingConstructorBehavior : IPipelineBehavior<TestCommand, TestResult>
    {
        public ThrowingConstructorBehavior() => throw new ConstructorException();

        public Task<TestResult> Handle(TestCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken) => next();
    }

    private sealed class ThrowingConstructorHandler : IRequestHandler<TestCommand, TestResult>
    {
        public ThrowingConstructorHandler() => throw new ConstructorException();

        public Task<TestResult> Handle(TestCommand request, CancellationToken cancellationToken) => Task.FromResult(new TestResult("never"));
    }

    // Not async: throws before returning a task.
    private sealed class SyncThrowingBehavior : IPipelineBehavior<TestCommand, TestResult>
    {
        public Task<TestResult> Handle(TestCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken)
            => throw new SyncThrowException("behavior thrown synchronously");
    }

    private sealed class ThrowingConstructorPublisher : INotificationPublisher
    {
        public ThrowingConstructorPublisher() => throw new ConstructorException();

        public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class SyncThrowingNotificationHandler : INotificationHandler<TestNotification>
    {
        public Task Handle(TestNotification notification, CancellationToken cancellationToken) => throw new SyncThrowException("handler thrown synchronously");
    }

    private static TestHost WithLogging(LogSink sink, Action<IServiceCollection> configure, Action<HecoleMediatorConfiguration>? mediator = null)
        => TestHost.Build(s =>
        {
            s.AddSingleton(sink).AddSingleton(typeof(ILogger<>), typeof(FakeLogger<>));
            configure(s);
        }, mediator);

    [Fact]
    public async Task InnerBehaviorConstructorThrows_ArrivesThroughTheTask_AndNoBehaviorSeesIt()
    {
        var sink = new LogSink();
        using var host = WithLogging(sink, s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            s.AddTransient<IPipelineBehavior<TestCommand, TestResult>, ThrowingConstructorBehavior>();
        }, cfg => cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>)));
        var mediator = host.Mediator;

        var task = mediator.Send(new TestCommand("x"));

        Assert.True(task.IsFaulted);
        await Assert.ThrowsAsync<ConstructorException>(() => task);
        // Behaviors are created before the pipeline starts: the outer UnhandledExceptionBehavior never ran.
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public async Task HandlerConstructorThrows_ArrivesThroughTheTask_AndIsLoggedByTheOuterBehavior()
    {
        var sink = new LogSink();
        using var host = WithLogging(sink, s => s.AddRequestHandler<TestCommand, TestResult, ThrowingConstructorHandler>(),
            cfg => cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>)));

        var task = host.Mediator.Send(new TestCommand("x"));

        await Assert.ThrowsAsync<ConstructorException>(() => task);
        Assert.Equal(3001, Assert.Single(sink.Entries).EventId.Id);
    }

    [Fact]
    public async Task ValidationBehaviorOutermost_InvalidRequest_ArrivesThroughTheTask()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<ValidatedCommand, string, ValidatedCommandHandler>(),
            cfg => cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)));

        var typed = host.Mediator.Send(new ValidatedCommand(null));
        var boxed = host.Mediator.Send((object)new ValidatedCommand(null));

        Assert.True(typed.IsFaulted);
        await Assert.ThrowsAsync<RequestValidationException>(() => typed);
        await Assert.ThrowsAsync<RequestValidationException>(() => boxed);
    }

    [Fact]
    public async Task NonAsyncBehaviorThrowsBeforeReturningATask_ArrivesThroughTheTask()
    {
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
            s.AddTransient<IPipelineBehavior<TestCommand, TestResult>, SyncThrowingBehavior>();
        });

        var task = host.Mediator.Send(new TestCommand("x"));

        Assert.True(task.IsFaulted);
        await Assert.ThrowsAsync<SyncThrowException>(() => task);
    }

    [Fact]
    public async Task HandlerThrowsBeforeReturningATask_ArrivesThroughTheTask_InEveryForm()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<SyncThrowingCommand, TestResult, SyncThrowingHandler>());

        var typed = host.Mediator.Send(new SyncThrowingCommand());
        var boxed = host.Mediator.Send((object)new SyncThrowingCommand());

        Assert.True(typed.IsFaulted);
        await Assert.ThrowsAsync<SyncThrowException>(() => typed);
        await Assert.ThrowsAsync<SyncThrowException>(() => boxed);
    }

    [Fact]
    public async Task NoHandler_ArrivesThroughTheTask_InEveryForm()
    {
        using var host = TestHost.Build(_ => { });

        var typed = host.Mediator.Send(new UnhandledCommand());
        var boxed = host.Mediator.Send((object)new UnhandledCommand());
        var voidTask = host.Mediator.Send(new VoidCommand("v"));

        Assert.True(typed.IsFaulted);
        Assert.True(voidTask.IsFaulted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => typed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => boxed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => voidTask);
    }

    [Fact]
    public async Task RootProviderGuard_ArrivesThroughTheTask_ForSendAndPublish()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RequireScope = true);
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        using var root = services.BuildServiceProvider();
        var mediator = root.GetRequiredService<IMediator>();

        var tasks = new Task[]
        {
            mediator.Send(new TestCommand("x")),
            mediator.Send((object)new TestCommand("x")),
            mediator.Send(new VoidCommand("v")),
            mediator.Publish(new TestNotification("n")),
            mediator.Publish((object)new TestNotification("n")),
        };

        foreach (var task in tasks)
        {
            Assert.True(task.IsFaulted);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            Assert.Contains("root IServiceProvider", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WhenAll_TwoFailingSends_AggregatesBothFailures()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<SyncThrowingCommand, TestResult, SyncThrowingHandler>());

        var all = Task.WhenAll(host.Mediator.Send(new UnhandledCommand()), host.Mediator.Send(new SyncThrowingCommand()));

        await Assert.ThrowsAnyAsync<Exception>(() => all);
        Assert.Collection(
            all.Exception!.InnerExceptions,
            e => Assert.IsType<InvalidOperationException>(e),
            e => Assert.IsType<SyncThrowException>(e));
    }

    [Fact]
    public async Task PublisherCannotBeCreated_ArrivesThroughTheTask()
    {
        using var host = TestHost.Build(_ => { }, cfg => cfg.NotificationPublisherType = typeof(ThrowingConstructorPublisher));

        var typed = host.Mediator.Publish(new TestNotification("n"));
        var boxed = host.Mediator.Publish((object)new TestNotification("n"));

        Assert.True(typed.IsFaulted);
        await Assert.ThrowsAsync<ConstructorException>(() => typed);
        await Assert.ThrowsAsync<ConstructorException>(() => boxed);
    }

    [Fact]
    public async Task NotificationHandlerThrowsBeforeReturningATask_ArrivesThroughTheTask()
    {
        using var host = TestHost.Build(s => s.AddNotificationHandler<TestNotification, SyncThrowingNotificationHandler>());

        var task = host.Mediator.Publish(new TestNotification("n"));

        await Assert.ThrowsAsync<SyncThrowException>(() => task);
    }

    [Fact]
    public void ArgumentErrors_AreThrownSynchronously()
    {
        using var host = TestHost.Build(_ => { });
        var mediator = host.Mediator;

        Assert.Throws<ArgumentNullException>(() => { _ = mediator.Send((IRequest<TestResult>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = mediator.Send((VoidCommand)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = mediator.Send((object)null!); });
        Assert.Throws<ArgumentException>(() => { _ = mediator.Send(new object()); });
        Assert.Throws<ArgumentNullException>(() => { _ = mediator.Publish((object)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = mediator.Publish((TestNotification)null!); });
        Assert.Throws<ArgumentException>(() => { _ = mediator.Publish(new object()); });
    }

    [Fact]
    public void ArgumentErrors_AreThrownSynchronously_EvenWhenTheRootGuardWouldFail()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg => cfg.RequireScope = true);
        using var root = services.BuildServiceProvider();
        var mediator = root.GetRequiredService<IMediator>();

        Assert.Throws<ArgumentNullException>(() => { _ = mediator.Send((object)null!); });
        Assert.Throws<ArgumentException>(() => { _ = mediator.Publish(new object()); });
    }
}
