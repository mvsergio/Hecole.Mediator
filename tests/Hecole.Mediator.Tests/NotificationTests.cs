using Hecole.Mediator.Internal;
using Hecole.Mediator.NotificationPublishers;
using Hecole.Mediator.Tests.ScanTarget;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

/// <summary>
/// <c>Publish</c>: typed dispatch by runtime type, the two built-in publishers, custom publishers, explicit
/// registration and the publisher configuration (lote 6, items 13, 14 and 15 of the 1.3.0 analysis).
/// </summary>
public class NotificationTests
{
    private static TestHost Build(Action<IServiceCollection> configure, Action<HecoleMediatorConfiguration>? mediator = null)
        => TestHost.Build(s =>
        {
            s.AddSingleton<Log>();
            configure(s);
        }, mediator);

    // ---- Default publisher: ForeachAwaitPublisher -------------------------------------------------------------------

    [Fact]
    public void DefaultPublisher_IsForeachAwaitSingleton()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(INotificationPublisher));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.IsType<ForeachAwaitPublisher>(descriptor.ImplementationInstance);
    }

    [Fact]
    public async Task ForeachAwait_SecondHandlerStartsOnlyAfterFirstCompletes()
    {
        using var host = Build(s =>
        {
            s.AddNotificationHandler<Note, SlowFirstHandler>();
            s.AddNotificationHandler<Note, SecondHandler>();
        });

        await host.Mediator.Publish(new Note("n"));

        Assert.Equal(["first:start", "first:end", "second:n"], host.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task ForeachAwait_ExceptionInFirstHandler_StopsAndReachesCaller()
    {
        using var host = Build(s =>
        {
            s.AddNotificationHandler<Note, AsyncThrowingHandler>();
            s.AddNotificationHandler<Note, SecondHandler>();
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Publish(new Note("n")));

        Assert.Equal("async:n", ex.Message);
        Assert.Empty(host.GetRequiredService<Log>().Entries);
    }

    // Item 14: a handler that throws before returning its task. The exception arrives as thrown (no
    // TargetInvocationException) and, with the default publisher, stops the next handler.
    [Fact]
    public async Task ForeachAwait_SynchronousException_ArrivesUnwrapped()
    {
        using var host = Build(s =>
        {
            s.AddNotificationHandler<Note, SyncThrowingHandler>();
            s.AddNotificationHandler<Note, SecondHandler>();
        });

        var typed = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Publish(new Note("t")));
        var boxed = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Publish((object)new Note("o")));

        Assert.Equal("sync:t", typed.Message);
        Assert.Equal("sync:o", boxed.Message);
        Assert.Empty(host.GetRequiredService<Log>().Entries);
    }

    // ---- TaskWhenAllPublisher ----------------------------------------------------------------------------------------

    [Fact]
    public async Task TaskWhenAll_StartsEveryHandlerBeforeAnyCompletes()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Build(
            s =>
            {
                s.AddSingleton(gate);
                s.AddNotificationHandler<Note, GatedHandler>();
                s.AddNotificationHandler<Note, SecondHandler>();
            },
            cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());
        var log = host.GetRequiredService<Log>();

        var publish = host.Mediator.Publish(new Note("n"));

        // The first handler is blocked on the gate; the second already ran.
        Assert.False(publish.IsCompleted);
        Assert.Equal(["gated:start", "second:n"], log.Entries);

        gate.SetResult();
        await publish;
        Assert.Equal(["gated:start", "second:n", "gated:end"], log.Entries);
    }

    [Fact]
    public async Task TaskWhenAll_EveryHandlerStartsAndEveryExceptionArrives()
    {
        using var host = Build(
            s =>
            {
                s.AddNotificationHandler<Note, SyncThrowingHandler>();
                s.AddNotificationHandler<Note, AsyncThrowingHandler>();
                s.AddNotificationHandler<Note, SecondHandler>();
            },
            cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        var publish = host.Mediator.Publish(new Note("n"));
        var first = await Assert.ThrowsAsync<InvalidOperationException>(() => publish);

        Assert.Equal("sync:n", first.Message);
        Assert.Equal(["sync:n", "async:n"], publish.Exception!.InnerExceptions.Select(e => e.Message));
        Assert.Equal(["second:n"], host.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task TaskWhenAll_SynchronousCancellation_BecomesCanceledTask()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var host = Build(
            s =>
            {
                s.AddNotificationHandler<Note, SyncCancellationHandler>();
                s.AddNotificationHandler<Note, SecondHandler>();
            },
            cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        var publish = host.Mediator.Publish(new Note("n"), cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publish);

        Assert.True(publish.IsCanceled);
        Assert.Equal(["second:n"], host.GetRequiredService<Log>().Entries);
    }

    // ---- Custom publisher -------------------------------------------------------------------------------------------

    [Fact]
    public async Task CustomPublisher_ReceivesExecutorsInRegistrationOrder_AndMaySkipAndReorder()
    {
        var publisher = new ReversingSkippingPublisher();
        using var host = Build(
            s =>
            {
                s.AddNotificationHandler<Note, SyncThrowingHandler>();
                s.AddNotificationHandler<Note, FirstHandler>();
                s.AddNotificationHandler<Note, SecondHandler>();
            },
            cfg => cfg.NotificationPublisher = publisher);
        var note = new Note("n");

        await host.Mediator.Publish(note);

        Assert.Same(note, publisher.Notification);
        Assert.Equal([typeof(SyncThrowingHandler), typeof(FirstHandler), typeof(SecondHandler)], publisher.HandlerTypes);
        Assert.Equal(["second:n", "first:n"], host.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task CustomPublisherType_IsRegisteredWithMediatorLifetime_AndResolvedFromScope()
    {
        using var host = Build(
            s => s.AddNotificationHandler<Note, FirstHandler>(),
            cfg =>
            {
                cfg.Lifetime = ServiceLifetime.Scoped;
                cfg.NotificationPublisherType = typeof(CountingPublisher);
            });

        await host.Mediator.Publish(new Note("a"));
        await host.Mediator.Publish(new Note("b"));

        var publisher = Assert.IsType<CountingPublisher>(host.GetRequiredService<INotificationPublisher>());
        Assert.Equal(2, publisher.Calls);
        Assert.Equal(ServiceLifetime.Scoped, host.GetRequiredService<HandlerRegistry>().Services.Single(d => d.ServiceType == typeof(INotificationPublisher)).Lifetime);
        Assert.Equal(["first:a", "first:b"], host.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public void PublisherTypeAndInstance_TypeWins()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(cfg =>
        {
            cfg.NotificationPublisher = new TaskWhenAllPublisher();
            cfg.NotificationPublisherType = typeof(CountingPublisher);
        });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(INotificationPublisher));
        Assert.Equal(typeof(CountingPublisher), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    [Fact]
    public async Task PublisherRegisteredInContainerBeforeAddHecoleMediator_IsKept()
    {
        var publisher = new ReversingSkippingPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<Log>();
        services.AddSingleton<INotificationPublisher>(publisher);
        services.AddHecoleMediator(_ => { });
        services.AddNotificationHandler<Note, FirstHandler>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(new Note("n"));

        Assert.Single(services, d => d.ServiceType == typeof(INotificationPublisher));
        Assert.Equal([typeof(FirstHandler)], publisher.HandlerTypes);
    }

    [Theory]
    [InlineData(typeof(IPublisher))]
    [InlineData(typeof(AbstractPublisher))]
    [InlineData(typeof(Log))]
    [InlineData(typeof(GenericPublisher<>))]
    public void InvalidPublisherType_Throws(Type type)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddHecoleMediator(cfg => cfg.NotificationPublisherType = type));

        Assert.Contains(nameof(HecoleMediatorConfiguration.NotificationPublisherType), ex.Message);
    }

    [Fact]
    public void NullPublisherInstanceWithoutType_Throws()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<ArgumentException>(() => services.AddHecoleMediator(cfg => cfg.NotificationPublisher = null!));

        Assert.Contains(nameof(HecoleMediatorConfiguration.NotificationPublisher), ex.Message);
    }

    // ---- AddHecoleMediator called more than once --------------------------------------------------------------------

    [Fact]
    public void RepeatedAddHecoleMediator_DefaultPublisherInBoth_IsAllowed()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddHecoleMediator(_ => { });

        Assert.Single(services, d => d.ServiceType == typeof(INotificationPublisher));
    }

    [Fact]
    public void RepeatedAddHecoleMediator_SamePublisherTypeOrInstance_IsAllowed()
    {
        var shared = new ReversingSkippingPublisher();
        var byType = new ServiceCollection();
        byType.AddHecoleMediator(cfg => cfg.NotificationPublisherType = typeof(CountingPublisher));
        byType.AddHecoleMediator(cfg => cfg.NotificationPublisherType = typeof(CountingPublisher));
        var byInstance = new ServiceCollection();
        byInstance.AddHecoleMediator(cfg => cfg.NotificationPublisher = shared);
        byInstance.AddHecoleMediator(cfg => cfg.NotificationPublisher = shared);
        var builtIn = new ServiceCollection();
        builtIn.AddHecoleMediator(cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());
        builtIn.AddHecoleMediator(cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher());

        Assert.Single(byType, d => d.ServiceType == typeof(INotificationPublisher));
        Assert.Single(byInstance, d => d.ServiceType == typeof(INotificationPublisher));
        Assert.Single(builtIn, d => d.ServiceType == typeof(INotificationPublisher));
    }

    public static TheoryData<Action<HecoleMediatorConfiguration>, Action<HecoleMediatorConfiguration>> DifferentPublishers => new()
    {
        { _ => { }, cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher() },
        { cfg => cfg.NotificationPublisher = new TaskWhenAllPublisher(), _ => { } },
        { _ => { }, cfg => cfg.NotificationPublisherType = typeof(ForeachAwaitPublisher) },
        { cfg => cfg.NotificationPublisherType = typeof(CountingPublisher), cfg => cfg.NotificationPublisherType = typeof(ForeachAwaitPublisher) },
        { cfg => cfg.NotificationPublisher = new ReversingSkippingPublisher(), cfg => cfg.NotificationPublisher = new ReversingSkippingPublisher() },
    };

    [Theory]
    [MemberData(nameof(DifferentPublishers))]
    public void RepeatedAddHecoleMediator_DifferentPublisher_Throws(
        Action<HecoleMediatorConfiguration> first, Action<HecoleMediatorConfiguration> second)
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(first);

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddHecoleMediator(second));

        Assert.Contains("NotificationPublisher", ex.Message);
    }

    // ---- Typed dispatch (item 15) ------------------------------------------------------------------------------------

    [Fact]
    public async Task ClassHandlingTwoNotifications_ReceivesEach()
    {
        using var host = Build(s =>
        {
            s.AddNotificationHandler<Note, DualHandler>();
            s.AddNotificationHandler<OtherNote, DualHandler>();
        });

        await host.Mediator.Publish(new Note("a"));
        await host.Mediator.Publish((object)new OtherNote(1));

        Assert.Equal(["dual:note:a", "dual:other:1"], host.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task ExplicitInterfaceImplementation_IsCalled()
    {
        using var host = Build(s => s.AddNotificationHandler<Note, ExplicitHandler>());

        await host.Mediator.Publish(new Note("a"));
        await host.Mediator.Publish((object)new Note("b"));

        Assert.Equal(["explicit:a", "explicit:b"], host.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task ScannedHandlers_AreCalledInDeclarationOrder()
    {
        using var host = Build(s => s.AddSingleton<ScanRecorder>(), cfg => cfg.RegisterServicesFromAssemblyContaining<ScanNote>());
        var recorder = host.GetRequiredService<ScanRecorder>();

        await host.Mediator.Publish(new ScanNote("x"));

        Assert.Equal(["note:x", "audit:x"], recorder.Entries);
    }

    [Fact]
    public async Task NoHandler_BothForms_CompleteWithoutError()
    {
        var publisher = new ReversingSkippingPublisher();
        using var host = Build(_ => { }, cfg => cfg.NotificationPublisher = publisher);

        await host.Mediator.Publish(new Note("a"));
        await host.Mediator.Publish((object)new OtherNote(1));

        Assert.Empty(publisher.HandlerTypes);
    }

    [Fact]
    public async Task PublishTypedAsBaseOrInterface_DispatchesByRuntimeType()
    {
        using var host = Build(s =>
        {
            s.AddNotificationHandler<DerivedNote, DerivedNoteHandler>();
            s.AddNotificationHandler<BaseNote, BaseNoteHandler>();
        });
        BaseNote asBase = new DerivedNote("d");
        INotification asInterface = new DerivedNote("i");

        await host.Mediator.Publish(asBase);
        await host.Mediator.Publish(asInterface);
        await host.Mediator.Publish((object)new DerivedNote("o"));

        Assert.Equal(["derived:d", "derived:i", "derived:o"], host.GetRequiredService<Log>().Entries);
    }

    // With Microsoft.Extensions.DependencyInjection the container does not apply variance, so a handler of
    // a base type (or of INotification) is not called for a derived notification. Dispatch is by exact type.
    [Fact]
    public async Task HandlerOfBaseTypeOrOfINotification_IsNotCalledForDerivedNotification()
    {
        using var host = Build(s =>
        {
            s.AddNotificationHandler<BaseNote, BaseNoteHandler>();
            s.AddTransient<INotificationHandler<INotification>, CatchAllHandler>();
        });

        await host.Mediator.Publish(new DerivedNote("d"));
        await host.Mediator.Publish(new BaseNote("b"));

        Assert.Equal(["base:b"], host.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task StructNotification_BothForms_ReachHandler()
    {
        using var host = Build(s => s.AddNotificationHandler<ValueNote, ValueNoteHandler>());

        await host.Mediator.Publish(new ValueNote(7));
        await host.Mediator.Publish((object)new ValueNote(8));

        Assert.Equal(["value:7", "value:8"], host.GetRequiredService<Log>().Entries);
    }

    [Fact]
    public async Task PublishTyped_Null_Throws()
    {
        using var host = Build(_ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(() => host.Mediator.Publish<Note>(null!));
    }

    [Fact]
    public async Task CancellationToken_ReachesHandler_AndCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        using var host = Build(s => s.AddNotificationHandler<Note, TokenHandler>());
        var log = host.GetRequiredService<Log>();

        await host.Mediator.Publish(new Note("live"), cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Mediator.Publish(new Note("dead"), cts.Token));

        Assert.Equal(["token:live:True:False"], log.Entries);
    }

    // ---- Registration -----------------------------------------------------------------------------------------------

    [Fact]
    public void AddNotificationHandler_RegistersTransient_Once()
    {
        var services = new ServiceCollection();
        services.AddNotificationHandler<Note, FirstHandler>();
        services.AddNotificationHandler<Note, FirstHandler>();
        services.AddNotificationHandler<Note, SecondHandler>();

        var descriptors = services.Where(d => d.ServiceType == typeof(INotificationHandler<Note>)).ToList();
        Assert.Equal([typeof(FirstHandler), typeof(SecondHandler)], descriptors.Select(d => d.ImplementationType));
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Transient, d.Lifetime));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameHandlerByScanAndExplicitRegistration_RunsOnce(bool scanFirst)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ScanRecorder>();
        if (!scanFirst)
            services.AddNotificationHandler<ScanNote, ScanNoteHandler>();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<ScanNote>());
        if (scanFirst)
            services.AddNotificationHandler<ScanNote, ScanNoteHandler>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IPublisher>().Publish(new ScanNote("x"));

        Assert.Single(services, d => d.ServiceType == typeof(INotificationHandler<ScanNote>) && d.ImplementationType == typeof(ScanNoteHandler));
        Assert.Equal(1, provider.GetRequiredService<ScanRecorder>().Entries.Count(e => e == "note:x"));
    }

    [Fact]
    public async Task HandlerRegisteredDirectlyInContainer_IsCalled()
    {
        using var host = Build(s =>
        {
            s.AddNotificationHandler<Note, FirstHandler>();
            s.AddTransient<INotificationHandler<Note>, SecondHandler>();
        });

        await host.Mediator.Publish(new Note("n"));

        Assert.Equal(["first:n", "second:n"], host.GetRequiredService<Log>().Entries);
    }

    // The same handler class returned more than once by the container runs once, at its first position.
    [Fact]
    public async Task SameHandlerClassTwiceInContainer_RunsOnce()
    {
        var publisher = new ReversingSkippingPublisher();
        using var host = Build(
            s =>
            {
                s.AddTransient<INotificationHandler<Note>, FirstHandler>();
                s.AddTransient<INotificationHandler<Note>, SecondHandler>();
                s.AddTransient<INotificationHandler<Note>, FirstHandler>();
                s.AddSingleton<INotificationHandler<Note>>(new SyncThrowingHandler());
                s.AddSingleton<INotificationHandler<Note>>(new SyncThrowingHandler());
            },
            cfg => cfg.NotificationPublisher = publisher);

        await host.Mediator.Publish(new Note("n"));

        Assert.Equal([typeof(FirstHandler), typeof(SecondHandler), typeof(SyncThrowingHandler)], publisher.HandlerTypes);
    }

    // ---- Executors: created once per notification type ---------------------------------------------------------------

    [Fact]
    public void Executor_KnownAtRegistration_ByExplicitRegistrationAndScan()
    {
        var services = new ServiceCollection();
        services.AddNotificationHandler<Note, FirstHandler>();
        services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<ScanNote>());
        var registry = HandlerRegistry.GetOrAdd(services);

        Assert.Contains(registry.NotificationExecutors, e => e.NotificationType == typeof(Note));
        Assert.Contains(registry.NotificationExecutors, e => e.NotificationType == typeof(ScanNote));
        Assert.Same(NotificationExecutor<Note>.Instance, registry.NotificationExecutors.Single(e => e.NotificationType == typeof(Note)));
    }

    [Fact]
    public void Executor_OnDemand_CreatedOnceAndShared()
    {
        using var host = Build(_ => { });
        var catalog = host.Root.GetRequiredService<MediatorRuntime>().Notifications;

        var first = catalog.GetExecutor(typeof(OtherNote));
        var second = catalog.GetExecutor(typeof(OtherNote));

        Assert.Same(first, second);
        Assert.Same(NotificationExecutor<OtherNote>.Instance, first);
    }

    // ---- Types ------------------------------------------------------------------------------------------------------

    public sealed class Log
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_entries)
                    return [.. _entries];
            }
        }

        public void Add(string entry)
        {
            lock (_entries)
                _entries.Add(entry);
        }
    }

    public sealed record Note(string Text) : INotification;

    public sealed record OtherNote(int Number) : INotification;

    public record BaseNote(string Text) : INotification;

    public sealed record DerivedNote(string Text) : BaseNote(Text);

    public readonly record struct ValueNote(int Number) : INotification;

    public sealed class FirstHandler(Log log) : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            log.Add($"first:{notification.Text}");
            return Task.CompletedTask;
        }
    }

    public sealed class SecondHandler(Log log) : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            log.Add($"second:{notification.Text}");
            return Task.CompletedTask;
        }
    }

    public sealed class SlowFirstHandler(Log log) : INotificationHandler<Note>
    {
        public async Task Handle(Note notification, CancellationToken cancellationToken)
        {
            log.Add("first:start");
            await Task.Delay(20, cancellationToken);
            log.Add("first:end");
        }
    }

    public sealed class GatedHandler(Log log, TaskCompletionSource gate) : INotificationHandler<Note>
    {
        public async Task Handle(Note notification, CancellationToken cancellationToken)
        {
            log.Add("gated:start");
            await gate.Task;
            log.Add("gated:end");
        }
    }

    public sealed class AsyncThrowingHandler : INotificationHandler<Note>
    {
        public async Task Handle(Note notification, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException($"async:{notification.Text}");
        }
    }

    public sealed class SyncThrowingHandler : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken)
            => throw new InvalidOperationException($"sync:{notification.Text}");
    }

    public sealed class SyncCancellationHandler : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    public sealed class TokenHandler(Log log) : INotificationHandler<Note>
    {
        public async Task Handle(Note notification, CancellationToken cancellationToken)
        {
            await Task.Delay(1, cancellationToken);
            log.Add($"token:{notification.Text}:{cancellationToken.CanBeCanceled}:{cancellationToken.IsCancellationRequested}");
        }
    }

    public sealed class DualHandler(Log log) : INotificationHandler<Note>, INotificationHandler<OtherNote>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken)
        {
            log.Add($"dual:note:{notification.Text}");
            return Task.CompletedTask;
        }

        public Task Handle(OtherNote notification, CancellationToken cancellationToken)
        {
            log.Add($"dual:other:{notification.Number}");
            return Task.CompletedTask;
        }
    }

    public sealed class ExplicitHandler(Log log) : INotificationHandler<Note>
    {
        Task INotificationHandler<Note>.Handle(Note notification, CancellationToken cancellationToken)
        {
            log.Add($"explicit:{notification.Text}");
            return Task.CompletedTask;
        }
    }

    public sealed class BaseNoteHandler(Log log) : INotificationHandler<BaseNote>
    {
        public Task Handle(BaseNote notification, CancellationToken cancellationToken)
        {
            log.Add($"base:{notification.Text}");
            return Task.CompletedTask;
        }
    }

    public sealed class DerivedNoteHandler(Log log) : INotificationHandler<DerivedNote>
    {
        public Task Handle(DerivedNote notification, CancellationToken cancellationToken)
        {
            log.Add($"derived:{notification.Text}");
            return Task.CompletedTask;
        }
    }

    public sealed class CatchAllHandler(Log log) : INotificationHandler<INotification>
    {
        public Task Handle(INotification notification, CancellationToken cancellationToken)
        {
            log.Add($"all:{notification}");
            return Task.CompletedTask;
        }
    }

    public sealed class ValueNoteHandler(Log log) : INotificationHandler<ValueNote>
    {
        public Task Handle(ValueNote notification, CancellationToken cancellationToken)
        {
            log.Add($"value:{notification.Number}");
            return Task.CompletedTask;
        }
    }

    /// <summary>Records what it receives, then runs the handlers in reverse order, skipping <see cref="SyncThrowingHandler"/>.</summary>
    public sealed class ReversingSkippingPublisher : INotificationPublisher
    {
        public INotification? Notification { get; private set; }

        public List<Type> HandlerTypes { get; } = [];

        public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
        {
            Notification = notification;
            var executors = handlerExecutors.ToList();
            HandlerTypes.AddRange(executors.Select(e => e.HandlerInstance.GetType()));

            foreach (var executor in Enumerable.Reverse(executors).Where(e => e.HandlerInstance is not SyncThrowingHandler))
                await executor.HandlerCallback(notification, cancellationToken);
        }
    }

    public sealed class CountingPublisher : INotificationPublisher
    {
        private readonly ForeachAwaitPublisher _inner = new();

        public int Calls { get; private set; }

        public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
        {
            Calls++;
            return _inner.Publish(handlerExecutors, notification, cancellationToken);
        }
    }

    public abstract class AbstractPublisher : INotificationPublisher
    {
        public abstract Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken);
    }

    public sealed class GenericPublisher<T> : INotificationPublisher
    {
        public Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
