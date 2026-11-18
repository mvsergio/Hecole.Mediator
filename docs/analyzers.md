# Hecole.Mediator analyzers

The `Hecole.Mediator` package ships a Roslyn analyzer (`analyzers/dotnet/cs/Hecole.Mediator.Analyzers.dll`). It runs in
every C# project that references the package, adds no runtime dependency and does nothing in a compilation that does not
reference `Hecole.Mediator`.

There are two kinds of rules:

- **General rules (`HM0xxx`)** are on by default. They report mistakes that fail at run time, with heuristics chosen so
  that legitimate code is not reported.
- **Restrictive rules (`HM1xxx`)** are **off by default**. Each one reports a legitimate feature of the library that a
  project may decide to forbid (assembly scanning, a Singleton mediator, behaviors registered outside the composition
  project, ...). A project turns on the ones it wants in `.editorconfig`. They report references to the symbols they
  guard (a type, a method, a property), wherever those references appear, not the shape of a particular call.

The analyzer is an early warning in the IDE and the build, not a security boundary: see
[What the analyzer cannot see](#what-the-analyzer-cannot-see).

## Rules

| Id | Detects | Default severity | On by default | Category |
|---|---|---|---|---|
| [HM0001](#hm0001) | Request type with no `IRequestHandler` in the compilation | Info | Yes | `Hecole.Mediator.Usage` |
| [HM0002](#hm0002) | More than one handler for the same request in the compilation | Info | Yes | `Hecole.Mediator.Usage` |
| [HM1001](#hm1001) | Assembly scanning (`RegisterServicesFromAssembly`, `RegisterServicesFromAssemblies`, `RegisterServicesFromAssemblyContaining`) | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1002](#hm1002) | `HecoleMediatorConfiguration.Lifetime` set to anything but a constant `Transient` or `Scoped` | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1003](#hm1003) | Pipeline behaviors outside the composition project (`AddBehavior`, `AddOpenBehavior`, `AddOpenBehaviors`, behavior declarations, any `typeof` or type argument naming a behavior) | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1004](#hm1004) | Any reference to `TaskWhenAllPublisher`, including through a `using` alias | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1005](#hm1005) | Request handler without a matching `AddRequestHandler` call in the compilation | Warning when on | No | `Hecole.Mediator.Restriction` |
| [HM1006](#hm1006) | `IRequestHandler<,>` / `IRequestHandler<>` used outside a handler declaration | Warning when on | No | `Hecole.Mediator.Restriction` |

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

# No assembly scanning; every handler registered with AddRequestHandler in its own project, and called only through
# the mediator.
dotnet_diagnostic.HM1001.severity = error
dotnet_diagnostic.HM1005.severity = error
dotnet_diagnostic.HM1006.severity = error

# No Singleton mediator; no TaskWhenAllPublisher.
dotnet_diagnostic.HM1002.severity = error
dotnet_diagnostic.HM1004.severity = error

# No pipeline behavior outside the composition project (turned off again there, below).
dotnet_diagnostic.HM1003.severity = error
```

```ini
# src/MyApp.Host/.editorconfig: the composition project is the one place allowed to declare and register behaviors.
[*.cs]
dotnet_diagnostic.HM1003.severity = none
```

A project that only declares behaviors for the host to register (a shared `Behaviors` library) turns HM1003 off too.

## General rules

### HM0001

**Request has no handler in this compilation.** Default: Info (shown in the IDE, does not fail the build).

Reported on a concrete request type (class, record or struct implementing `IRequest` or `IRequest<TResponse>`) declared
in the compilation when no concrete, closed class of the same compilation implements `IRequestHandler<TRequest>` /
`IRequestHandler<TRequest, TResponse>` for it and the compilation has no `AddRequestHandler` call for it. The mediator
resolves the handler by the exact request type, so sending the request throws.

Heuristic, to stay quiet on legitimate code:

| Situation | Reported? | Why |
|---|---|---|
| The compilation declares no request handler at all | No | Contracts assembly: requests here, handlers in another assembly |
| The only handler is an open generic class (`Handler<TRequest> : IRequestHandler<TRequest, X>`) | Yes | Scanning skips it, the container cannot close a class of another arity for `IRequestHandler<,>`, and `VerifyHecoleMediator` reports open generic handlers as unsupported |
| An `AddRequestHandler` call for the request in the compilation (for example `AddRequestHandler<Query, X, Handler<Query>>()`, or through a generic helper as described in [HM1005](#hm1005)) | No | The registration names the handler, and a closed construction of a generic class works |
| Abstract request, generic request definition, interface | No | Never sent as such |
| Request implementing both `IRequest` and `IRequest<T>` | No | The library rejects it at registration |
| Handler through a base class (`QueryHandler : HandlerBase<Query>`) | No | The concrete class is the handler |
| The only handler is abstract (`abstract class QueryHandlerBase : IRequestHandler<Query, int>`) | Yes | An abstract class cannot be registered; it still marks the compilation as one that declares handlers |
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
| Decorator (a handler with a public constructor that takes the same handler interface, as with a container decoration extension or manual wrapping) | No | Wraps the real handler |
| Handler whose only constructor taking the same handler interface is not public | Yes | The container builds the class with a public constructor, so it is a plain handler |
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

Generated code is analyzed and reported on like any other code: a generated-code header (`// <auto-generated/>`) or a
`.g.cs`/`.designer.cs` file name does not silence a restrictive rule that is on. (The general rules HM0001 and HM0002 keep
skipping generated code, as described above.)

### HM1001

**Assembly scanning is not allowed.** Reports every call to, or method group of, `RegisterServicesFromAssembly`,
`RegisterServicesFromAssemblies` and `RegisterServicesFromAssemblyContaining` (both overloads) of
`HecoleMediatorConfiguration`, and the configuration converted to `dynamic` (see below). For projects that want every
handler registered explicitly with `AddRequestHandler` and `AddNotificationHandler`.

**Configuration through `dynamic` (HM1001, HM1002, HM1003).** Converting an expression of type
`HecoleMediatorConfiguration` to `dynamic` (`dynamic late = cfg;`, `(dynamic)cfg`, passing `cfg` to a `dynamic`
parameter) binds every later member access at run time, out of reach of the analyzer. The conversion itself is reported
under each of HM1001, HM1002 and HM1003 that is on, with a message saying the configuration is used through `dynamic`.

```ini
dotnet_diagnostic.HM1001.severity = error
```

### HM1002

**Singleton mediator lifetime is not allowed.** Reports every assignment to `HecoleMediatorConfiguration.Lifetime` unless
the assigned value is a compile-time constant equal to `ServiceLifetime.Transient` or `ServiceLifetime.Scoped`: simple
assignment (in a lambda, a statement or an object initializer), deconstruction target (`(cfg.Lifetime, _) = (...)`),
compound assignment and `++`/`--`. A constant `Singleton` is reported as such; any other value (a local variable, a value
read from settings, a computed value) is reported because it cannot be proven not to be `Singleton`. The configuration
converted to `dynamic` is reported too (see [HM1001](#hm1001)). A Singleton mediator resolves handlers and behaviors from
the root provider, so their scoped dependencies live as long as the application.

Not reported: a constant `Transient` or `Scoped` (also as the matching element of a tuple literal in a deconstruction),
the `serviceLifetime` argument of `AddBehavior`/`AddOpenBehavior`, and a `Lifetime` property of another type. A project
that must pick the lifetime from settings keeps the rule off.

```ini
dotnet_diagnostic.HM1002.severity = error
```

### HM1003

**Pipeline behavior registration is not allowed in this project.** A behavior is a reference to `IPipelineBehavior<,>` (open
or closed), to a type that implements it, or to a constructed type that contains one of those
(`IEnumerable<IPipelineBehavior<A, B>>`, `List<(int, MyBehavior)>`). The rule reports references, wherever they appear:

- `cfg.AddBehavior` (all overloads), `cfg.AddOpenBehavior`, `cfg.AddOpenBehaviors`, as calls or method groups (reported
  once, on the call; the types inside it are not reported again);
- a type declaration that implements `IPipelineBehavior<,>`, directly or through a base class or interface (reported on
  its name; references inside its own body are not reported again);
- every `typeof(X)` whose type is a behavior: as a registration argument, in a variable later passed to
  `new ServiceDescriptor(service, implementation, lifetime)` or `services.Insert`, in `typeof(IPipelineBehavior<,>).MakeGenericType(...)`;
- every generic type argument, at any depth, whose type is a behavior: in any call (`services.AddScoped<IPipelineBehavior<A, B>, X>()`,
  a non-extension helper `Helper.Put<IPipelineBehavior<A, B>, X>(services)`, `services.AddSingleton<IEnumerable<IPipelineBehavior<A, B>>>(...)`,
  `provider.GetServices<IPipelineBehavior<A, B>>()`), object creation, method group or declaration. The outermost type
  argument is the reported site.

`Remove`/`RemoveAll` are reported too: removing a behavior from a module changes the pipeline as much as adding one.
Not reported: XML documentation and `nameof`, and types that are not behaviors.

Turn it on for the whole solution and turn it off (`none`) in the project that composes the application (and in a
project that only declares behaviors for it), so the pipeline is declared in one place.

```ini
dotnet_diagnostic.HM1003.severity = error
```

### HM1004

**TaskWhenAllPublisher is not allowed.** Reports every reference to
`Hecole.Mediator.NotificationPublishers.TaskWhenAllPublisher`, resolved semantically: `new`, `typeof` (for
`NotificationPublisherType`), generic type argument, base type, the `using` alias declaration and every use of the alias
(`using` or `global using`, declared in any file of the compilation). `nameof` and XML documentation are not reported,
nor is another type with the same name or an alias of another type. For projects whose
notification handlers share state that is not thread-safe (a `DbContext`, a unit of work) and must run one at a time
with the default `ForeachAwaitPublisher`.

```ini
dotnet_diagnostic.HM1004.severity = error
```

### HM1005

**Request handler is not registered with AddRequestHandler.** Reports every concrete, non-generic request handler class
of the compilation that has no `AddRequestHandler<TRequest, TResponse, THandler>()` or `AddRequestHandler<TRequest, THandler>()`
call for the same handler and request in the same compilation. A class that handles two requests needs a call for each.
Decorators (a public constructor takes the same handler interface; a non-public one does not count), abstract and open
generic handlers are not reported; calls in generated code count.

A registration through a generic helper declared in the compilation counts: when a method's body passes its own type
parameters to `AddRequestHandler` (`static IServiceCollection Feature<TRequest, TResponse, THandler>(this IServiceCollection s) => s.AddRequestHandler<TRequest, TResponse, THandler>()`),
each call `services.Feature<Query, X, QueryHandler>()` registers its type arguments. One level of forwarding is followed:
a helper that calls another helper is not, nor is a helper declared in a referenced assembly.

The rule does not see reachability: an `AddRequestHandler` call in a method that is never called counts. A handler
registered directly in the container (`services.AddTransient<IRequestHandler<A, B>, Handler>()`) while an
`AddRequestHandler` call for it sits in dead code is reported by [HM1006](#hm1006), which is the rule to turn on with
HM1005.

For projects that forbid scanning (usually together with HM1001) and register handlers in the project that declares
them, so a forgotten registration fails the build instead of the first `Send`. Not suited to projects whose handlers are
registered by another project.

```ini
dotnet_diagnostic.HM1005.severity = error
```

### HM1006

**Request handler interface is used outside a handler declaration.** Reports every reference to `IRequestHandler<,>` or
`IRequestHandler<>` (open or closed, written by name or through a `using` alias) that is not part of declaring a handler:
parameter, field, property, local and return types, casts, `typeof`, and generic type arguments at any depth. A class
that takes `IRequestHandler<A, B>` in its constructor calls the handler without the pipeline; `services.AddTransient<IRequestHandler<A, B>, Handler>()`
registers a handler without `AddRequestHandler`; `provider.GetService<IRequestHandler<A, B>>()` resolves one directly.

| Situation | Reported? | Why |
|---|---|---|
| Base list of a class, struct, record or interface (`class QueryHandler : IRequestHandler<Query, int>`) | No | Declares a handler |
| Explicit interface implementation (`Task<int> IRequestHandler<Query, int>.Handle(...)`) | No | Part of the handler declaration |
| Generic constraint (`where THandler : IRequestHandler<TRequest, TResponse>`, as in a registration helper) | No | Constrains a type argument; calls and registers nothing |
| Decorator: a public constructor parameter of the handler interface the class implements, and a field or property of that interface in that class | No | Wraps the real handler, as a container decoration does |
| Constructor parameter that is not public, or of another handler interface than the one the class implements | Yes | Not a decorator |
| XML documentation | No | Not code |

For projects where only the mediator calls handlers and every handler is registered with `AddRequestHandler`; usually on
together with HM1005.

```ini
dotnet_diagnostic.HM1006.severity = error
```

## What the analyzer cannot see

The analyzer is an early warning, not an enforcement mechanism. It does not see:

- **Reflection by string**: ``Type.GetType("Hecole.Mediator.IPipelineBehavior`2")``, `Activator.CreateInstance`, members
  invoked by name. Only references to symbols in the code are reported.
- **The consuming project's own policy**: a nested `.editorconfig` that sets a rule to `none` for a folder,
  `#pragma warning disable`, `[SuppressMessage]`, or a `<NoWarn>` in a project file turns a rule off where it applies.
  Whether those are allowed is the solution's policy (a review rule or a build check that rejects them), not the
  analyzer's.
- **Reachability**: a call in a method that is never called counts like any other (see [HM1005](#hm1005)).
- **Other compilations**: each project is analyzed on its own; a registration or a handler in another project is not
  visible (see the false positives of HM0001 and HM0002).
- **Values known only at run time**: the rules see constants and symbols, not what a variable will hold (HM1002 reports
  such values instead of guessing), and not what code bound through `dynamic` does (the conversion is reported).
