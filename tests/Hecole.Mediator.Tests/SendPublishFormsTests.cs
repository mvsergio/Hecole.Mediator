using Hecole.Mediator;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

/// <summary>
/// The three forms of <see cref="ISender.Send{TResponse}"/> and the two forms of <see cref="IPublisher.Publish(object, CancellationToken)"/>.
/// </summary>
public class SendPublishFormsTests
{
    private static TestHost BuildProvider(Action<IServiceCollection> configure) => TestHost.Build(configure);

    private static void AddVoidHandler(IServiceCollection services)
    {
        services.AddSingleton<Recorder>();
        services.AddRequestHandler<VoidCommand, VoidCommandHandler>();
    }

    // Send<TResponse>(IRequest<TResponse>)

    [Fact]
    public async Task SendWithResponse_ReturnsHandlerResponse()
    {
        using var provider = BuildProvider(s =>
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>());
        ISender sender = provider.GetRequiredService<IMediator>();

        TestResult result = await sender.Send(new TestCommand("a"));

        Assert.Equal("Handled: a", result.Value);
    }

    [Fact]
    public async Task SendWithResponse_RunsBehaviorsAndPassesToken()
    {
        using var cts = new CancellationTokenSource();
        var seen = new List<CancellationToken>();
        using var provider = BuildProvider(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenRecordingBehavior(seen));
        });
        var mediator = provider.GetRequiredService<IMediator>();

        var handlerToken = await mediator.Send(new TokenQuery(), cts.Token);

        Assert.Equal(cts.Token, Assert.Single(seen));
        Assert.Equal(cts.Token, handlerToken);
    }

    // Send<TRequest>(TRequest) where TRequest : IRequest

    [Fact]
    public async Task SendWithoutResponse_CallsHandler()
    {
        using var provider = BuildProvider(s => AddVoidHandler(s));
        ISender sender = provider.GetRequiredService<IMediator>();

        await sender.Send(new VoidCommand("v"));

        Assert.Equal("v", Assert.Single(provider.GetRequiredService<Recorder>().Entries));
    }

    [Fact]
    public async Task SendWithoutResponse_RunsBehaviorsWithUnitResponse()
    {
        var log = new List<string>();
        using var provider = BuildProvider(s =>
        {
            AddVoidHandler(s);
            s.AddTransient<IPipelineBehavior<VoidCommand, Unit>>(
                _ => new UnitTrackingBehavior(log));
        });
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new VoidCommand("v"));

        Assert.Equal(["before", "after"], log);
        Assert.Single(provider.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task SendWithoutResponse_NoHandler_Throws()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new VoidCommand("v")));
    }

    [Fact]
    public async Task SendWithoutResponse_CancellationTokenReachesHandler()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var provider = BuildProvider(s => AddVoidHandler(s));
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mediator.Send(new VoidCommand("v"), cts.Token));
    }

    [Fact]
    public async Task SendWithoutResponse_Null_Throws()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Send((VoidCommand)null!));
    }

    // Send(object)

    [Fact]
    public async Task SendObject_RequestWithResponse_ReturnsBoxedResponse()
    {
        using var provider = BuildProvider(s =>
            s.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>());
        var mediator = provider.GetRequiredService<IMediator>();

        object? result = await mediator.Send((object)new TestCommand("o"));

        Assert.Equal("Handled: o", Assert.IsType<TestResult>(result).Value);
    }

    [Fact]
    public async Task SendObject_RequestWithoutResponse_ReturnsUnit()
    {
        using var provider = BuildProvider(s => AddVoidHandler(s));
        var mediator = provider.GetRequiredService<IMediator>();

        object? result = await mediator.Send((object)new VoidCommand("o"));

        Assert.Equal(Unit.Value, Assert.IsType<Unit>(result));
        Assert.Equal("o", Assert.Single(provider.GetRequiredService<Recorder>().Entries));
    }

    [Fact]
    public async Task SendObject_NotARequest_ThrowsArgumentException()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new object()));
    }

    [Fact]
    public async Task SendObject_Null_Throws()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Send((object)null!));
    }

    // Publish<TNotification>(TNotification)

    [Fact]
    public async Task PublishTyped_ReachesHandler()
    {
        var handler = new TestNotificationHandler();
        using var provider = BuildProvider(s => s.AddSingleton<INotificationHandler<TestNotification>>(handler));
        IPublisher publisher = provider.GetRequiredService<IMediator>();

        await publisher.Publish(new TestNotification("n"));

        Assert.Equal("n", Assert.Single(handler.ReceivedMessages));
    }

    // Publish(object)

    [Fact]
    public async Task PublishObject_ReachesHandlerOfRuntimeType()
    {
        var handler = new TestNotificationHandler();
        using var provider = BuildProvider(s => s.AddSingleton<INotificationHandler<TestNotification>>(handler));
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Publish((object)new TestNotification("o"));

        Assert.Equal("o", Assert.Single(handler.ReceivedMessages));
    }

    [Fact]
    public async Task PublishObject_NotANotification_ThrowsArgumentException()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Publish(new object()));
    }

    [Fact]
    public async Task PublishObject_Null_Throws()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => mediator.Publish((object)null!));
    }

    // Unit

    [Fact]
    public async Task Unit_ValueAndTask()
    {
        Assert.Equal(default(Unit), Unit.Value);
        Assert.Equal(Unit.Value, await Unit.Task);
        Assert.Equal("()", Unit.Value.ToString());
        Assert.True(Unit.Value == default);
        Assert.Equal(0, Unit.Value.CompareTo(default));
    }

    private sealed class TokenRecordingBehavior(List<CancellationToken> seen) : IPipelineBehavior<TokenQuery, CancellationToken>
    {
        public Task<CancellationToken> Handle(TokenQuery request, RequestHandlerDelegate<CancellationToken> next, CancellationToken cancellationToken)
        {
            seen.Add(cancellationToken);
            return next();
        }
    }

    private sealed class UnitTrackingBehavior(List<string> log) : IPipelineBehavior<VoidCommand, Unit>
    {
        public async Task<Unit> Handle(VoidCommand request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken)
        {
            log.Add("before");
            var result = await next(cancellationToken);
            log.Add("after");
            return result;
        }
    }
}
