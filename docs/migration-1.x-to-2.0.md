# Migrating from Hecole.Mediator 1.x to 2.0

2.0 is a rewrite with new contracts in the `Hecole.Mediator` namespace. It is not source compatible with 1.x: every project that uses the
library needs the changes below. Behavior also changes in a few places (notification order, registration errors); see
[Behavior changes](#behavior-changes).

## Summary

| Area | 1.x | 2.0 |
|---|---|---|
| Target framework | `net8.0` and `net10.0` | `net10.0` only |
| Dependencies | `FluentValidation`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Abstractions` | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions` |
| Namespaces | `Hecole.Mediator.Interfaces`, `Hecole.Mediator.Interfaces.Behaviors`, `Hecole.Mediator.Implementation`, `Hecole.Mediator.Implementation.Behaviors`, `Hecole.Mediator.Implementation.Extensions` | `Hecole.Mediator` (contracts), `Hecole.Mediator.Behaviors`, `Hecole.Mediator.NotificationPublishers`, `Microsoft.Extensions.DependencyInjection` (registration) |
| Mediator interface | `ICoreMediator` | `IMediator`, `ISender` (send only), `IPublisher` (publish only) |
| Registration | `AddHecoleMediator(params Assembly[])` (scoped; scans the whole `AppDomain` when no assembly is given), `AddCoreMediator(params Assembly[])` (singleton) | `AddHecoleMediator(cfg => ...)` (transient by default; scans only the assemblies given), plus optional `AddRequestHandler` / `AddNotificationHandler` |
| Behaviors | Found by the assembly scan | Registered explicitly: `cfg.AddOpenBehavior`, `cfg.AddBehavior`, or `services.AddTransient(typeof(IPipelineBehavior<,>), ...)` |
| `IPipelineBehavior` constraint | `where TRequest : IRequest<TResponse>` | `where TRequest : notnull` |
| `RequestHandlerDelegate<TResponse>` | `()` | `(CancellationToken t = default)`; `next()` still compiles |
| Requests without response | Not available | `IRequest`, `IRequestHandler<TRequest>`, `Send` returns `Task` |
| `Send(object)`, `Publish(object)` | Not available | Available |
| Validation | `ValidationBehavior` over FluentValidation | `ValidationBehavior` over `System.ComponentModel.DataAnnotations`, or your own behavior |
| Notifications | All handlers in parallel (`Task.WhenAll`) | One at a time, in registration order (`ForeachAwaitPublisher`); `TaskWhenAllPublisher` is opt-in |
| Built-in behaviors | Run only when the scan found them | Never registered unless added |

## Step by step

### 1. Target framework and package

Projects must target `net10.0` or later. Remove direct references to `FluentValidation` if they existed only for the
mediator's validation.

```xml
<TargetFramework>net10.0</TargetFramework>
```

### 2. Namespaces

| 1.x | 2.0 |
|---|---|
| `using Hecole.Mediator.Interfaces;` | `using Hecole.Mediator;` |
| `using Hecole.Mediator.Interfaces.Behaviors;` | `using Hecole.Mediator;` |
| `using Hecole.Mediator.Implementation;` | `using Microsoft.Extensions.DependencyInjection;` (registration) |
| `using Hecole.Mediator.Implementation.Extensions;` | `using Microsoft.Extensions.DependencyInjection;` (registration) |
| `using Hecole.Mediator.Implementation.Behaviors;` | `using Hecole.Mediator.Behaviors;` |

Requests, handlers and notifications keep their shape:

```csharp
// 1.x and 2.0: only the using changes.
public sealed record CreateOrder(string Customer) : IRequest<Guid>;

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, Guid>
{
    public Task<Guid> Handle(CreateOrder request, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
}
```

### 3. `ICoreMediator` replaced by `IMediator`, `ISender` and `IPublisher`

```csharp
// 1.x
public sealed class OrdersController(ICoreMediator mediator) { /* mediator.Send(...); mediator.Publish(...); */ }

// 2.0
public sealed class OrdersController(ISender sender, IPublisher publisher) { /* sender.Send(...); publisher.Publish(...); */ }
// or IMediator, which has both
```

`CoreMediator` (the public class of 1.x) has no public replacement: resolve the interfaces.

### 4. Registration

```csharp
// 1.x
services.AddHecoleMediator(typeof(CreateOrder).Assembly);
// or
services.AddCoreMediator(typeof(CreateOrder).Assembly);

// 2.0
services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateOrder).Assembly));
```

| 1.x behavior | 2.0 | What to do |
|---|---|---|
| `AddHecoleMediator()` with no assembly scanned every assembly loaded in the `AppDomain` | Only the assemblies given with `RegisterServicesFrom...` are scanned; none is required | Name the assemblies, or register handlers with `AddRequestHandler` / `AddNotificationHandler` |
| `AddHecoleMediator` registered `ICoreMediator` as scoped | `IMediator`, `ISender`, `IPublisher` are transient by default | Set `cfg.Lifetime = ServiceLifetime.Scoped` if you depended on one instance per scope |
| `AddCoreMediator` registered a singleton mediator, so handlers were resolved from the root provider | `cfg.Lifetime = ServiceLifetime.Singleton` is still possible | Prefer the default. `cfg.RequireScope = true` makes `Send` and `Publish` throw when the mediator comes from the root provider |
| The scan also registered every `IPipelineBehavior` it found | Behaviors are never scanned | Add each behavior, in the order you want (see step 5) |
| Two handlers for one request: the last registration won | Throws `InvalidOperationException` with both names | Keep one handler per request |

To check the configuration at start-up (optional):

```csharp
var provider = services.BuildServiceProvider();
provider.VerifyHecoleMediator(typeof(CreateOrder).Assembly);
```

### 5. Pipeline behaviors

The constraint changes from `IRequest<TResponse>` to `notnull`, so the same behavior also runs for requests without a
response (`TResponse` is `Unit`). Registration becomes explicit; the first registered is the outermost.

```csharp
// 1.x
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}
// registered by the assembly scan

// 2.0
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

services.AddHecoleMediator(cfg => cfg
    .RegisterServicesFromAssembly(typeof(CreateOrder).Assembly)
    .AddOpenBehavior(typeof(AuditBehavior<,>)));
```

Keeping `where TRequest : IRequest<TResponse>` still compiles; the behavior is then skipped for requests without a
response.

### 6. Built-in behaviors

The four built-in behaviors keep their names and move to `Hecole.Mediator.Behaviors`. None is registered by default:

```csharp
services.AddHecoleMediator(cfg => cfg
    .RegisterServicesFromAssembly(typeof(CreateOrder).Assembly)
    .AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>))
    .AddOpenBehavior(typeof(LoggingBehavior<,>))
    .AddOpenBehavior(typeof(PerformanceBehavior<,>))
    .AddOpenBehavior(typeof(ValidationBehavior<,>)));
```

| Behavior | 1.x | 2.0 |
|---|---|---|
| `ValidationBehavior` | FluentValidation `IValidator<TRequest>`; throws `FluentValidation.ValidationException` | DataAnnotations attributes and `IValidatableObject`; throws `RequestValidationException` (derives from `System.ComponentModel.DataAnnotations.ValidationException`) with every error in `Errors` |
| `LoggingBehavior` | Start and end at Information | Start, end, failure and cancellation, with stable event ids (1001–1004) |
| `PerformanceBehavior` | Warning above a fixed 500 ms | Warning above `PerformanceBehaviorOptions.Threshold` (500 ms by default), also when the request fails |
| `UnhandledExceptionBehavior` | Logged the whole request object (`{@Request}`) | Logs the request type name only |

### 7. FluentValidation

There is no FluentValidation integration in 2.0. Two options:

| Option | How |
|---|---|
| Move to DataAnnotations | Put attributes on the request properties (`[property: Required]` on a positional record) or implement `IValidatableObject`; add `ValidationBehavior<,>` |
| Keep FluentValidation | Reference `FluentValidation` in your project and write a behavior that runs your validators |

A behavior that keeps FluentValidation:

```csharp
using FluentValidation;
using Hecole.Mediator;

public sealed class FluentValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}

// services.AddValidatorsFromAssembly(...) (FluentValidation.DependencyInjectionExtensions)
// services.AddHecoleMediator(cfg => cfg.AddOpenBehavior(typeof(FluentValidationBehavior<,>)));
```

The validators run one after another, each with its own context (1.x ran them in parallel over a shared context).

### 8. Notifications

| 1.x | 2.0 |
|---|---|
| Handlers ran in parallel (`Task.WhenAll`) | Handlers run one at a time, in registration order; the first exception stops the rest |
| Two registrations of the same handler class ran twice | The same handler class runs once |

To keep parallel execution:

```csharp
services.AddHecoleMediator(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(CreateOrder).Assembly);
    cfg.NotificationPublisher = new TaskWhenAllPublisher(); // Hecole.Mediator.NotificationPublishers
});
```

## Behavior changes

Changes that compile without errors but behave differently:

| Change | Effect |
|---|---|
| Notifications sequential by default | Slower when handlers wait on I/O; a failing handler now stops the ones registered after it |
| Mediator transient by default (was scoped with `AddHecoleMediator`, singleton with `AddCoreMediator`) | One mediator instance per resolution; handlers still come from the scope it was resolved in |
| No scan of the whole `AppDomain` | Handlers in assemblies that are not named are not registered; `Send` throws "no handler" |
| Behaviors are not scanned | Behaviors that were picked up by the scan stop running until added |
| Duplicate handler | Two handlers for one request throw at registration instead of the last one winning |
| Synchronous exception in a handler | Leaves `Send` as thrown, not wrapped in `TargetInvocationException` |

## No replacement in 2.0

| 1.x | Note |
|---|---|
| `net8.0` target | Removed. Stay on 1.3.0 for .NET 8 |
| `AddCoreMediator` | Removed. Use `AddHecoleMediator(cfg => ...)` |
| `ICoreMediator`, public `CoreMediator` class | Removed. Use `IMediator`, `ISender`, `IPublisher` |
| FluentValidation in `ValidationBehavior` | Removed. DataAnnotations, or your own behavior (step 7) |
| Scanning every loaded assembly | Removed on purpose. Name the assemblies |
