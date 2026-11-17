using Hecole.Mediator;
using Hecole.Mediator.Behaviors;
using Hecole.Mediator.Benchmarks;
using Hecole.Mediator.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// Each scenario gets its own container; the mediator is resolved once from a scope and reused, as in the 1.3.0 baseline.
// Handlers and behaviors do no work, so the numbers are the cost of the mediator and the container.
Harness.RaisePriority();

var command = new Command();
var voidCommand = new VoidCommand();
var note = new Note();
var direct = new CommandHandler();

var scenarios = new List<(string Key, Func<Task> Operation)>
{
    ("direct-handler", () => direct.Handle(command, default)),
};

foreach (var behaviors in new[] { 0, 1, 4 })
{
    var mediator = Build(services =>
    {
        services.AddRequestHandler<Command, string, CommandHandler>();
        services.AddHecoleMediator(cfg =>
        {
            for (var i = 0; i < behaviors; i++)
                cfg.AddOpenBehavior(PassBehaviors.Types[i]);
        });
    });
    scenarios.Add(($"send-{behaviors}", () => mediator.Send(command)));
}

var objectMediator = Build(services =>
{
    services.AddRequestHandler<Command, string, CommandHandler>();
    services.AddHecoleMediator(_ => { });
});
object boxed = command;
scenarios.Add(("send-object", () => objectMediator.Send(boxed)));

var voidMediator = Build(services =>
{
    services.AddRequestHandler<VoidCommand, VoidCommandHandler>();
    services.AddHecoleMediator(_ => { });
});
scenarios.Add(("send-void", () => voidMediator.Send(voidCommand)));

var scanMediator = Build(services => services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<Command>()));
scenarios.Add(("send-scan", () => scanMediator.Send(command)));

var containerMediator = Build(services =>
{
    services.AddTransient<IRequestHandler<Command, string>, CommandHandler>();
    services.AddHecoleMediator(_ => { });
});
scenarios.Add(("send-container", () => containerMediator.Send(command)));

foreach (var (name, publisher) in new (string, INotificationPublisher)[] { ("foreach", new ForeachAwaitPublisher()), ("whenall", new TaskWhenAllPublisher()) })
{
    foreach (var handlers in new[] { 1, 3 })
    {
        var mediator = Build(services =>
        {
            services.AddHecoleMediator(cfg => cfg.NotificationPublisher = publisher);
            services.AddNotificationHandler<Note, NoteHandler1>();
            if (handlers == 3)
            {
                services.AddNotificationHandler<Note, NoteHandler2>();
                services.AddNotificationHandler<Note, NoteHandler3>();
            }
        });
        scenarios.Add(($"publish-{handlers}-{name}", () => mediator.Publish(note)));
    }
}

var builtInMediator = Build(services =>
{
    // Logging off: every ILogger<T> is a NullLogger<T> (IsEnabled returns false).
    services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
    services.AddRequestHandler<Command, string, CommandHandler>();
    services.AddHecoleMediator(cfg => cfg
        .AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>))
        .AddOpenBehavior(typeof(LoggingBehavior<,>))
        .AddOpenBehavior(typeof(PerformanceBehavior<,>))
        .AddOpenBehavior(typeof(ValidationBehavior<,>)));
});
scenarios.Add(("builtin-4", () => builtInMediator.Send(command)));

var only = args.Length > 0 ? new HashSet<string>(args, StringComparer.Ordinal) : null;

Console.WriteLine($"version=2.0 runtime={Environment.Version} os={Environment.OSVersion.VersionString} cpu={Environment.ProcessorCount} ops={Harness.Operations} rounds={Harness.Rounds} (median)");
foreach (var (key, operation) in scenarios)
{
    if (only is not null && !only.Contains(key))
        continue;
    var (ns, bytes) = await Harness.Measure(operation).ConfigureAwait(false);
    Console.WriteLine($"{key,-20} | {ns,8:F0} ns/op | {bytes,6} B/op");
}

static IMediator Build(Action<IServiceCollection> configure)
{
    var services = new ServiceCollection();
    configure(services);
    return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMediator>();
}

namespace Hecole.Mediator.Benchmarks
{
    public sealed record Command : IRequest<string>;

    public sealed record VoidCommand : IRequest;

    public sealed record Note : INotification;

    public sealed class CommandHandler : IRequestHandler<Command, string>
    {
        private static readonly Task<string> Done = Task.FromResult("ok");

        public Task<string> Handle(Command request, CancellationToken cancellationToken) => Done;
    }

    public sealed class VoidCommandHandler : IRequestHandler<VoidCommand>
    {
        public Task Handle(VoidCommand request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class NoteHandler1 : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class NoteHandler2 : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class NoteHandler3 : INotificationHandler<Note>
    {
        public Task Handle(Note notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    // Four distinct pass-through behaviors: the same type registered four times would be added once (TryAddEnumerable).
    internal static class PassBehaviors
    {
        public static readonly Type[] Types = [typeof(Pass1<,>), typeof(Pass2<,>), typeof(Pass3<,>), typeof(Pass4<,>)];
    }

    public sealed class Pass1<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public sealed class Pass2<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public sealed class Pass3<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }

    public sealed class Pass4<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) => next();
    }
}
