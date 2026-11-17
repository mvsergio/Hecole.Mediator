using Hecole.Mediator;

namespace Hecole.Mediator.Tests.Fakes;

// Commands & Queries
public record TestCommand(string Name) : IRequest<TestResult>;
public record TestQuery(int Id) : IRequest<TestResult>;
public record UnhandledCommand() : IRequest<TestResult>;
public record TestResult(string Value);

// Notifications
public record TestNotification(string Message) : INotification;

// Handlers
public class TestCommandHandler : IRequestHandler<TestCommand, TestResult>
{
    public Task<TestResult> Handle(TestCommand request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new TestResult($"Handled: {request.Name}"));
    }
}

public class TestQueryHandler : IRequestHandler<TestQuery, TestResult>
{
    public Task<TestResult> Handle(TestQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new TestResult($"Query: {request.Id}"));
    }
}

public class CancellationAwareHandler : IRequestHandler<TestCommand, TestResult>
{
    public async Task<TestResult> Handle(TestCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(10, cancellationToken);
        return new TestResult("ok");
    }
}

public class TestNotificationHandler : INotificationHandler<TestNotification>
{
    public List<string> ReceivedMessages { get; } = [];

    public Task Handle(TestNotification notification, CancellationToken cancellationToken)
    {
        ReceivedMessages.Add(notification.Message);
        return Task.CompletedTask;
    }
}

public class ThrowingNotificationHandler : INotificationHandler<TestNotification>
{
    public Task Handle(TestNotification notification, CancellationToken cancellationToken)
    {
        return Task.FromException(new InvalidOperationException("Handler failed"));
    }
}

// Open-generic behavior with DI-resolvable constructor.
public class OpenGenericTestBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

// Tracking Behavior (for pipeline ordering tests)
public class TrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly List<string> _log;
    private readonly string _name;

    public TrackingBehavior(List<string> log, string name)
    {
        _log = log;
        _name = name;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _log.Add($"{_name}:before");
        var result = await next();
        _log.Add($"{_name}:after");
        return result;
    }
}

// Request without a response value (IRequest)
public record VoidCommand(string Name) : IRequest;

public class VoidCommandHandler(Recorder recorder) : IRequestHandler<VoidCommand>
{
    public Task Handle(VoidCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        recorder.Add(request.Name);
        return Task.CompletedTask;
    }
}

// Shared log for handlers and behaviors created by the container.
public sealed class Recorder
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string entry) => _entries.Add(entry);
}

// Request that implements both request interfaces (invalid for Send(object))
public record AmbiguousRequest() : IRequest, IRequest<TestResult>;

// Derived request: dispatched by exact type only, so it does not reach the handler of TestCommand.
public record DerivedTestCommand(string Name) : TestCommand(Name);

// Handler that throws before returning a task.
public record SyncThrowingCommand() : IRequest<TestResult>;
public sealed class SyncThrowException(string message) : Exception(message);

public class SyncThrowingHandler : IRequestHandler<SyncThrowingCommand, TestResult>
{
    public Task<TestResult> Handle(SyncThrowingCommand request, CancellationToken cancellationToken)
        => throw new SyncThrowException("thrown synchronously");
}

// Handler that returns the token it received.
public record TokenQuery() : IRequest<CancellationToken>;

public class TokenQueryHandler : IRequestHandler<TokenQuery, CancellationToken>
{
    public Task<CancellationToken> Handle(TokenQuery request, CancellationToken cancellationToken)
        => Task.FromResult(cancellationToken);
}

// Handler with a scoped, disposable dependency.
public sealed class ScopedDependency : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

public record ScopedDependencyQuery() : IRequest<ScopedDependency>;

public class ScopedDependencyHandler(ScopedDependency dependency) : IRequestHandler<ScopedDependencyQuery, ScopedDependency>
{
    public Task<ScopedDependency> Handle(ScopedDependencyQuery request, CancellationToken cancellationToken)
        => Task.FromResult(dependency);
}

// Handler with no allocation of its own (for the allocation ceiling test).
public record PingQuery() : IRequest<string>;

public class PingHandler : IRequestHandler<PingQuery, string>
{
    private static readonly Task<string> Pong = Task.FromResult("pong");

    public Task<string> Handle(PingQuery request, CancellationToken cancellationToken) => Pong;
}
