using FluentValidation;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;

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

// Validators
public class TestCommandValidator : AbstractValidator<TestCommand>
{
    public TestCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
    }
}

public class TestCommandLengthValidator : AbstractValidator<TestCommand>
{
    public TestCommandLengthValidator()
    {
        RuleFor(x => x.Name).MaximumLength(10).WithMessage("Name too long");
    }
}

public class AsyncTestCommandValidator : AbstractValidator<TestCommand>
{
    public AsyncTestCommandValidator()
    {
        RuleFor(x => x.Name).MustAsync(async (name, ct) =>
        {
            await Task.Delay(1, ct);
            return name != "forbidden";
        }).WithMessage("Name is forbidden");
    }
}

public class ThrowingNotificationHandler : INotificationHandler<TestNotification>
{
    public Task Handle(TestNotification notification, CancellationToken cancellationToken)
    {
        return Task.FromException(new InvalidOperationException("Handler failed"));
    }
}

// Open-generic behavior with DI-resolvable constructor (for AddHecoleMediator scan tests).
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
