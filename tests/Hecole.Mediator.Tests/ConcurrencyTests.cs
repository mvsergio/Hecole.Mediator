using System.Collections.Concurrent;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

/// <summary>First use of a mediator and of on-demand executors from several threads at once.</summary>
public class ConcurrencyTests
{
    private const int Calls = 32;

    public sealed record OnDemandQuery(int Id) : IRequest<int>;

    public sealed class OnDemandQueryHandler : IRequestHandler<OnDemandQuery, int>
    {
        public async Task<int> Handle(OnDemandQuery request, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return request.Id;
        }
    }

    public sealed record OnDemandNote(int Id) : INotification;

    public sealed class OnDemandNoteHandler(ConcurrentBag<int> received) : INotificationHandler<OnDemandNote>
    {
        public Task Handle(OnDemandNote notification, CancellationToken cancellationToken)
        {
            received.Add(notification.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class InstanceRecordingPublisher(ConcurrentBag<INotificationPublisher> used) : INotificationPublisher
    {
        public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
        {
            used.Add(this);
            return Task.CompletedTask;
        }
    }

    // Starts every call at the same moment, each on its own thread-pool thread.
    private static async Task<T[]> AllAtOnce<T>(int count, Func<int, Task<T>> call)
    {
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(() =>
        {
            start.Wait();
            return call(i);
        })).ToArray();
        start.Set();
        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task FirstUseOfOnDemandRequestAndNotification_InParallel_NoExceptionAndOneResultPerCall()
    {
        var received = new ConcurrentBag<int>();
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        // Registered only in the container: both executors are created on demand, on first use.
        services.AddTransient<IRequestHandler<OnDemandQuery, int>, OnDemandQueryHandler>();
        services.AddTransient<INotificationHandler<OnDemandNote>, OnDemandNoteHandler>();
        services.AddSingleton(received);
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var results = await AllAtOnce(Calls, async i =>
        {
            await using var scope = root.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var typed = await mediator.Send(new OnDemandQuery(i));
            var boxed = (int)(await mediator.Send((object)new OnDemandQuery(i)))!;
            // Publish(object) goes through the on-demand notification catalog.
            await mediator.Publish((object)new OnDemandNote(i));
            return (typed, boxed);
        });

        Assert.Equal(Enumerable.Range(0, Calls), results.Select(r => r.typed));
        Assert.Equal(Enumerable.Range(0, Calls), results.Select(r => r.boxed));
        Assert.Equal(Enumerable.Range(0, Calls), received.Order());
    }

    [Fact]
    public async Task ConcurrentFirstPublish_OnOneMediator_UsesOnePublisherInstance()
    {
        using var bothCreating = new Barrier(2);
        var created = 0;
        var used = new ConcurrentBag<INotificationPublisher>();
        var services = new ServiceCollection();
        // A transient publisher registered before AddHecoleMediator is kept; the barrier holds the first resolution
        // until the second one has started, so both first Publish calls create a publisher.
        services.AddTransient<INotificationPublisher>(_ =>
        {
            Interlocked.Increment(ref created);
            bothCreating.SignalAndWait(TimeSpan.FromSeconds(10));
            return new InstanceRecordingPublisher(used);
        });
        services.AddHecoleMediator(_ => { });
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await AllAtOnce(2, async i =>
        {
            await mediator.Publish(new TestNotification($"n{i}"));
            return i;
        });
        await mediator.Publish(new TestNotification("later"));

        Assert.Equal(2, created);
        Assert.Equal(3, used.Count);
        Assert.Single(used.Distinct());
    }
}
