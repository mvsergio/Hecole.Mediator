using System.ComponentModel.DataAnnotations;
using Hecole.Mediator.Behaviors;
using Hecole.Mediator.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hecole.Mediator.Tests;

public class LogBehaviorTests
{
    public sealed record LoginCommand([property: Required] string? Password) : IRequest<string>;

    public sealed class BoomException(string message) : Exception(message);

    private const string Secret = "s3cr3t-P4ssw0rd-value";

    private static RequestHandlerDelegate<string> Returning(string value) => _ => Task.FromResult(value);

    private static RequestHandlerDelegate<string> Throwing(Exception exception) => _ => Task.FromException<string>(exception);

    private static RequestHandlerDelegate<string> CanceledBy(CancellationToken token) => _ => Task.FromCanceled<string>(token);

    private static CancellationToken CanceledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    private static async Task<string> ThrowFromDeepInside()
    {
        await Task.Yield();
        throw new BoomException("deep failure");
    }

    // ---------- LoggingBehavior ----------

    [Fact]
    public async Task Logging_Success_LogsStartAndFinishWithNameAndTime()
    {
        var logger = new FakeLogger<LoggingBehavior<LoginCommand, string>>();
        var behavior = new LoggingBehavior<LoginCommand, string>(logger);

        var result = await behavior.Handle(new LoginCommand(Secret), Returning("ok"), default);

        Assert.Equal("ok", result);
        var entries = logger.Sink.Entries;
        Assert.Equal([1001, 1002], entries.Select(e => e.EventId.Id));
        Assert.Equal(["RequestStarting", "RequestFinished"], entries.Select(e => e.EventId.Name));
        Assert.All(entries, e => Assert.Equal(LogLevel.Information, e.Level));
        Assert.All(entries, e => Assert.Contains("LoginCommand", e.Message, StringComparison.Ordinal));
        Assert.All(entries, e => Assert.Null(e.Exception));
        var elapsed = Assert.IsType<double>(entries[1].Values.Single(v => v.Key == "ElapsedMilliseconds").Value);
        Assert.True(elapsed >= 0);
    }

    [Fact]
    public async Task Logging_Exception_LogsFailedAndRethrowsSameException()
    {
        var logger = new FakeLogger<LoggingBehavior<LoginCommand, string>>();
        var behavior = new LoggingBehavior<LoginCommand, string>(logger);
        var boom = new BoomException("failure");

        var thrown = await Assert.ThrowsAsync<BoomException>(() => behavior.Handle(new LoginCommand(Secret), Throwing(boom), default));

        Assert.Same(boom, thrown);
        var entries = logger.Sink.Entries;
        Assert.Equal([1001, 1003], entries.Select(e => e.EventId.Id));
        Assert.Contains(nameof(BoomException), entries[1].Message, StringComparison.Ordinal);
        Assert.Null(entries[1].Exception);
    }

    [Fact]
    public async Task Logging_CallerCanceled_LogsCanceled()
    {
        var logger = new FakeLogger<LoggingBehavior<LoginCommand, string>>();
        var behavior = new LoggingBehavior<LoginCommand, string>(logger);
        var token = CanceledToken();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(new LoginCommand(Secret), CanceledBy(token), token));

