using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

public class MediatorTests
{
    [Fact]
    public async Task Send_Command_DispatchesToHandlerAndReturnsResult()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>());

        var result = await host.Mediator.Send(new TestCommand("hello"));

        Assert.Equal("Handled: hello", result.Value);
    }

    [Fact]
    public async Task Send_Query_DispatchesToHandlerAndReturnsResult()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<TestQuery, TestResult, TestQueryHandler>());

        var result = await host.Mediator.Send(new TestQuery(42));

        Assert.Equal("Query: 42", result.Value);
    }

    [Fact]
    public async Task Send_NoHandlerRegistered_ThrowsWithFullTypeName()
    {
        using var host = TestHost.Build(_ => { });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Send(new UnhandledCommand()));

        Assert.Contains(typeof(UnhandledCommand).FullName!, ex.Message);
    }

    [Fact]
    public async Task Send_ObjectNoHandlerRegistered_ThrowsWithFullTypeName()
    {
        using var host = TestHost.Build(_ => { });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Send((object)new UnhandledCommand()));

        Assert.Contains(typeof(UnhandledCommand).FullName!, ex.Message);
    }

    [Fact]
    public async Task Send_HandlerRegisteredOnlyInContainer_WorksInAllThreeForms()
    {
        using var host = TestHost.Build(s =>
        {
            s.AddTransient<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>();
            s.AddSingleton<Recorder>();
            s.AddTransient<IRequestHandler<VoidCommand>, VoidCommandHandler>();
        });

        Assert.Equal("Handled: a", (await host.Mediator.Send(new TestCommand("a"))).Value);
        Assert.Equal("Handled: b", Assert.IsType<TestResult>(await host.Mediator.Send((object)new TestCommand("b"))).Value);
        IRequest<object> covariant = new TestCommand("c");
        Assert.Equal("Handled: c", Assert.IsType<TestResult>(await host.Mediator.Send(covariant)).Value);
        await host.Mediator.Send(new VoidCommand("v1"));
        Assert.Equal(Unit.Value, Assert.IsType<Unit>(await host.Mediator.Send((object)new VoidCommand("v2"))));

        Assert.Equal(["v1", "v2"], host.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task Send_HandlerRegisteredOnlyInContainer_ExecutorCreatedOnceAndShared()
    {
        using var host = TestHost.Build(s => s.AddTransient<IRequestHandler<TestCommand, TestResult>, TestCommandHandler>());
        var catalog = host.GetRequiredService<Hecole.Mediator.Internal.MediatorRuntime>().Catalog;

        await host.Mediator.Send(new TestCommand("a"));
        var first = catalog.GetExecutor(typeof(TestCommand));
        using var other = host.Root.CreateScope();
        await other.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("b"));

        Assert.Same(first, catalog.GetExecutor(typeof(TestCommand)));
        Assert.Equal(0, catalog.Count);
    }

    [Fact]
    public async Task Send_NoHandlerAnywhere_ThrowsWithFullTypeNameInAllThreeForms()
    {
        using var host = TestHost.Build(_ => { });

        var withResponse = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Send(new UnhandledCommand()));
        var asObject = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Send((object)new UnhandledCommand()));
        var withoutResponse = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Send(new VoidCommand("v")));

        Assert.Contains(typeof(UnhandledCommand).FullName!, withResponse.Message);
        Assert.Contains(typeof(UnhandledCommand).FullName!, asObject.Message);
        Assert.Contains(typeof(VoidCommand).FullName!, withoutResponse.Message);
    }

    [Fact]
    public async Task SendObject_RequestWithBothRequestInterfaces_ThrowsArgumentException()
    {
        using var host = TestHost.Build(_ => { });

        await Assert.ThrowsAsync<ArgumentException>(() => host.Mediator.Send((object)new AmbiguousRequest()));
    }

    public readonly record struct StructQuery(int Value) : IRequest<int>;

    public sealed class StructQueryHandler : IRequestHandler<StructQuery, int>
    {
        public Task<int> Handle(StructQuery request, CancellationToken cancellationToken) => Task.FromResult(request.Value * 2);
    }

    public sealed record TwoResponsesRequest : IRequest<string>, IRequest<int>;

    public sealed class TwoResponsesHandler : IRequestHandler<TwoResponsesRequest, string>
    {
        public Task<string> Handle(TwoResponsesRequest request, CancellationToken cancellationToken) => Task.FromResult("never");
    }

    [Fact]
    public async Task Send_StructRequest_WorksInEveryForm_RegisteredOrOnlyInContainer()
    {
        using var registered = TestHost.Build(s => s.AddRequestHandler<StructQuery, int, StructQueryHandler>());
        using var containerOnly = TestHost.Build(s => s.AddTransient<IRequestHandler<StructQuery, int>, StructQueryHandler>());

        foreach (var host in new[] { registered, containerOnly })
        {
            Assert.Equal(10, await host.Mediator.Send(new StructQuery(5)));
            Assert.Equal(12, Assert.IsType<int>(await host.Mediator.Send((object)new StructQuery(6))));
        }
    }

    // A request implementing two IRequest<T> has no single response type: refused at registration, and an argument error
    // (synchronous ArgumentException) when sent with only a container registration.
    [Fact]
    public void Send_RequestWithTwoResponseInterfaces_IsRefused()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentException>(() => services.AddRequestHandler<TwoResponsesRequest, string, TwoResponsesHandler>());

        using var host = TestHost.Build(s => s.AddTransient<IRequestHandler<TwoResponsesRequest, string>, TwoResponsesHandler>());
        var boxed = Assert.Throws<ArgumentException>(() => { _ = host.Mediator.Send((object)new TwoResponsesRequest()); });
        Assert.Throws<ArgumentException>(() => { _ = host.Mediator.Send<string>(new TwoResponsesRequest()); });

        Assert.Contains("more than one request interface", boxed.Message, StringComparison.Ordinal);
    }

    // The mediator holds the provider of its scope: once the scope is disposed, Send and Publish fail with
    // ObjectDisposedException, in the returned task.
    [Fact]
    public async Task MediatorUsedAfterItsScopeWasDisposed_FailsThroughTheTaskWithObjectDisposedException()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddNotificationHandler<TestNotification, TestNotificationHandler>();
        using var root = services.BuildServiceProvider();

        IMediator mediator;
        using (var scope = root.CreateScope())
        {
            mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new TestCommand("inside"));
        }

        var send = mediator.Send(new TestCommand("after"));
        var publish = mediator.Publish(new TestNotification("after"));

        Assert.True(send.IsFaulted);
        Assert.True(publish.IsFaulted);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => send);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => publish);
    }

    [Fact]
    public async Task Send_CancellationTokenIsPropagatedToHandler()
    {
        using var cts = new CancellationTokenSource();
        using var host = TestHost.Build(s => s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>());

        Assert.Equal(cts.Token, await host.Mediator.Send(new TokenQuery(), cts.Token));
        Assert.Equal(cts.Token, Assert.IsType<CancellationToken>(await host.Mediator.Send((object)new TokenQuery(), cts.Token)));
    }

    [Fact]
    public async Task Send_CanceledToken_HandlerObservesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var host = TestHost.Build(s => s.AddRequestHandler<TestCommand, TestResult, CancellationAwareHandler>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Mediator.Send(new TestCommand("test"), cts.Token));
    }

    [Fact]
    public async Task Send_HandlerThrowsSynchronously_OriginalExceptionReachesCaller()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<SyncThrowingCommand, TestResult, SyncThrowingHandler>());

        // ThrowsAsync checks the exact type: a TargetInvocationException would fail here.
        await Assert.ThrowsAsync<SyncThrowException>(() => host.Mediator.Send(new SyncThrowingCommand()));
        await Assert.ThrowsAsync<SyncThrowException>(() => host.Mediator.Send((object)new SyncThrowingCommand()));
    }

    [Fact]
    public async Task Send_HandlerThrowsSynchronouslyInsidePipeline_OriginalExceptionReachesCaller()
    {
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<SyncThrowingCommand, TestResult, SyncThrowingHandler>();
            s.AddTransient(typeof(IPipelineBehavior<,>), typeof(OpenGenericTestBehavior<,>));
        });

        await Assert.ThrowsAsync<SyncThrowException>(() => host.Mediator.Send(new SyncThrowingCommand()));
    }

    [Fact]
    public async Task Send_ThroughCovariantInterface_DispatchesByRuntimeType()
    {
        using var host = TestHost.Build(s => s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>());
        IRequest<object> request = new TestCommand("covariant");

        var result = await host.Mediator.Send(request);

        Assert.Equal("Handled: covariant", Assert.IsType<TestResult>(result).Value);
    }

    [Fact]
    public async Task Send_ThroughRequestInterface_DispatchesByRuntimeType()
    {
        using var host = TestHost.Build(s =>
        {
            s.AddSingleton<Recorder>();
            s.AddRequestHandler<VoidCommand, VoidCommandHandler>();
        });
        IRequest request = new VoidCommand("by interface");

        await host.Mediator.Send(request);

        Assert.Equal("by interface", Assert.Single(host.GetRequiredService<Recorder>().Entries));
    }

    [Fact]
    public async Task Send_DerivedRequest_IsDispatchedByExactTypeOnly()
    {
        // Exact type with Microsoft.Extensions.DependencyInjection: the base request's handler is not used.
        using var host = TestHost.Build(s => s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Send(new DerivedTestCommand("d")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Send((object)new DerivedTestCommand("d")));

        Assert.Contains(typeof(DerivedTestCommand).FullName!, ex.Message);
    }

    [Fact]
    public async Task Send_HandlerWithScopedDependency_EachScopeGetsItsOwnInstanceDisposedWithTheScope()
    {
        var services = new ServiceCollection();
        services.AddHecoleMediator(_ => { });
        services.AddScoped<ScopedDependency>();
        services.AddRequestHandler<ScopedDependencyQuery, ScopedDependency, ScopedDependencyHandler>();
        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        ScopedDependency first, firstAgain, second;
        using (var scope1 = root.CreateScope())
        {
            var sender = scope1.ServiceProvider.GetRequiredService<ISender>();
            first = await sender.Send(new ScopedDependencyQuery());
            firstAgain = await sender.Send(new ScopedDependencyQuery());
            Assert.False(first.Disposed);
        }

        await using (var scope2 = root.CreateAsyncScope())
        {
            second = await scope2.ServiceProvider.GetRequiredService<ISender>().Send(new ScopedDependencyQuery());
            Assert.False(second.Disposed);
        }

        Assert.Same(first, firstAgain);
        Assert.NotSame(first, second);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task Publish_NotifiesAllRegisteredHandlers()
    {
        var handler1 = new TestNotificationHandler();
        var handler2 = new SecondTestNotificationHandler();
        using var host = TestHost.Build(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(handler1);
            s.AddSingleton<INotificationHandler<TestNotification>>(handler2);
        });

        await host.Mediator.Publish(new TestNotification("ping"));

        Assert.Equal("ping", Assert.Single(handler1.ReceivedMessages));
        Assert.Equal("ping", Assert.Single(handler2.ReceivedMessages));
    }

    private sealed class SecondTestNotificationHandler : TestNotificationHandler;

    [Fact]
    public async Task Publish_NoHandlersRegistered_CompletesWithoutError()
    {
        using var host = TestHost.Build(_ => { });

        await host.Mediator.Publish(new TestNotification("nobody"));
    }

    [Fact]
    public async Task Publish_HandlerThrows_ReachesCallerAfterEarlierHandlersRan()
    {
        var successHandler = new TestNotificationHandler();
        using var host = TestHost.Build(s =>
        {
            s.AddSingleton<INotificationHandler<TestNotification>>(successHandler);
            s.AddSingleton<INotificationHandler<TestNotification>>(new ThrowingNotificationHandler());
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mediator.Publish(new TestNotification("test")));
        Assert.Equal("test", Assert.Single(successHandler.ReceivedMessages));
    }
}
