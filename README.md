[🇧🇷 Português](./README.pt-BR.md) | 🇺🇸 English

# 🧩 Hecole.Mediator

[![NuGet](https://img.shields.io/nuget/v/Hecole.Mediator)](https://www.nuget.org/packages/Hecole.Mediator)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Hecole.Mediator)](https://www.nuget.org/packages/Hecole.Mediator)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)

A **lightweight** and **performant** implementation of the **Mediator pattern** for **.NET 8** and **.NET 10**, inspired by **MediatR**.

Designed for **modular** projects based on **Clean Architecture**, with support for **CQRS** (Commands, Queries, Requests, and Notifications), **async behavior pipelines** (validation, logging, performance monitoring, and exception handling), and **native integration** with `Microsoft.Extensions.DependencyInjection`.

Ideal for systems with independent modules that need **use case orchestration with low coupling**.

---

## 🚀 Key Features

- ✅ **CQRS Support** — `IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`, `INotification`, and `INotificationHandler<TNotification>`.

- 🧠 **Async Pipeline Behaviors** — Middleware chain for cross-cutting concerns: **async validation** with FluentValidation (`ValidateAsync` + `MustAsync` — fixed in v1.2.0), **structured logging**, **performance monitoring**, and **unhandled exception capture**.

- ⚙️ **Auto-Registration via DI** — `AddHecoleMediator` extension for automatic assembly scanning and handler/behavior registration.

- ⚡ **Optimized Performance** — Reflection invoker caching with `ConcurrentDictionary` to avoid reflection on the hot path; **parallel notification dispatch** via `Task.WhenAll`.

- 🧱 **Robustness** — Isolated exception handling per notification handler, multiple validator support, and fire-and-forget notification semantics.

- 🎯 **Multi-Targeting** — Supports `net8.0` and `net10.0` in the same NuGet package. The runtime resolves the correct target automatically.

- 🧩 **Optional Dependencies** — Core depends only on `Microsoft.Extensions.DependencyInjection`. FluentValidation and Logging are optional.

- 🧭 **Clean Architecture Aligned** — Pure interfaces for Domain/SharedKernel and pluggable implementations in the Infrastructure layer.

---

## 🧰 Requirements

- **.NET 8.0** or **.NET 10.0** or later.
- Optional packages:
  - `FluentValidation` (for `ValidationBehavior`)
  - `Microsoft.Extensions.Logging` (for structured logging)

---

## 💾 Installation

Via NuGet:

```bash
dotnet add package Hecole.Mediator
```

Or as a project reference:

```bash
git clone https://github.com/mvsergio/Hecole.Mediator.git
```

```xml
<ProjectReference Include="..\Hecole.Mediator\Hecole.Mediator.csproj" />
```

---

## 🧠 Usage

### DI Registration (`Program.cs`)

```csharp
using Hecole.Mediator.Implementation.Extensions;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Register Mediator and scan assemblies (e.g., Application layer)
builder.Services.AddHecoleMediator(
    Assembly.GetAssembly(typeof(CreateInstitutionCommandHandler))
);

// Global behaviors (registration order matters: first registered = outermost)
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Register validators
builder.Services.AddTransient<IValidator<CreateInstitutionCommand>, CreateInstitutionCommandValidator>();
```

> **`AddHecoleMediator`** registers `ICoreMediator` as **Scoped** (recommended — works with scoped `DbContext`).
>
> **`AddCoreMediator`** registers `ICoreMediator` as **Singleton** (use when all dependencies are also singletons).

---

### Command / Query Handler

```csharp
using Hecole.Mediator.Interfaces;

public record CreateInstitutionCommand(string Name) : IRequest<CreateInstitutionResult>;
public record CreateInstitutionResult(Guid Id);

public class CreateInstitutionCommandHandler
    : IRequestHandler<CreateInstitutionCommand, CreateInstitutionResult>
{
    private readonly IRepository _repository;

    public CreateInstitutionCommandHandler(IRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateInstitutionResult> Handle(
        CreateInstitutionCommand request,
        CancellationToken cancellationToken)
    {
        var id = await _repository.SaveAsync(request, cancellationToken);
        return new CreateInstitutionResult(id);
    }
}
```

---

### Notification Handler

```csharp
using Hecole.Mediator.Interfaces;

public record InstitutionCreatedEvent(Guid Id, string Name) : INotification;

public class SendWelcomeEmailHandler : INotificationHandler<InstitutionCreatedEvent>
{
    public async Task Handle(InstitutionCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Send welcome email
        await Task.CompletedTask;
    }
}
```

Notifications are dispatched to **all registered handlers in parallel** via `Task.WhenAll`. If one handler throws, the others still execute — the exception propagates to the caller after all handlers complete.

---

### Controller

```csharp
using Hecole.Mediator.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/institutions")]
public class InstitutionController : ControllerBase
{
    private readonly ICoreMediator _mediator;

    public InstitutionController(ICoreMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInstitutionCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}
```

---

### Validation with `MustAsync()` (fixed in v1.2.0)

```csharp
using FluentValidation;

public class CreateInstitutionCommandValidator : AbstractValidator<CreateInstitutionCommand>
{
    public CreateInstitutionCommandValidator(IRepository repository)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name too long");

        RuleFor(x => x.Name)
            .MustAsync(async (name, ct) =>
            {
                return !await repository.ExistsAsync(name, ct);
            })
            .WithMessage("An institution with this name already exists");
    }
}
```

> In v1.1.0 and earlier, `MustAsync()` rules were **silently ignored** because `ValidationBehavior` called the synchronous `Validate()`. Since v1.2.0, it correctly uses `ValidateAsync()` with `Task.WhenAll`, and the `CancellationToken` is propagated.

---

### Custom Behavior

```csharp
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;

public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Before handler
        Console.WriteLine($"Processing {typeof(TRequest).Name}");

        var response = await next();

        // After handler
        Console.WriteLine($"Completed {typeof(TRequest).Name}");

        return response;
    }
}
```

Register it in the DI container:

```csharp
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
```

---

## 🔧 Built-in Behaviors

| Behavior | Description |
|---|---|
| `ValidationBehavior<,>` | Runs all `IValidator<TRequest>` via `ValidateAsync`. Throws `ValidationException` on failure. |
| `LoggingBehavior<,>` | Logs request start/end with elapsed time. |
| `PerformanceBehavior<,>` | Logs a warning when a request takes longer than 500ms. |
| `UnhandledExceptionBehavior<,>` | Catches and logs unhandled exceptions, then re-throws. |

---

## 🤝 Contributing

Contributions are **welcome**!

```bash
git clone https://github.com/mvsergio/Hecole.Mediator.git
git checkout -b feature/my-feature
git commit -m "Add my feature"
git push origin feature/my-feature
```

Open a **Pull Request** with a description of your changes.
Please add **unit tests** and follow the existing code style (`#nullable enable`, `async/await`).

---

## ⚖️ License

Distributed under the **MIT License**. See [LICENSE](./LICENSE) for details.

---

## 📋 Changelog

See [CHANGELOG.md](./CHANGELOG.md) for a full list of changes.

---
---

## 🇧🇷 Português

[🇧🇷 Versão completa em português](./README.pt-BR.md)

[![NuGet](https://img.shields.io/nuget/v/Hecole.Mediator)](https://www.nuget.org/packages/Hecole.Mediator)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Hecole.Mediator)](https://www.nuget.org/packages/Hecole.Mediator)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)

Uma implementação **leve** e **performática** do padrão **Mediator** para **.NET 8** e **.NET 10**, inspirada no **MediatR**.

Projetada para projetos **modulares** baseados em **Clean Architecture**, com suporte a **CQRS** (Commands, Queries, Requests e Notifications), **pipelines de behaviors assíncronos** (validação, logging, monitoramento de performance e tratamento de exceções) e **integração nativa** com `Microsoft.Extensions.DependencyInjection`.

Ideal para sistemas com módulos independentes que precisam de **orquestração de use cases com baixo acoplamento**.

---

### 🚀 Features Principais

- ✅ **Suporte a CQRS** — `IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`, `INotification` e `INotificationHandler<TNotification>`.

- 🧠 **Pipeline de Behaviors Assíncronos** — Cadeia de middlewares para cross-cutting concerns: **validação assíncrona** com FluentValidation (`ValidateAsync` + `MustAsync` — corrigido na v1.2.0), **logging estruturado**, **monitoramento de performance** e **captura de exceções**.

- ⚙️ **Registro Automático via DI** — Extensão `AddHecoleMediator` para scan automático de assemblies e registro de handlers/behaviors.

- ⚡ **Performance Otimizada** — Caching de invokers com `ConcurrentDictionary` para evitar reflection no hot path; **execução paralela de notifications** via `Task.WhenAll`.

- 🧱 **Robustez** — Tratamento isolado de exceções por notification handler, suporte a múltiplos validators e semântica fire-and-forget para notifications.

- 🎯 **Multi-Targeting** — Suporte a `net8.0` e `net10.0` no mesmo pacote NuGet. O runtime resolve o target correto automaticamente.

- 🧩 **Dependências Opcionais** — Core depende apenas de `Microsoft.Extensions.DependencyInjection`. FluentValidation e Logging são opcionais.

- 🧭 **Alinhado com Clean Architecture** — Interfaces puras para Domain/SharedKernel e implementações plugáveis na camada Infrastructure.

---

### 🧰 Requisitos

- **.NET 8.0** ou **.NET 10.0** ou superior.
- Pacotes opcionais:
  - `FluentValidation` (para `ValidationBehavior`)
  - `Microsoft.Extensions.Logging` (para logging estruturado)

---

### 💾 Instalação

Via NuGet:

```bash
dotnet add package Hecole.Mediator
```

Ou como referência de projeto:

```bash
git clone https://github.com/mvsergio/Hecole.Mediator.git
```

```xml
<ProjectReference Include="..\Hecole.Mediator\Hecole.Mediator.csproj" />
```

---

### 🧠 Uso

#### Registro no DI (`Program.cs`)

```csharp
using Hecole.Mediator.Implementation.Extensions;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Registra o Mediator e escaneia assemblies (ex.: camada Application)
builder.Services.AddHecoleMediator(
    Assembly.GetAssembly(typeof(CreateInstitutionCommandHandler))
);

// Behaviors globais (ordem de registro importa: primeiro registrado = mais externo)
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Registrar validators
builder.Services.AddTransient<IValidator<CreateInstitutionCommand>, CreateInstitutionCommandValidator>();
```

> **`AddHecoleMediator`** registra `ICoreMediator` como **Scoped** (recomendado — funciona com `DbContext` scoped).
>
> **`AddCoreMediator`** registra `ICoreMediator` como **Singleton** (use quando todas as dependências também são singletons).

---

#### Handler de Command / Query

```csharp
using Hecole.Mediator.Interfaces;

public record CreateInstitutionCommand(string Name) : IRequest<CreateInstitutionResult>;
public record CreateInstitutionResult(Guid Id);

public class CreateInstitutionCommandHandler
    : IRequestHandler<CreateInstitutionCommand, CreateInstitutionResult>
{
    private readonly IRepository _repository;

    public CreateInstitutionCommandHandler(IRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateInstitutionResult> Handle(
        CreateInstitutionCommand request,
        CancellationToken cancellationToken)
    {
        var id = await _repository.SaveAsync(request, cancellationToken);
        return new CreateInstitutionResult(id);
    }
}
```

---

#### Handler de Notification

```csharp
using Hecole.Mediator.Interfaces;

public record InstitutionCreatedEvent(Guid Id, string Name) : INotification;

public class SendWelcomeEmailHandler : INotificationHandler<InstitutionCreatedEvent>
{
    public async Task Handle(InstitutionCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Enviar e-mail de boas-vindas
        await Task.CompletedTask;
    }
}
```

Notifications são despachadas para **todos os handlers registrados em paralelo** via `Task.WhenAll`. Se um handler lançar exceção, os demais ainda executam — a exceção é propagada ao caller após todos os handlers completarem.

---

#### Controller

```csharp
using Hecole.Mediator.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/institutions")]
public class InstitutionController : ControllerBase
{
    private readonly ICoreMediator _mediator;

    public InstitutionController(ICoreMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateInstitutionCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);
        return Ok(result);
    }
}
```

---

#### Validação com `MustAsync()` (corrigido na v1.2.0)

```csharp
using FluentValidation;

public class CreateInstitutionCommandValidator : AbstractValidator<CreateInstitutionCommand>
{
    public CreateInstitutionCommandValidator(IRepository repository)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(200).WithMessage("Name too long");

        RuleFor(x => x.Name)
            .MustAsync(async (name, ct) =>
            {
                return !await repository.ExistsAsync(name, ct);
            })
            .WithMessage("An institution with this name already exists");
    }
}
```

> Na v1.1.0 e anteriores, regras `MustAsync()` eram **ignoradas silenciosamente** porque o `ValidationBehavior` chamava `Validate()` (síncrono). Desde a v1.2.0, ele usa corretamente `ValidateAsync()` com `Task.WhenAll`, e o `CancellationToken` é propagado.

---

#### Custom Behavior

```csharp
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;

public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Antes do handler
        Console.WriteLine($"Processando {typeof(TRequest).Name}");

        var response = await next();

        // Depois do handler
        Console.WriteLine($"Concluído {typeof(TRequest).Name}");

        return response;
    }
}
```

Registre no container DI:

```csharp
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
```

---

### 🔧 Behaviors Incluídos

| Behavior | Descrição |
|---|---|
| `ValidationBehavior<,>` | Executa todos os `IValidator<TRequest>` via `ValidateAsync`. Lança `ValidationException` em caso de falha. |
| `LoggingBehavior<,>` | Loga início/fim do request com tempo decorrido. |
| `PerformanceBehavior<,>` | Loga warning quando um request leva mais de 500ms. |
| `UnhandledExceptionBehavior<,>` | Captura e loga exceções não tratadas, depois re-lança. |

---

### 🤝 Contribuições

Contribuições são **bem-vindas**!

```bash
git clone https://github.com/mvsergio/Hecole.Mediator.git
git checkout -b feature/minha-feature
git commit -m "Adiciona minha feature"
git push origin feature/minha-feature
```

Abra um **Pull Request** com uma descrição das alterações.
Adicione **testes unitários** e siga o estilo de código existente (`#nullable enable`, `async/await`).

---

### ⚖️ Licença

Distribuído sob a **MIT License**. Veja [LICENSE](./LICENSE) para detalhes.

---

### 📋 Changelog

Veja [CHANGELOG.md](./CHANGELOG.md) para a lista completa de mudanças.
