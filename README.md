# 🧩 Hecole.Mediator

Uma implementação **leve**, **performática** e **sem dependências externas** do padrão **Mediator** para **.NET 8**, inspirada no **MediatR**.  
Projetada para projetos **modulares** baseados em **Clean Architecture**, com suporte a **CQRS** (Commands, Queries, Requests e Notifications), **pipelines de behaviors** (ex.: validação, logging, performance monitoring e tratamento de exceções) e **integração nativa** com o **Dependency Injection (DI)** de `Microsoft.Extensions`.

Ideal para sistemas com módulos independentes, que precisam de **orquestração de use cases com baixa acoplagem**.

---

## 🚀 Features Principais

- ✅ **Suporte a CQRS**  
  Interfaces para `IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`, `INotification` e `INotificationHandler<TNotification>`.

- 🧠 **Pipelines e Behaviors**  
  Cadeia de middlewares para cross-cutting concerns, como **validação assíncrona** (com FluentValidation), **logging estruturado**, **monitoramento de performance** e **captura de exceções**.

- ⚙️ **Registro Automático via DI**  
  Extensão `AddHecoleMediator` para scan automático de assemblies e registro de handlers/behaviors.

- ⚡ **Performance Otimizada**  
  Caching de invokers com `ConcurrentDictionary` para evitar reflection no hot path; execução paralela de notifications.

- 🧱 **Robustez**  
  Tratamento isolado de exceções em handlers, suporte a múltiplos validators e _fire-and-forget_ para events.

- 🧩 **Zero Dependências Externas**  
  Apenas `Microsoft.Extensions.DependencyInjection` (e opcionais como FluentValidation).

- 🧭 **Alinhado com Clean Architecture**  
  Interfaces puras para Domain/SharedKernel e implementações plugáveis na camada Infrastructure.

---

## 🧰 Requisitos

- .NET **8.0** ou superior.  
- Pacotes opcionais:
  - `FluentValidation` (para `ValidationBehavior`)
  - `Microsoft.Extensions.Logging` (para logging estruturado)

---

## 💾 Instalação

Clone o repositório:

```bash
git clone https://github.com/mvsergio/hecole-mediator.git
```

Adicione como referência ao seu projeto (`.csproj`):

```xml
<ItemGroup>
  <ProjectReference Include="..\Hecole.Mediator\Hecole.Mediator.csproj" />
</ItemGroup>
```

Ou via NuGet (https://www.nuget.org/packages/Hecole.Mediator):

```bash
dotnet add package Hecole.Mediator
```

---

## 🧠 Uso

### Registro no DI (`Program.cs` ou `Bootstrap.cs`)

```csharp
using Hecole.Mediator.Implementation.Extensions;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Registra o Mediator e scannea assemblies (ex.: camada Application)
builder.Services.AddHecoleMediator(
    Assembly.GetAssembly(typeof(CadastrarInstituicaoCommandHandler)) // Seu handler
);

// Behaviors globais (ordem importa: o último adicionado é o mais externo)
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Validator específico
builder.Services.AddTransient<IValidator<CadastrarInstituicaoCommand>, CadastrarInstituicaoCommandValidator>();
```

---

### Exemplo de Handler (em `Application.UseCases`)

```csharp
using Hecole.Mediator.Interfaces;

public class CadastrarInstituicaoCommandHandler 
    : IRequestHandler<CadastrarInstituicaoCommand, CadastrarInstituicaoCommandResponse>
{
    private readonly IRepository _repository;

    public CadastrarInstituicaoCommandHandler(IRepository repository)
    {
        _repository = repository;
    }

    public async Task<CadastrarInstituicaoCommandResponse> Handle(
        CadastrarInstituicaoCommand request, 
        CancellationToken ct)
    {
        // Lógica do use case
        await _repository.SaveAsync(request);
        return new CadastrarInstituicaoCommandResponse(/* resultado */);
    }
}
```

---

### Exemplo em Controller (`WebApi`)

```csharp
using Hecole.Mediator.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/instituicoes")]
public class InstituicaoController : ControllerBase
{
    private readonly ICoreMediator _mediator;

    public InstituicaoController(ICoreMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarInstituicaoCommand command)
    {
        var response = await _mediator.Send(command);
        return Ok(response);
    }
}
```

---

### Exemplo de Behavior Custom

Crie e registre behaviors para estender o pipeline, como validações assíncronas, auditoria, métricas, etc.

---

## 🤝 Contribuições

Contribuições são **bem-vindas**!  

Siga estes passos:

```bash
# Fork o repositório
git clone https://github.com/mvsergio/hecole-mediator.git

# Crie uma branch
git checkout -b feature/nova-feature

# Commit suas mudanças
git commit -m "Adiciona nova feature"

# Push para a branch
git push origin feature/nova-feature
```

Abra um **Pull Request** e descreva suas alterações.  
Lembre-se de adicionar **testes unitários** e seguir o estilo de código existente (`#nullable enable`, `async/await`, etc).

---

## ⚖️ Licença

Distribuído sob a **MIT License**.  
Veja o arquivo [LICENSE](./LICENSE) para mais detalhes.

---
