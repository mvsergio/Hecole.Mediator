# Hecole.Mediator

[Português](README.pt-BR.md)

In-process mediator for .NET 10 and later. A request goes to exactly one handler, a notification goes to any number of
handlers, and pipeline behaviors wrap every request. The public contracts are `IRequest`, `IRequestHandler`, `ISender`,
`IPublisher`, `IMediator`, `IPipelineBehavior` and `INotificationPublisher`.

It is for applications that use `Microsoft.Extensions.DependencyInjection` and want configuration mistakes, such as two
handlers for one request, reported at registration or at start-up. MIT license.

## Contents

- [Installation](#installation)
- [Quick start](#quick-start)
- [Requests without a response](#requests-without-a-response)
- [Notifications and publishers](#notifications-and-publishers)
- [Pipeline behaviors](#pipeline-behaviors)
- [Built-in behaviors (optional)](#built-in-behaviors-optional)
- [Start-up verification](#start-up-verification)
- [Analyzers](#analyzers)
- [Behavior notes](#behavior-notes)
- [Not supported yet](#not-supported-yet)
- [Upgrading from 1.x](#upgrading-from-1x)
- [Requirements](#requirements)
- [More documentation](#more-documentation)
- [License](#license)

## Installation

```bash
dotnet add package Hecole.Mediator
```

The package contains the library and a Roslyn analyzer. Contracts are in the `Hecole.Mediator` namespace, the
registration methods in `Microsoft.Extensions.DependencyInjection`, the optional behaviors in
`Hecole.Mediator.Behaviors` and the notification publishers in `Hecole.Mediator.NotificationPublishers`.

## Quick start

A request, its response and its handler:

```csharp
public sealed record CreateOrder(string Customer, decimal Total) : IRequest<OrderId>;

public sealed record OrderId(Guid Value);

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId>
{
    public Task<OrderId> Handle(CreateOrder request, CancellationToken cancellationToken)
        => Task.FromResult(new OrderId(Guid.NewGuid()));
}
```

Register the handlers by scanning an assembly. Only the assemblies you name are scanned; concrete, non-generic
request and notification handlers are registered as transient. Pipeline behaviors are not scanned.

```csharp
services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrder>());
```

Or register each handler explicitly, without scanning. `AddRequestHandler` and `AddNotificationHandler` can be called
before or after `AddHecoleMediator`, and both forms can be mixed.

```csharp
services.AddHecoleMediator(_ => { });
services.AddRequestHandler<CreateOrder, OrderId, CreateOrderHandler>();
services.AddRequestHandler<CancelOrder, CancelOrderHandler>();
services.AddNotificationHandler<OrderCreated, SendConfirmationEmail>();
services.AddNotificationHandler<OrderCreated, UpdateSalesReport>();
```

Send the request through `ISender` (or `IMediator`, which is `ISender` plus `IPublisher`):

```csharp
public sealed class OrdersEndpoint(ISender sender)
{
    public Task<OrderId> Create(string customer, decimal total, CancellationToken cancellationToken)
        => sender.Send(new CreateOrder(customer, total), cancellationToken);
}
```

Registration rules:

| Rule | Detail |
|---|---|
| One handler per request | Two different handlers for the same request throw `InvalidOperationException` in `AddHecoleMediator` or `AddRequestHandler`, with both type names |
| No duplicates | The same assembly, handler or behavior registered again (by scan, explicitly, or in another `AddHecoleMediator` call) is registered once |
| `AddHecoleMediator` more than once | Allowed (for example, once per module). Every call must use the same `Lifetime`, `RequireScope` and notification publisher, or it throws |
| Exact type | A request is dispatched by its runtime type. A derived request without its own handler does not use the handler of its base type |
| Lifetimes | Handlers are transient. `IMediator`, `ISender` and `IPublisher` are transient by default; `cfg.Lifetime` changes that |
| Handler registered directly in the container | `services.AddTransient<IRequestHandler<A, B>, H>()` also works |
| `RequireScope` (optional, off by default) | When `true`, `Send` and `Publish` throw if the mediator was resolved from the root provider instead of a scope. Cannot be combined with `Lifetime = Singleton` |

## Requests without a response

```csharp
public sealed record CancelOrder(Guid OrderId) : IRequest;

public sealed class CancelOrderHandler : IRequestHandler<CancelOrder>
{
    public Task Handle(CancelOrder request, CancellationToken cancellationToken)
    {
        // Cancel the order.
        return Task.CompletedTask;
    }
}
```

```csharp
await sender.Send(new CancelOrder(orderId.Value), cancellationToken);
```

Inside the pipeline, a request without a response has `TResponse = Unit`, so the same behaviors run for both kinds of
request. When the request type is only known at run time, `Send(object)` returns the response, or `Unit.Value` for a
request without one:

```csharp
object request = new CreateOrder("ACME", 10m);
object? response = await sender.Send(request, cancellationToken); // an OrderId
object? none = await sender.Send((object)new CancelOrder(orderId.Value), cancellationToken); // Unit.Value
```

`Send(object)` throws `ArgumentException` when the object is not a request.

## Notifications and publishers

```csharp
public sealed record OrderCreated(Guid OrderId) : INotification;

public sealed class SendConfirmationEmail : INotificationHandler<OrderCreated>
{
    public Task Handle(OrderCreated notification, CancellationToken cancellationToken)
    {
        // Send the e-mail.
        return Task.CompletedTask;
    }
}

public sealed class UpdateSalesReport : INotificationHandler<OrderCreated>
{
    public Task Handle(OrderCreated notification, CancellationToken cancellationToken)
    {
        // Update the report.
        return Task.CompletedTask;
    }
}
```

```csharp
await publisher.Publish(new OrderCreated(orderId.Value), cancellationToken);
```

`IPublisher.Publish` also has a `Publish(object)` form. The `INotificationPublisher` decides how the handlers run:

| Publisher | Behavior |
|---|---|
| `ForeachAwaitPublisher` (default) | One handler at a time, in registration order, each awaited. The first exception stops the rest and reaches the caller |
| `TaskWhenAllPublisher` | Starts every handler, then awaits all of them. The returned task carries every exception. A handler that throws before returning its task does not stop the others |
| Your own | Implement `INotificationPublisher` and set `cfg.NotificationPublisherType` (resolved from the container) or `cfg.NotificationPublisher` (an instance) |

```csharp
services.AddHecoleMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrder>();
    cfg.NotificationPublisher = new TaskWhenAllPublisher();
});
```

A notification with no handler does nothing. Handlers of a base type or of `INotification` do not run for a derived
notification (exact type). The same handler class registered twice runs once.

## Pipeline behaviors

A behavior wraps the handler. It can run code before and after it, change the response, or return without calling
`next` (the handler is then not created).

```csharp
public sealed class StopwatchBehavior<TRequest, TResponse>(ILogger<StopwatchBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var response = await next();
        logger.LogInformation("{Request} took {Elapsed} ms", typeof(TRequest).Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return response;
    }
}
```

Behaviors run in registration order: the first one registered is the outermost.

```csharp
services.AddHecoleMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrder>();
    cfg.AddOpenBehavior(typeof(StopwatchBehavior<,>)); // outermost
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)); // runs after StopwatchBehavior, before the handler
});
```

| Topic | Rule |
|---|---|
| Registration | `cfg.AddOpenBehavior(typeof(B<,>))`, `cfg.AddOpenBehaviors(...)`, `cfg.AddBehavior<TImplementation>()`, `cfg.AddBehavior<TService, TImplementation>()`; each takes an optional `ServiceLifetime` (transient by default). `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(B<,>))` also works and keeps its place in the order |
| Constraints | An open behavior whose generic constraints do not fit a request is skipped for that request |
| Open behavior shape | Exactly two type parameters, declared as `B<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>`. Other shapes are rejected by `AddOpenBehavior`; register them closed with `AddBehavior` |
| Cancellation token | `next()` passes the caller's token on; `next(token)` passes `token` |
| Instances | Behaviors are resolved from the container on every `Send`, so their lifetimes are respected |

## Built-in behaviors (optional)

Four behaviors ship in `Hecole.Mediator.Behaviors`. None is registered unless you add it.

```csharp
services.AddHecoleMediator(cfg => cfg
    .RegisterServicesFromAssemblyContaining<CreateOrder>()
    .AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>))
    .AddOpenBehavior(typeof(LoggingBehavior<,>))
    .AddOpenBehavior(typeof(PerformanceBehavior<,>))
    .AddOpenBehavior(typeof(ValidationBehavior<,>)));
services.AddSingleton(new PerformanceBehaviorOptions { Threshold = TimeSpan.FromSeconds(1) });
```

| Behavior | What it does | Log events |
|---|---|---|
| `ValidationBehavior<,>` | Validates the request with `System.ComponentModel.DataAnnotations` (property attributes, then class attributes and `IValidatableObject`). On failure throws `RequestValidationException` (a `ValidationException`) with every error in `Errors`; the handler does not run | — |
| `LoggingBehavior<,>` | Logs start, end, failure (exception type name only) and cancellation, with the elapsed time | 1001, 1002, 1003, 1004 (Information) |
| `PerformanceBehavior<,>` | Logs a warning when a request takes longer than `PerformanceBehaviorOptions.Threshold` (500 ms by default) | 2001 (Warning) |
| `UnhandledExceptionBehavior<,>` | Logs the exception and rethrows it. Cancellation requested by the caller is logged at Debug | 3001 (Error), 3002 (Debug) |

The request object is never written to the log: only its type name. The logging behaviors need `ILogger<T>` in the
container (`services.AddLogging()`, which the ASP.NET Core and Generic Host builders already call).

Validation attributes on a positional record go on the property:

```csharp
public sealed record RegisterCustomer(
    [property: Required, EmailAddress] string Email,
    [property: Range(18, 130)] int Age) : IRequest<Guid>;

public sealed class RegisterCustomerHandler : IRequestHandler<RegisterCustomer, Guid>
{
    public Task<Guid> Handle(RegisterCustomer request, CancellationToken cancellationToken)
        => Task.FromResult(Guid.NewGuid());
}
```

```csharp
public sealed class CustomersEndpoint(ISender sender)
{
    public async Task<IReadOnlyList<string>> Register(string email, int age, CancellationToken cancellationToken)
    {
        try
        {
            await sender.Send(new RegisterCustomer(email, age), cancellationToken);
            return [];
        }
        catch (RequestValidationException exception)
        {
            // One ValidationResult per error, in property order.
            return exception.Errors.Select(error => $"{string.Join(", ", error.MemberNames)}: {error.ErrorMessage}").ToList();
        }
    }
}
```

If validation failures should be a return value instead of an exception, write your own behavior: the built-in ones are
ordinary `IPipelineBehavior` implementations.

## Start-up verification

`VerifyHecoleMediator` is optional and runs only when you call it, typically right after building the container. It
reads registrations only (no handler, behavior or publisher is created) and throws `MediatorConfigurationException`
listing every problem it finds; `Problems` holds them one per item.

```csharp
using var provider = services.BuildServiceProvider();
provider.VerifyHecoleMediator(typeof(CreateOrder).Assembly);
```

It checks that every registered request has exactly one handler in the container, that every concrete request type in
the assemblies given has exactly one handler and implements only one request interface, and that the notification
publisher and the notification handlers registered by the library are in the container. In an ASP.NET Core application,
call it on `app.Services`. `IRequestCatalog`, resolved from the container, lists the requests known at registration.

## Analyzers

The package includes a Roslyn analyzer. It adds no runtime dependency.

| Id | Detects | Default |
|---|---|---|
| HM0001 | Request type with no handler in the compilation | On (Info) |
| HM0002 | More than one handler for the same request in the compilation | On (Info) |
| HM1001 | Assembly scanning | Off |
| HM1002 | `Lifetime = ServiceLifetime.Singleton` | Off |
| HM1003 | Pipeline behavior registration | Off |
| HM1004 | `TaskWhenAllPublisher` | Off |
| HM1005 | Handler without a matching `AddRequestHandler` | Off |

The `HM1xxx` rules report legitimate features that a project may decide to forbid; turn them on in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.HM1001.severity = error
```

Details, heuristics and known false positives: [docs/analyzers.md](docs/analyzers.md).

## Behavior notes

| Topic | Behavior |
|---|---|
| Contracts | `IBaseRequest`, `IRequest`, `IRequest<TResponse>`, `Unit`, `INotification`, `IRequestHandler<TRequest, TResponse>`, `IRequestHandler<TRequest>`, `INotificationHandler<TNotification>`, `ISender`, `IPublisher`, `IMediator`, `IPipelineBehavior<TRequest, TResponse>`, `RequestHandlerDelegate<TResponse>`, `INotificationPublisher`, `NotificationHandlerExecutor`, `ForeachAwaitPublisher`, `TaskWhenAllPublisher` |
| Configuration | `AddHecoleMediator(cfg => ...)` with `HecoleMediatorConfiguration`: `RegisterServicesFromAssembly`, `RegisterServicesFromAssemblies`, `RegisterServicesFromAssemblyContaining`, `AddBehavior`, `AddOpenBehavior`, `AddOpenBehaviors`, `Lifetime`, `NotificationPublisher`, `NotificationPublisherType` |
| Two handlers for one request | Throws at registration, with both names |
| `AddHecoleMediator` called again with another `Lifetime`, `RequireScope` or publisher | Throws, with both values |
| Invalid behavior or publisher type | Throws in `AddHecoleMediator` |
| Pipeline | Behaviors come from the container in registration order; the handler is created only when the pipeline reaches it; `next()` keeps the caller's token |
| `TaskWhenAllPublisher`, handler that throws before returning its task | The exception goes into that handler's task; the other handlers still run |
| Notifications | The default publisher runs handlers one at a time; the same handler class runs once; dispatch by exact runtime type |
| Dispatch | A typed executor is created at registration; reflection runs once per request type for handlers registered only in the container |
| Optional extras | `AddRequestHandler`, `AddNotificationHandler`, `RequireScope`, `VerifyHecoleMediator`, `IRequestCatalog`, built-in behaviors, analyzer rules |

## Not supported yet

| Feature | Alternative today |
|---|---|
| Streaming requests (a request answered with `IAsyncEnumerable<T>`, with its own pipeline) | None |
| Pre-processors and post-processors | A pipeline behavior |
| Exception handlers and exception actions | A pipeline behavior with `try`/`catch` |
| A public mediator class, or replacing the mediator implementation by type | Resolve `IMediator`, `ISender` or `IPublisher` |
| Open generic request handlers | Closed handlers |
| Open behaviors whose type parameters are not `<TRequest, TResponse>`, in that order | Register them closed with `AddBehavior` |

These may be added later without breaking the existing API.

## Upgrading from 1.x

See [docs/migration-1.x-to-2.0.md](docs/migration-1.x-to-2.0.md).

## Requirements

| Item | Value |
|---|---|
| Target framework | `net10.0` (.NET 10 or later) |
| Dependencies | `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`, 10.0.0 or later |
| Container | Tested with `Microsoft.Extensions.DependencyInjection`. Other containers are not tested |

## More documentation

| Document | Content |
|---|---|
| [docs/analyzers.md](docs/analyzers.md) | Analyzer rules and configuration |
| [docs/benchmarks.md](docs/benchmarks.md) | Measured cost of `Send` and `Publish`, 1.3.0 against 2.0, and the limits of that measurement |
| [docs/migration-1.x-to-2.0.md](docs/migration-1.x-to-2.0.md) | Upgrading from 1.x |
| [CHANGELOG.md](CHANGELOG.md) | Changes per version (in Portuguese) |

## License

MIT. See [LICENSE.txt](LICENSE.txt).