        Assert.Equal([1001, 1004], logger.Sink.Entries.Select(e => e.EventId.Id));
    }

    [Fact]
    public async Task Logging_CanceledWithoutCallerToken_IsAFailure()
    {
        var logger = new FakeLogger<LoggingBehavior<LoginCommand, string>>();
        var behavior = new LoggingBehavior<LoginCommand, string>(logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(new LoginCommand(Secret), CanceledBy(CanceledToken()), default));

        Assert.Equal([1001, 1003], logger.Sink.Entries.Select(e => e.EventId.Id));
    }

    [Fact]
    public async Task Logging_LevelDisabled_WritesNothing()
    {
        var logger = new FakeLogger<LoggingBehavior<LoginCommand, string>>();
        logger.Sink.MinimumLevel = LogLevel.Warning;
        var behavior = new LoggingBehavior<LoginCommand, string>(logger);

        await behavior.Handle(new LoginCommand(Secret), Returning("ok"), default);

        Assert.Empty(logger.Sink.Entries);
        Assert.Equal(2, logger.Sink.IsEnabledCalls);
    }

    // ---------- PerformanceBehavior ----------

    [Fact]
    public void PerformanceOptions_DefaultThresholdIs500Ms_AndNegativeIsRefused()
    {
        var options = new PerformanceBehaviorOptions();

        Assert.Equal(TimeSpan.FromMilliseconds(500), options.Threshold);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Threshold = TimeSpan.FromMilliseconds(-1));
    }

    [Fact]
    public async Task Performance_DefaultThreshold_FastRequest_LogsNothing()
    {
        var logger = new FakeLogger<PerformanceBehavior<LoginCommand, string>>();
        var behavior = new PerformanceBehavior<LoginCommand, string>(logger);

        await behavior.Handle(new LoginCommand(Secret), Returning("ok"), default);

        Assert.Empty(logger.Sink.Entries);
    }

    [Fact]
    public async Task Performance_DefaultThreshold_SlowRequest_LogsWarning()
    {
        var logger = new FakeLogger<PerformanceBehavior<LoginCommand, string>>();
        var behavior = new PerformanceBehavior<LoginCommand, string>(logger);

        await behavior.Handle(new LoginCommand(Secret), async _ =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            return "ok";
        }, default);

        var entry = Assert.Single(logger.Sink.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(new EventId(2001, "SlowRequest"), entry.EventId);
        Assert.Equal(500d, entry.Values.Single(v => v.Key == "ThresholdMilliseconds").Value);
        Assert.True((double)entry.Values.Single(v => v.Key == "ElapsedMilliseconds").Value! > 500);
        Assert.Contains("LoginCommand", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Performance_ConfiguredThreshold_IsUsed()
    {
        var zero = new FakeLogger<PerformanceBehavior<LoginCommand, string>>();
        var hour = new FakeLogger<PerformanceBehavior<LoginCommand, string>>();
        async Task<string> Slow(CancellationToken _)
        {
            await Task.Delay(20);
            return "ok";
        }

        await new PerformanceBehavior<LoginCommand, string>(zero, new PerformanceBehaviorOptions { Threshold = TimeSpan.Zero })
            .Handle(new LoginCommand(Secret), Slow, default);
        await new PerformanceBehavior<LoginCommand, string>(hour, new PerformanceBehaviorOptions { Threshold = TimeSpan.FromHours(1) })
            .Handle(new LoginCommand(Secret), Slow, default);

        Assert.Equal(2001, Assert.Single(zero.Sink.Entries).EventId.Id);
        Assert.Empty(hour.Sink.Entries);
    }

    [Fact]
    public async Task Performance_ExceptionAndCancellation_StillMeasuredAndPropagated()
    {
        var logger = new FakeLogger<PerformanceBehavior<LoginCommand, string>>();
        var behavior = new PerformanceBehavior<LoginCommand, string>(logger, new PerformanceBehaviorOptions { Threshold = TimeSpan.Zero });
        var boom = new BoomException("failure");
        var token = CanceledToken();

        var thrown = await Assert.ThrowsAsync<BoomException>(() => behavior.Handle(new LoginCommand(Secret), async _ =>
        {
            await Task.Delay(5);
            throw boom;
        }, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(new LoginCommand(Secret), async _ =>
        {
            await Task.Delay(5);
            token.ThrowIfCancellationRequested();
            return "";
        }, token));

        Assert.Same(boom, thrown);
        Assert.Equal([2001, 2001], logger.Sink.Entries.Select(e => e.EventId.Id));
        Assert.All(logger.Sink.Entries, e => Assert.Null(e.Exception));
    }

    [Fact]
    public async Task Performance_ThroughContainer_OptionsResolvedWhenRegistered_DefaultOtherwise()
    {
        using var configured = TestHost.Build(services => services
            .AddSingleton<LogSink>()
            .AddSingleton(typeof(ILogger<>), typeof(FakeLogger<>))
            .AddSingleton(new PerformanceBehaviorOptions { Threshold = TimeSpan.Zero })
            .AddRequestHandler<SlowQuery, string, SlowQueryHandler>(),
            cfg => cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>)));
        using var defaults = TestHost.Build(services => services
            .AddSingleton<LogSink>()
            .AddSingleton(typeof(ILogger<>), typeof(FakeLogger<>))
            .AddRequestHandler<SlowQuery, string, SlowQueryHandler>(),
            cfg => cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>)));

        await configured.Mediator.Send(new SlowQuery());
        await defaults.Mediator.Send(new SlowQuery());

        Assert.Equal(2001, Assert.Single(configured.GetRequiredService<LogSink>().Entries).EventId.Id);
        Assert.Empty(defaults.GetRequiredService<LogSink>().Entries);
    }

    public sealed record SlowQuery : IRequest<string>;

    public sealed class SlowQueryHandler : IRequestHandler<SlowQuery, string>
    {
        public async Task<string> Handle(SlowQuery request, CancellationToken cancellationToken)
        {
            await Task.Delay(20, cancellationToken);
            return "slow";
        }
    }

    // ---------- UnhandledExceptionBehavior ----------

    [Fact]
    public async Task Unhandled_Success_LogsNothing()
    {
        var logger = new FakeLogger<UnhandledExceptionBehavior<LoginCommand, string>>();
        var behavior = new UnhandledExceptionBehavior<LoginCommand, string>(logger);

        var result = await behavior.Handle(new LoginCommand(Secret), Returning("ok"), default);

        Assert.Equal("ok", result);
        Assert.Empty(logger.Sink.Entries);
    }

    [Fact]
    public async Task Unhandled_Exception_LogsErrorAndRethrowsSameExceptionWithOriginalStackTrace()
    {
        var logger = new FakeLogger<UnhandledExceptionBehavior<LoginCommand, string>>();
        var behavior = new UnhandledExceptionBehavior<LoginCommand, string>(logger);

        var thrown = await Assert.ThrowsAsync<BoomException>(() => behavior.Handle(new LoginCommand(Secret), _ => ThrowFromDeepInside(), default));

        var entry = Assert.Single(logger.Sink.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(new EventId(3001, "UnhandledException"), entry.EventId);
        Assert.Same(thrown, entry.Exception);
        Assert.Contains("LoginCommand", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowFromDeepInside), thrown.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhandled_CallerCanceled_IsNotAnError()
    {
        var logger = new FakeLogger<UnhandledExceptionBehavior<LoginCommand, string>>();
        var behavior = new UnhandledExceptionBehavior<LoginCommand, string>(logger);
        var token = CanceledToken();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(new LoginCommand(Secret), CanceledBy(token), token));

        var entry = Assert.Single(logger.Sink.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal(new EventId(3002, "RequestCanceledByCaller"), entry.EventId);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task Unhandled_CanceledWithoutCallerToken_IsAnError()
    {
        var logger = new FakeLogger<UnhandledExceptionBehavior<LoginCommand, string>>();
        var behavior = new UnhandledExceptionBehavior<LoginCommand, string>(logger);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(new LoginCommand(Secret), CanceledBy(CanceledToken()), default));

        var entry = Assert.Single(logger.Sink.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(thrown, entry.Exception);
    }

    // ---------- All four in the pipeline ----------

    public sealed record SecretCommand([property: Required, MinLength(3)] string? Password, bool Fail = false) : IRequest<string>;

    public sealed record SecretVoidCommand([property: Required] string? Password) : IRequest;

    public sealed class SecretHandler(Recorder recorder) : IRequestHandler<SecretCommand, string>, IRequestHandler<SecretVoidCommand>
    {
        public Task<string> Handle(SecretCommand request, CancellationToken cancellationToken)
        {
            recorder.Add("handled");
            return request.Fail
                ? Task.FromException<string>(new BoomException("handler failed"))
                : Task.FromResult("done:" + request.Password!.Length);
        }

        public Task Handle(SecretVoidCommand request, CancellationToken cancellationToken)
        {
            recorder.Add("void");
            return Task.CompletedTask;
        }
    }

    private static TestHost BuildFullPipeline() => TestHost.Build(services => services
        .AddSingleton<Recorder>()
        .AddSingleton<LogSink>()
        .AddSingleton(typeof(ILogger<>), typeof(FakeLogger<>))
        .AddSingleton(new PerformanceBehaviorOptions { Threshold = TimeSpan.Zero })
        .AddRequestHandler<SecretCommand, string, SecretHandler>()
        .AddRequestHandler<SecretVoidCommand, SecretHandler>(),
        cfg => cfg
            .AddOpenBehavior(typeof(LoggingBehavior<,>))
            .AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>))
            .AddOpenBehavior(typeof(PerformanceBehavior<,>))
            .AddOpenBehavior(typeof(ValidationBehavior<,>)));

    [Fact]
    public async Task AllFour_ThroughMediator_ValidRequest()
    {
        using var host = BuildFullPipeline();

        var result = await host.Mediator.Send(new SecretCommand(Secret));
        await host.Mediator.Send(new SecretVoidCommand(Secret));

        Assert.Equal("done:" + Secret.Length, result);
        Assert.Equal(["handled", "void"], host.GetRequiredService<Recorder>().Entries);
        Assert.Equal([1001, 2001, 1002, 1001, 2001, 1002], host.GetRequiredService<LogSink>().Entries.Select(e => e.EventId.Id));
    }

    [Fact]
    public async Task AllFour_ThroughMediator_InvalidRequest_HandlerNotCalledAndFailureLogged()
    {
        using var host = BuildFullPipeline();

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => host.Mediator.Send(new SecretCommand("ab")));

        Assert.Single(ex.Errors);
        Assert.Empty(host.GetRequiredService<Recorder>().Entries);
        var entries = host.GetRequiredService<LogSink>().Entries;
        Assert.Equal([1001, 2001, 3001, 1003], entries.Select(e => e.EventId.Id));
        Assert.Same(ex, entries.Single(e => e.EventId.Id == 3001).Exception);
    }

    [Fact]
    public async Task AllFour_ThroughMediator_HandlerThrows()
    {
        using var host = BuildFullPipeline();

        await Assert.ThrowsAsync<BoomException>(() => host.Mediator.Send(new SecretCommand(Secret, Fail: true)));

        Assert.Equal([1001, 2001, 3001, 1003], host.GetRequiredService<LogSink>().Entries.Select(e => e.EventId.Id));
    }

    [Fact]
    public async Task AllFour_RequestPropertyValue_NeverAppearsInAnyLog()
    {
        using var host = BuildFullPipeline();
        var token = CanceledToken();

        await host.Mediator.Send(new SecretCommand(Secret));
        await host.Mediator.Send(new SecretVoidCommand(Secret));
        await Assert.ThrowsAsync<BoomException>(() => host.Mediator.Send(new SecretCommand(Secret, Fail: true)));
        await Assert.ThrowsAsync<RequestValidationException>(() => host.Mediator.Send(new SecretCommand("s3")));
        await Assert.ThrowsAsync<RequestValidationException>(() => host.Mediator.Send(new SecretVoidCommand(null)));

        var entries = host.GetRequiredService<LogSink>().Entries;
        Assert.NotEmpty(entries);
        Assert.Contains(entries, e => e.Exception is not null);
        foreach (var entry in entries)
        {
            Assert.DoesNotContain(Secret, entry.Message, StringComparison.Ordinal);
            foreach (var value in entry.Values)
            {
                Assert.IsNotType<SecretCommand>(value.Value);
                Assert.IsNotType<SecretVoidCommand>(value.Value);
                Assert.DoesNotContain(Secret, Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "", StringComparison.Ordinal);
            }

            if (entry.Exception is not null)
                Assert.DoesNotContain(Secret, entry.Exception.ToString(), StringComparison.Ordinal);
        }
    }
}
