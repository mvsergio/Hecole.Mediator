# Hecole.Mediator analyzers

The `Hecole.Mediator` package ships a Roslyn analyzer (`analyzers/dotnet/cs/Hecole.Mediator.Analyzers.dll`). It runs in
every C# project that references the package, adds no runtime dependency and does nothing in a compilation that does not
reference `Hecole.Mediator`.

There are two kinds of rules:

- **General rules (`HM0xxx`)** are on by default. They report mistakes that fail at run time, with heuristics chosen so
  that legitimate code is not reported.
- **Restrictive rules (`HM1xxx`)** are **off by default**. Each one reports a legitimate feature of the library that a
  project may decide to forbid (assembly scanning, a Singleton mediator, behaviors registered outside the composition
  project, ...). A project turns on the ones it wants in `.editorconfig`.

## Rules

| Id | Detects | Default severity | On by default | Category |
|---|---|---|---|---|
| [HM0001](#hm0001) | Request type with no `IRequestHandler` in the compilation | Info | Yes | `Hecole.Mediator.Usage` |
| [HM0002](#hm0002) | More than one handler for the same request in the compilation | Info | Yes | `Hecole.Mediator.Usage` |
| [HM1001](#hm1001) | Assembly scanning (`RegisterServicesFromAssembly`, `RegisterServicesFromAssemblies`, `RegisterServicesFromAssemblyContaining`) | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1002](#hm1002) | `HecoleMediatorConfiguration.Lifetime` set to `ServiceLifetime.Singleton` | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1003](#hm1003) | Pipeline behavior registration (`AddBehavior`, `AddOpenBehavior`, `AddOpenBehaviors`, `IPipelineBehavior<,>` added to an `IServiceCollection`) | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1004](#hm1004) | Any reference to `TaskWhenAllPublisher` | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1005](#hm1005) | Request handler without a matching `AddRequestHandler` call in the compilation | Warning when on | No | `Hecole.Mediator.Restriction` |

Ids are stable: a rule is never renumbered and an id is never reused.

## Turning rules on and off

Severity is set per rule in `.editorconfig` (or a `.globalconfig`), with the standard .NET key:

```ini
[*.cs]
# Turn a restrictive rule on, failing the build.
dotnet_diagnostic.HM1001.severity = error

# Make a general rule stricter, or turn it off.
dotnet_diagnostic.HM0001.severity = warning
dotnet_diagnostic.HM0002.severity = none
```

Accepted values: `error`, `warning`, `suggestion`, `silent`, `none`. A single occurrence can be suppressed with
`#pragma warning disable HM0002` or `[SuppressMessage("Hecole.Mediator.Usage", "HM0002", Justification = "...")]`.

### Example: a restrictive project

A solution that forbids scanning and a Singleton mediator, registers every handler explicitly in the project that
declares it, runs notification handlers one at a time and declares the pipeline only in the host project:

```ini
# .editorconfig at the solution root: applies to every project.
root = true

[*.cs]
# Request without handler; two handlers for one request.
dotnet_diagnostic.HM0001.severity = error
dotnet_diagnostic.HM0002.severity = error

# No assembly scanning; every handler registered with AddRequestHandler in its own project.
dotnet_diagnostic.HM1001.severity = error
dotnet_diagnostic.HM1005.severity = error

# No Singleton mediator; no TaskWhenAllPublisher.
dotnet_diagnostic.HM1002.severity = error
dotnet_diagnostic.HM1004.severity = error

# No behavior registration (turned off again in the composition project below).
dotnet_diagnostic.HM1003.severity = error
```

```ini
# src/MyApp.Host/.editorconfig: the composition project is the one place allowed to register behaviors.
[*.cs]
dotnet_diagnostic.HM1003.severity = none
```

## General rules

### HM0001

**Request has no handler in this compilation.** Default: Info (shown in the IDE, does not fail the build).

Reported on a concrete request type (class, record or struct implementing `IRequest` or `IRequest<TResponse>`) declared
in the compilation when no class of the same compilation implements `IRequestHandler<TRequest>` /
`IRequestHandler<TRequest, TResponse>` for it. The mediator resolves the handler by the exact request type, so sending the
request throws.

Heuristic, to stay quiet on legitimate code:

| Situation | Reported? | Why |
|---|---|---|
| The compilation declares no request handler at all | No | Contracts assembly: requests here, handlers in another assembly |
| An open generic handler (`Handler<TRequest> : IRequestHandler<TRequest, X>`) exists for the same shape (with or without response) | No | It can be registered as `IRequestHandler<,>` and handle the request |
| Abstract request, generic request definition, interface | No | Never sent as such |
| Request implementing both `IRequest` and `IRequest<T>` | No | The library rejects it at registration |
| Handler through a base class (`QueryHandler : HandlerBase<Query>`) or abstract closed handler | No | Counts as a handler |
| Handler or request in generated code | Counted, never reported | Source generators may produce handlers |
| Derived request (`Derived : Base`) with a handler only for `Base` | Yes | The mediator does not fall back to the base type |

Remaining false positives: a request handled by a handler in another assembly while this compilation also declares other
handlers (for example requests in `Application`, some handlers in `Infrastructure`), a request sent to another process,
or a request used only with a test double. That is why the default is Info; projects without those patterns can raise it
to `warning` or `error`.

### HM0002

**Request has more than one handler.** Default: Info (shown in the IDE, does not fail the build).

Reported on every concrete, non-generic handler class when two or more of them in the same compilation handle the same
request type and the compilation does not register that request with `AddRequestHandler`. Registering both (by assembly
scanning, or with `AddRequestHandler` for each) throws an `InvalidOperationException` at registration; with scanning this
rule shows the error before the application starts.

| Situation | Reported? | Why |
|---|---|---|
| Handlers for the same request in different assemblies | No | Usually one per host, or test doubles; registration decides |
| Decorator (a handler whose constructor takes the same handler interface, as with Scrutor `Decorate` or manual wrapping) | No | Wraps the real handler |
| The compilation calls `AddRequestHandler` for that request (one handler registered, alternatives chosen by configuration, test doubles registered per test) | No | The handler is chosen explicitly; registering two different ones still throws at run time |
| Abstract handler + concrete subclass | No (one handler) | Only the concrete class is registered |
| Open generic handler + closed handler | No | Different registrations |
| Concrete handler + concrete subclass of it, no `AddRequestHandler` for the request | Yes | Both are handlers; scanning registers both and throws |
| One duplicate in generated code | Reported on the user's handler only | Generated code is never reported on |

Remaining false positives: two handlers in one project that are registered explicitly by **another** project (the
composition project registers one per host or per environment), or test doubles registered through a helper in another
assembly. The analyzer cannot see that registration, which is why the default is Info. Projects that rely on scanning
can raise the rule to `warning` or `error`.

## Restrictive rules

All are off by default. Turn each one on with an explicit severity, usually `error` (`warning` also works).

### HM1001

**Assembly scanning is not allowed.** Reports every call to, or method group of, `RegisterServicesFromAssembly`,
`RegisterServicesFromAssemblies` and `RegisterServicesFromAssemblyContaining` (both overloads) of
`HecoleMediatorConfiguration`. For projects that want every handler registered explicitly with `AddRequestHandler` and
`AddNotificationHandler`.

```ini
dotnet_diagnostic.HM1001.severity = error
```

### HM1002

**Singleton mediator lifetime is not allowed.** Reports `HecoleMediatorConfiguration.Lifetime` assigned a constant equal
to `ServiceLifetime.Singleton` (in a lambda, a statement or an object initializer). A Singleton mediator resolves handlers
and behaviors from the root provider, so their scoped dependencies live as long as the application.

Not reported: other lifetimes, the `serviceLifetime` argument of `AddBehavior`/`AddOpenBehavior`, a `Lifetime` property of
another type, and a value that is not a compile-time constant (read from settings, for example), which no build-time rule
can know.

```ini
dotnet_diagnostic.HM1002.severity = error
```

### HM1003

**Pipeline behavior registration is not allowed in this project.** Reports:

- `cfg.AddBehavior` (all overloads), `cfg.AddOpenBehavior`, `cfg.AddOpenBehaviors`, as calls or method groups;
- `IPipelineBehavior<,>` (open or closed) registered directly in an `IServiceCollection`: as a generic type argument
  (`services.AddScoped<IPipelineBehavior<A, B>, X>()`) or as a `typeof` argument (`services.AddTransient(typeof(IPipelineBehavior<,>), typeof(X<,>))`,
  `ServiceDescriptor.Transient(...)`, `new ServiceDescriptor(...)`), including `TryAdd*`, `TryAddEnumerable` and `Replace`.

Not reported: `Remove`/`RemoveAll`, reading behaviors from an `IServiceProvider`, and a `Type` held in a variable.

Turn it on for the whole solution and turn it off (`none`) in the project that composes the application, so the pipeline
is declared in one place.

```ini
dotnet_diagnostic.HM1003.severity = error
```

### HM1004

**TaskWhenAllPublisher is not allowed.** Reports every reference to
`Hecole.Mediator.NotificationPublishers.TaskWhenAllPublisher`: `new`, `typeof` (for `NotificationPublisherType`), generic
type argument, base type and `using` alias. `nameof` and XML documentation are not reported. For projects whose
notification handlers share state that is not thread-safe (a `DbContext`, a unit of work) and must run one at a time
with the default `ForeachAwaitPublisher`.

```ini
dotnet_diagnostic.HM1004.severity = error
```

### HM1005

**Request handler is not registered with AddRequestHandler.** Reports every concrete, non-generic request handler class
of the compilation that has no `AddRequestHandler<TRequest, TResponse, THandler>()` or `AddRequestHandler<TRequest, THandler>()`
call for the same handler and request in the same compilation. A class that handles two requests needs a call for each.
Decorators, abstract and open generic handlers are not reported; calls in generated code count.

For projects that forbid scanning (usually together with HM1001) and register handlers in the project that declares
them, so a forgotten registration fails the build instead of the first `Send`. Not suited to projects whose handlers are
registered by another project.

```ini
dotnet_diagnostic.HM1005.severity = error
```
