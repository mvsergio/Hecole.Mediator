using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Tests;

public class PipelineOrderingTests
{
    [Fact]
    public async Task Pipeline_BehaviorsExecuteInRegistrationOrder_BeforeAndAfterHandler()
    {
        var log = new List<string>();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, LoggingCommandHandler>();
            s.AddSingleton(log);
            s.AddTransient<IPipelineBehavior<TestCommand, TestResult>>(_ => new TrackingBehavior<TestCommand, TestResult>(log, "First"));
            s.AddTransient<IPipelineBehavior<TestCommand, TestResult>>(_ => new TrackingBehavior<TestCommand, TestResult>(log, "Second"));
        });

        var result = await host.Mediator.Send(new TestCommand("test"));

        Assert.Equal("Handled: test", result.Value);
        Assert.Equal(["First:before", "Second:before", "handler", "Second:after", "First:after"], log);
    }

    [Fact]
    public async Task Pipeline_BehaviorShortCircuits_HandlerNotCalled()
    {
        var log = new List<string>();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TestCommand, TestResult, LoggingCommandHandler>();
            s.AddSingleton(log);
            s.AddTransient<IPipelineBehavior<TestCommand, TestResult>, ShortCircuitBehavior>();
        });

        var result = await host.Mediator.Send(new TestCommand("test"));

        Assert.Equal("short", result.Value);
        Assert.Empty(log);
    }

    [Fact]
    public async Task Pipeline_BehaviorCallsNextWithoutToken_HandlerReceivesCallerToken()
    {
        using var cts = new CancellationTokenSource();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient(typeof(IPipelineBehavior<,>), typeof(OpenGenericTestBehavior<,>));
        });

        Assert.Equal(cts.Token, await host.Mediator.Send(new TokenQuery(), cts.Token));
    }

    [Fact]
    public async Task Pipeline_BehaviorPassesItsOwnToken_HandlerReceivesIt()
    {
        using var caller = new CancellationTokenSource();
        using var own = new CancellationTokenSource();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenReplacingBehavior(own.Token));
        });

        Assert.Equal(own.Token, await host.Mediator.Send(new TokenQuery(), caller.Token));
    }

    [Fact]
    public async Task Pipeline_FourBehaviorsFromCfgAndContainerMixed_RunInRegistrationOrder()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddRequestHandler<TestCommand, TestResult, RecordingCommandHandler>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Tag1Behavior<,>));
        services.AddHecoleMediator(cfg => cfg
            .AddOpenBehavior(typeof(Tag2Behavior<,>))
            .AddBehavior<Tag3CommandBehavior>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(Tag4Behavior<,>));

        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("x"));

        Assert.Equal(
            ["1:before", "2:before", "3:before", "4:before", "handler", "4:after", "3:after", "2:after", "1:after"],
            root.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task Pipeline_ShortCircuit_ReturnsBehaviorResponse_AndHandlerIsNeverCreated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddRequestHandler<TestCommand, TestResult, CreationRecordingCommandHandler>();
        services.AddHecoleMediator(cfg => cfg.AddBehavior<ShortCircuitBehavior>());

        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var recorder = root.GetRequiredService<Recorder>();

        // The handler records its construction, so this proves "not created", not only "not called".
        scope.ServiceProvider.GetRequiredService<IRequestHandler<TestCommand, TestResult>>();
        Assert.Equal(["created"], recorder.Entries);

        var result = await sender.Send(new TestCommand("x"));

        Assert.Equal("short", result.Value);
        Assert.Equal(["created"], recorder.Entries);
    }

    [Fact]
    public async Task Pipeline_ShortCircuit_WithoutAnyHandler_ReturnsBehaviorResponse()
    {
        using var host = TestHost.Build(_ => { }, cfg => cfg.AddBehavior<ShortCircuitBehavior>());

        Assert.Equal("short", (await host.Mediator.Send(new TestCommand("x"))).Value);
    }

    [Fact]
    public async Task Pipeline_BehaviorClosedForOneRequest_RunsOnlyForThatRequest()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddRequestHandler<TestCommand, TestResult, RecordingCommandHandler>();
        services.AddRequestHandler<TestQuery, TestResult, TestQueryHandler>();
        services.AddHecoleMediator(cfg => cfg.AddBehavior<IPipelineBehavior<TestCommand, TestResult>, Tag3CommandBehavior>());

        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var recorder = root.GetRequiredService<Recorder>();

        await sender.Send(new TestQuery(1));
        Assert.Empty(recorder.Entries);

        await sender.Send(new TestCommand("x"));
        Assert.Equal(["3:before", "handler", "3:after"], recorder.Entries);
    }

    [Fact]
    public async Task Pipeline_OpenBehaviorWithExtraConstraint_IsSkippedForRequestsThatDoNotSatisfyIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddRequestHandler<MarkedQuery, string, MarkedQueryHandler>();
        services.AddRequestHandler<UnmarkedQuery, string, UnmarkedQueryHandler>();
        services.AddHecoleMediator(cfg => cfg.AddOpenBehavior(typeof(MarkerBehavior<,>)).AddOpenBehavior(typeof(Tag2Behavior<,>)));

        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = root.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var recorder = root.GetRequiredService<Recorder>();

        Assert.Equal("unmarked", await sender.Send(new UnmarkedQuery()));
        Assert.Equal(["2:before", "2:after"], recorder.Entries);

        Assert.Equal("marked", await sender.Send(new MarkedQuery()));
        Assert.Equal(["2:before", "2:after", "marker", "2:before", "2:after"], recorder.Entries);
    }

    [Fact]
    public async Task Pipeline_BehaviorCallsNextTwice_RunsTheRestOfThePipelineTwice()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddRequestHandler<TestCommand, TestResult, RecordingCommandHandler>();
        services.AddHecoleMediator(cfg => cfg.AddOpenBehavior(typeof(RetryBehavior<,>)).AddOpenBehavior(typeof(Tag2Behavior<,>)));

        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("x"));

        Assert.Equal(["2:before", "handler", "2:after", "2:before", "handler", "2:after"], root.GetRequiredService<Recorder>().Entries);
    }

    // Token rule: next() continues with the token the calling behavior received, so a timeout set by an outer behavior
    // reaches every inner behavior and the handler (before 2.0.1 the inner next() went back to the caller's token).
    [Fact]
    public async Task Pipeline_OuterPassesTimeoutToken_InnerBehaviorsCallNextWithoutToken_HandlerReceivesTimeoutToken()
    {
        using var caller = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var seenByMiddle = new List<CancellationToken>();
        var seenByInner = new List<CancellationToken>();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenReplacingBehavior(timeout.Token));
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenObservingBehavior(seenByMiddle));
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenObservingBehavior(seenByInner));
        });

        var handlerToken = await host.Mediator.Send(new TokenQuery(), caller.Token);

        Assert.Equal(timeout.Token, Assert.Single(seenByMiddle));
        Assert.Equal(timeout.Token, Assert.Single(seenByInner));
        Assert.Equal(timeout.Token, handlerToken);
    }

    [Fact]
    public async Task Pipeline_OuterPassesTimeoutToken_OnlyBehaviorBeforeHandler_HandlerReceivesTimeoutToken()
    {
        using var caller = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        var seenByInner = new List<CancellationToken>();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenReplacingBehavior(timeout.Token));
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenObservingBehavior(seenByInner));
        });

        Assert.Equal(timeout.Token, await host.Mediator.Send(new TokenQuery(), caller.Token));
        Assert.Equal(timeout.Token, Assert.Single(seenByInner));
    }

    // CancellationToken.None cannot be told from an omitted argument: it continues with the token the behavior received.
    [Fact]
    public async Task Pipeline_NextWithCancellationTokenNone_ContinuesWithTheTokenTheBehaviorReceived()
    {
        using var caller = new CancellationTokenSource();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenReplacingBehavior(CancellationToken.None));
        });

        Assert.Equal(caller.Token, await host.Mediator.Send(new TokenQuery(), caller.Token));
    }

    [Fact]
    public async Task Pipeline_InnerNextWithCancellationTokenNone_ContinuesWithTheTokenTheInnerBehaviorReceived_NotTheCallers()
    {
        using var caller = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenReplacingBehavior(timeout.Token));
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenReplacingBehavior(CancellationToken.None));
        });

        Assert.Equal(timeout.Token, await host.Mediator.Send(new TokenQuery(), caller.Token));
    }

    [Fact]
    public async Task Pipeline_RetryWithADifferentTokenPerCall_EachCallRunsWithItsOwnToken()
    {
        using var caller = new CancellationTokenSource();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        var seenByInner = new List<CancellationToken>();
        var handlerTokens = new List<CancellationToken>();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenRetryBehavior(handlerTokens, first.Token, second.Token, default));
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenObservingBehavior(seenByInner));
        });

        await host.Mediator.Send(new TokenQuery(), caller.Token);

        // The third call is next(): the token the retrying behavior received, the caller's.
        Assert.Equal([first.Token, second.Token, caller.Token], seenByInner);
        Assert.Equal([first.Token, second.Token, caller.Token], handlerTokens);
    }

    [Fact]
    public async Task Pipeline_RetryInsideSingleBehavior_EachCallReachesTheHandlerWithItsOwnToken()
    {
        using var caller = new CancellationTokenSource();
        using var first = new CancellationTokenSource();
        var handlerTokens = new List<CancellationToken>();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenRetryBehavior(handlerTokens, first.Token, default));
        });

        await host.Mediator.Send(new TokenQuery(), caller.Token);

        Assert.Equal([first.Token, caller.Token], handlerTokens);
    }

    [Fact]
    public async Task Pipeline_OutermostBehavior_ReceivesExactlyTheCallersToken_InEveryForm()
    {
        using var caller = new CancellationTokenSource();
        var seen = new List<CancellationToken>();
        using var host = TestHost.Build(s =>
        {
            s.AddRequestHandler<TokenQuery, CancellationToken, TokenQueryHandler>();
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenObservingBehavior(seen));
            s.AddTransient<IPipelineBehavior<TokenQuery, CancellationToken>>(_ => new TokenReplacingBehavior(CancellationToken.None));
        });

        Assert.Equal(caller.Token, await host.Mediator.Send(new TokenQuery(), caller.Token));
        Assert.Equal(caller.Token, await host.Mediator.Send((object)new TokenQuery(), caller.Token));

        Assert.Equal([caller.Token, caller.Token], seen);
    }

    [Fact]
    public async Task Pipeline_VoidRequest_FollowsTheSameTokenRule()
    {
        using var caller = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        var outermost = new List<CancellationToken>();
        var handlerTokens = new List<CancellationToken>();
        using var host = TestHost.Build(s =>
        {
            s.AddSingleton(handlerTokens);
            s.AddRequestHandler<VoidTokenCommand, VoidTokenCommandHandler>();
            s.AddTransient<IPipelineBehavior<VoidTokenCommand, Unit>>(_ => new VoidTokenBehavior(outermost, replaceWith: timeout.Token));
            s.AddTransient<IPipelineBehavior<VoidTokenCommand, Unit>>(_ => new VoidTokenBehavior([], replaceWith: default));
        });

        await host.Mediator.Send(new VoidTokenCommand(), caller.Token);
        await host.Mediator.Send((object)new VoidTokenCommand(), caller.Token);

        Assert.Equal([caller.Token, caller.Token], outermost);
        Assert.Equal([timeout.Token, timeout.Token], handlerTokens);
    }

    [Fact]
    public async Task Pipeline_TransientBehavior_IsCreatedPerSend_ScopedBehavior_PerScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddRequestHandler<TestQuery, TestResult, TestQueryHandler>();
        services.AddRequestHandler<TestCommand, TestResult, TestCommandHandler>();
        services.AddHecoleMediator(cfg => cfg
            .AddBehavior<IPipelineBehavior<TestQuery, TestResult>, InstanceBehavior<TestQuery>>()
            .AddBehavior<IPipelineBehavior<TestCommand, TestResult>, InstanceBehavior<TestCommand>>(ServiceLifetime.Scoped));

        using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var recorder = root.GetRequiredService<Recorder>();

        using (var scope = root.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new TestQuery(1));
            await sender.Send(new TestQuery(2));
            await sender.Send(new TestCommand("a"));
            await sender.Send(new TestCommand("b"));
        }

        using (var scope = root.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TestCommand("c"));

        var queries = recorder.Entries.Where(e => e.StartsWith("TestQuery", StringComparison.Ordinal)).ToList();
        var commands = recorder.Entries.Where(e => e.StartsWith("TestCommand", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, queries.Distinct().Count());
        Assert.Equal(3, commands.Count);
        Assert.Equal(commands[0], commands[1]);
        Assert.NotEqual(commands[1], commands[2]);
    }

    private sealed class RecordingCommandHandler(Recorder recorder) : IRequestHandler<TestCommand, TestResult>
    {
        public Task<TestResult> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            recorder.Add("handler");
            return Task.FromResult(new TestResult($"Handled: {request.Name}"));
        }
    }

    private sealed class CreationRecordingCommandHandler : IRequestHandler<TestCommand, TestResult>
    {
        private readonly Recorder _recorder;

        public CreationRecordingCommandHandler(Recorder recorder)
        {
            _recorder = recorder;
            recorder.Add("created");
        }

        public Task<TestResult> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            _recorder.Add("handler");
            return Task.FromResult(new TestResult($"Handled: {request.Name}"));
        }
    }

    private abstract class TaggedBehavior<TRequest, TResponse>(Recorder recorder, string tag) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            recorder.Add($"{tag}:before");
            var response = await next();
            recorder.Add($"{tag}:after");
            return response;
        }
    }

    private sealed class Tag1Behavior<TRequest, TResponse>(Recorder recorder) : TaggedBehavior<TRequest, TResponse>(recorder, "1")
        where TRequest : notnull;

    private sealed class Tag2Behavior<TRequest, TResponse>(Recorder recorder) : TaggedBehavior<TRequest, TResponse>(recorder, "2")
        where TRequest : notnull;

    private sealed class Tag3CommandBehavior(Recorder recorder) : TaggedBehavior<TestCommand, TestResult>(recorder, "3");

    private sealed class Tag4Behavior<TRequest, TResponse>(Recorder recorder) : TaggedBehavior<TRequest, TResponse>(recorder, "4")
        where TRequest : notnull;

    private sealed class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            await next();
            return await next();
        }
    }

    public interface IMarker;

    public sealed record MarkedQuery : IRequest<string>, IMarker;

    public sealed record UnmarkedQuery : IRequest<string>;

    private sealed class MarkedQueryHandler : IRequestHandler<MarkedQuery, string>
    {
        public Task<string> Handle(MarkedQuery request, CancellationToken cancellationToken) => Task.FromResult("marked");
    }

    private sealed class UnmarkedQueryHandler : IRequestHandler<UnmarkedQuery, string>
    {
        public Task<string> Handle(UnmarkedQuery request, CancellationToken cancellationToken) => Task.FromResult("unmarked");
    }

    private sealed class MarkerBehavior<TRequest, TResponse>(Recorder recorder) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IMarker
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            recorder.Add("marker");
            return next();
        }
    }

    private sealed class TokenObservingBehavior(List<CancellationToken> seen) : IPipelineBehavior<TokenQuery, CancellationToken>
    {
        public Task<CancellationToken> Handle(TokenQuery request, RequestHandlerDelegate<CancellationToken> next, CancellationToken cancellationToken)
        {
            seen.Add(cancellationToken);
            return next();
        }
    }

    private sealed class InstanceBehavior<TRequest>(Recorder recorder) : IPipelineBehavior<TRequest, TestResult>
        where TRequest : notnull
    {
        private readonly Guid _id = Guid.NewGuid();

        public Task<TestResult> Handle(TRequest request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken)
        {
            recorder.Add($"{typeof(TRequest).Name}:{_id}");
            return next();
        }
    }

    private sealed class LoggingCommandHandler(List<string> log) : IRequestHandler<TestCommand, TestResult>
    {
        public Task<TestResult> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            log.Add("handler");
            return Task.FromResult(new TestResult($"Handled: {request.Name}"));
        }
    }

    private sealed class ShortCircuitBehavior : IPipelineBehavior<TestCommand, TestResult>
    {
        public Task<TestResult> Handle(TestCommand request, RequestHandlerDelegate<TestResult> next, CancellationToken cancellationToken)
            => Task.FromResult(new TestResult("short"));
    }

    private sealed class TokenReplacingBehavior(CancellationToken token) : IPipelineBehavior<TokenQuery, CancellationToken>
    {
        public Task<CancellationToken> Handle(TokenQuery request, RequestHandlerDelegate<CancellationToken> next, CancellationToken cancellationToken)
            => next(token);
    }

    /// <summary>Calls next once per token given, in order, and records the token each call reached the handler with.</summary>
    private sealed class TokenRetryBehavior(List<CancellationToken> handlerTokens, params CancellationToken[] tokens) : IPipelineBehavior<TokenQuery, CancellationToken>
    {
        public async Task<CancellationToken> Handle(TokenQuery request, RequestHandlerDelegate<CancellationToken> next, CancellationToken cancellationToken)
        {
            var last = default(CancellationToken);
            foreach (var token in tokens)
            {
                last = await next(token);
                handlerTokens.Add(last);
            }

            return last;
        }
    }

    public sealed record VoidTokenCommand : IRequest;

    private sealed class VoidTokenCommandHandler(List<CancellationToken> tokens) : IRequestHandler<VoidTokenCommand>
    {
        public Task Handle(VoidTokenCommand request, CancellationToken cancellationToken)
        {
            tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class VoidTokenBehavior(List<CancellationToken> seen, CancellationToken replaceWith) : IPipelineBehavior<VoidTokenCommand, Unit>
    {
        public Task<Unit> Handle(VoidTokenCommand request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken)
        {
            seen.Add(cancellationToken);
            return next(replaceWith);
        }
    }
}
