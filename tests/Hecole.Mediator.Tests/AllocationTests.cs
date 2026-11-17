using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

/// <summary>
/// Ceiling on bytes allocated per <c>Send</c> and <c>Publish</c> (handlers and behaviors resolved per call; they
/// allocate nothing themselves). The 1.3.0 allocated 768 B per call without behaviors and 1.376 B with one.
/// </summary>
public class AllocationTests
{
    private const int Calls = 10_000;
    private const long CeilingBytesPerSend = 128;

    [Fact]
    public void SendWithResponse_NoBehaviors_AllocatesBelowCeiling()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<PingQuery, string, PingHandler>());
        var sender = host.GetRequiredService<ISender>();
        var request = new PingQuery();

        var perCall = Measure(() => sender.Send(request).GetAwaiter().GetResult());

        Assert.True(perCall <= CeilingBytesPerSend, $"Send allocated {perCall} B per call; ceiling is {CeilingBytesPerSend} B.");
    }

    [Fact]
    public void SendWithoutResponse_NoBehaviors_AllocatesBelowCeiling()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<NoopCommand, NoopCommandHandler>());
        var sender = host.GetRequiredService<ISender>();
        var request = new NoopCommand();

        var perCall = Measure(() => sender.Send(request).GetAwaiter().GetResult());

        Assert.True(perCall <= CeilingBytesPerSend, $"Send allocated {perCall} B per call; ceiling is {CeilingBytesPerSend} B.");
    }

    [Fact]
    public void SendWithResponse_OneBehavior_AllocatesBelowCeiling()
    {
        const long ceiling = 256;
        using var host = TestHost.Build(
            s => s.AddRequestHandler<PingQuery, string, PingHandler>(),
            cfg => cfg.AddOpenBehavior(typeof(OpenGenericTestBehavior<,>)));
        var sender = host.GetRequiredService<ISender>();
        var request = new PingQuery();

        var perCall = Measure(() => sender.Send(request).GetAwaiter().GetResult());

        Assert.True(perCall <= ceiling, $"Send with one behavior allocated {perCall} B per call; ceiling is {ceiling} B.");
    }

    // Per Publish: the handler array from the container, the handlers (transient), the executor array and, per handler,
    // one NotificationHandlerExecutor and one delegate bound to the handler (184 B with one handler, 440 B with three).
    // The default publisher is an async method: it allocates nothing in Release when every handler completes
    // synchronously, but its state machine is a class in Debug (+104 B). Ceilings hold in both; the 1.3.0 allocated
    // 376 B with one handler and 672 B with three.
    [Theory]
    [InlineData(1, 320)]
    [InlineData(3, 608)]
    public void Publish_AllocatesBelowCeiling(int handlers, long ceiling)
    {
        using var host = TestHost.Build(s =>
        {
            s.AddNotificationHandler<NoopNote, NoopNoteHandler1>();
            if (handlers > 1)
                s.AddNotificationHandler<NoopNote, NoopNoteHandler2>();
            if (handlers > 2)
                s.AddNotificationHandler<NoopNote, NoopNoteHandler3>();
        });
        var publisher = host.GetRequiredService<IPublisher>();
        var notification = new NoopNote();

        var perCall = Measure(() => publisher.Publish(notification).GetAwaiter().GetResult());

        Assert.True(perCall <= ceiling, $"Publish with {handlers} handler(s) allocated {perCall} B per call; ceiling is {ceiling} B.");
    }

    private static long Measure(Action call)
    {
        for (var i = 0; i < 1_000; i++)
            call();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Calls; i++)
            call();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / Calls;
    }

    private sealed record NoopNote : INotification;

    private sealed class NoopNoteHandler1 : INotificationHandler<NoopNote>
    {
        public Task Handle(NoopNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopNoteHandler2 : INotificationHandler<NoopNote>
    {
        public Task Handle(NoopNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopNoteHandler3 : INotificationHandler<NoopNote>
    {
        public Task Handle(NoopNote notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record NoopCommand : IRequest;

    private sealed class NoopCommandHandler : IRequestHandler<NoopCommand>
    {
        public Task Handle(NoopCommand request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
