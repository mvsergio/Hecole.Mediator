namespace Hecole.Mediator.Tests.ScanTarget;

/// <summary>Shared log; tests register it as a singleton.</summary>
public sealed class ScanRecorder
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string entry)
    {
        lock (_entries)
            _entries.Add(entry);
    }
}

// Request with response, public handler.
public sealed record ScanQuery(int Id) : IRequest<string>;

public sealed class ScanQueryHandler : IRequestHandler<ScanQuery, string>
{
    public Task<string> Handle(ScanQuery request, CancellationToken cancellationToken) => Task.FromResult($"scan:{request.Id}");
}

// Request without response, internal handler (the scan sees non-public types).
public sealed record ScanCommand(string Name) : IRequest;

internal sealed class ScanCommandHandler(ScanRecorder recorder) : IRequestHandler<ScanCommand>
{
    public Task Handle(ScanCommand request, CancellationToken cancellationToken)
    {
        recorder.Add($"command:{request.Name}");
        return Task.CompletedTask;
    }
}

// Notification with two handlers.
public sealed record ScanNote(string Text) : INotification;

public sealed class ScanNoteHandler(ScanRecorder recorder) : INotificationHandler<ScanNote>
{
    public Task Handle(ScanNote notification, CancellationToken cancellationToken)
    {
        recorder.Add($"note:{notification.Text}");
        return Task.CompletedTask;
    }
}

public sealed class ScanNoteAuditHandler(ScanRecorder recorder) : INotificationHandler<ScanNote>
{
    public Task Handle(ScanNote notification, CancellationToken cancellationToken)
    {
        recorder.Add($"audit:{notification.Text}");
        return Task.CompletedTask;
    }
}

// Not registered by the scan: abstract, open generic, and a pipeline behavior.
public sealed record ScanSkippedQuery() : IRequest<string>;

public abstract class AbstractScanSkippedHandler : IRequestHandler<ScanSkippedQuery, string>
{
    public abstract Task<string> Handle(ScanSkippedQuery request, CancellationToken cancellationToken);
}

public sealed record GenericScanQuery<T>() : IRequest<string>;

public sealed class OpenGenericScanHandler<T> : IRequestHandler<GenericScanQuery<T>, string>
{
    public Task<string> Handle(GenericScanQuery<T> request, CancellationToken cancellationToken) => Task.FromResult(typeof(T).Name);
}

public sealed class ScanTargetBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
}

public sealed class ClosedScanTargetBehavior : IPipelineBehavior<ScanQuery, string>
{
    public Task<string> Handle(ScanQuery request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) => next();
}
