# Hecole.Mediator

[English](README.md)

Mediator em processo para .NET 10 ou superior. Um pedido (request) vai para exatamente um handler, uma notification vai
para quantos handlers houver, e pipeline behaviors envolvem todo pedido. Os contratos públicos são `IRequest`,
`IRequestHandler`, `ISender`, `IPublisher`, `IMediator`, `IPipelineBehavior` e `INotificationPublisher`.

É para aplicações que usam `Microsoft.Extensions.DependencyInjection` e querem erros de configuração, como dois handlers
para um pedido, apontados no registro ou na partida. Licença MIT.

## Conteúdo

- [Instalação](#instalação)
- [Início rápido](#início-rápido)
- [Pedidos sem resposta](#pedidos-sem-resposta)
- [Notifications e publishers](#notifications-e-publishers)
- [Pipeline behaviors](#pipeline-behaviors)
- [Behaviors prontos (opcionais)](#behaviors-prontos-opcionais)
- [Verificação na partida](#verificação-na-partida)
- [Analyzers](#analyzers)
- [Notas de comportamento](#notas-de-comportamento)
- [Ainda não suportado](#ainda-não-suportado)
- [Atualizando da 1.x](#atualizando-da-1x)
- [Requisitos](#requisitos)
- [Mais documentação](#mais-documentação)
- [Licença](#licença)

## Instalação

```bash
dotnet add package Hecole.Mediator
```

O pacote contém a biblioteca e um analyzer Roslyn. Os contratos ficam no namespace `Hecole.Mediator`, os métodos de
registro em `Microsoft.Extensions.DependencyInjection`, os behaviors opcionais em `Hecole.Mediator.Behaviors` e os
publishers de notification em `Hecole.Mediator.NotificationPublishers`.

## Início rápido

Um pedido, sua resposta e seu handler:

```csharp
public sealed record CreateOrder(string Customer, decimal Total) : IRequest<OrderId>;

public sealed record OrderId(Guid Value);

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId>
{
    public Task<OrderId> Handle(CreateOrder request, CancellationToken cancellationToken)
        => Task.FromResult(new OrderId(Guid.NewGuid()));
}
```

Registre os handlers varrendo um assembly. Só os assemblies informados são varridos; handlers concretos e não genéricos
de pedido e de notification entram como transient. Pipeline behaviors não são varridos.

```csharp
services.AddHecoleMediator(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrder>());
```

Ou registre cada handler de forma explícita, sem varredura. `AddRequestHandler` e `AddNotificationHandler` podem ser
chamados antes ou depois de `AddHecoleMediator`, e as duas formas podem ser misturadas.

```csharp
services.AddHecoleMediator(_ => { });
services.AddRequestHandler<CreateOrder, OrderId, CreateOrderHandler>();
services.AddRequestHandler<CancelOrder, CancelOrderHandler>();
services.AddNotificationHandler<OrderCreated, SendConfirmationEmail>();
services.AddNotificationHandler<OrderCreated, UpdateSalesReport>();
```

Envie o pedido por `ISender` (ou `IMediator`, que é `ISender` mais `IPublisher`):

```csharp
public sealed class OrdersEndpoint(ISender sender)
{
    public Task<OrderId> Create(string customer, decimal total, CancellationToken cancellationToken)
        => sender.Send(new CreateOrder(customer, total), cancellationToken);
}
```

Regras de registro:

| Regra | Detalhe |
|---|---|
| Um handler por pedido | Dois handlers diferentes para o mesmo pedido lançam `InvalidOperationException` em `AddHecoleMediator` ou `AddRequestHandler`, com os dois nomes de tipo |
| Sem duplicação | O mesmo assembly, handler ou behavior registrado de novo (por varredura, de forma explícita ou em outra chamada de `AddHecoleMediator`) entra uma vez |
| `AddHecoleMediator` mais de uma vez | Permitido (por exemplo, uma vez por módulo). Toda chamada precisa usar os mesmos `Lifetime`, `RequireScope` e publisher de notification; senão lança |
| Tipo exato | O pedido é despachado pelo tipo real. Um pedido derivado sem handler próprio não usa o handler do tipo base |
| Tempo de vida | Handlers são transient. `IMediator`, `ISender` e `IPublisher` são transient por padrão; `cfg.Lifetime` muda isso |
| Handler registrado direto no container | `services.AddTransient<IRequestHandler<A, B>, H>()` também funciona |
| `RequireScope` (opcional, desligado por padrão) | Com `true`, `Send` e `Publish` lançam se o mediator veio do provider raiz em vez de um scope. Não combina com `Lifetime = Singleton` |

## Pedidos sem resposta

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

Dentro do pipeline, o pedido sem resposta tem `TResponse = Unit`, então os mesmos behaviors rodam para os dois tipos de
pedido. Quando o tipo do pedido só é conhecido em execução, `Send(object)` devolve a resposta, ou `Unit.Value` para
pedido sem resposta:

```csharp
object request = new CreateOrder("ACME", 10m);
object? response = await sender.Send(request, cancellationToken); // an OrderId
object? none = await sender.Send((object)new CancelOrder(orderId.Value), cancellationToken); // Unit.Value
```

`Send(object)` lança `ArgumentException` quando o objeto não é um pedido.

## Notifications e publishers

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

`IPublisher.Publish` tem também a forma `Publish(object)`. O `INotificationPublisher` decide como os handlers rodam:

| Publisher | Comportamento |
|---|---|
| `ForeachAwaitPublisher` (padrão) | Um handler por vez, na ordem de registro, cada um aguardado. A primeira exceção interrompe os demais e chega ao chamador |
| `TaskWhenAllPublisher` | Inicia todos os handlers e depois aguarda todos. A `Task` devolvida carrega todas as exceções. Um handler que lança antes de devolver a `Task` não impede os outros |
| O seu | Implemente `INotificationPublisher` e use `cfg.NotificationPublisherType` (resolvido do container) ou `cfg.NotificationPublisher` (uma instância) |

```csharp
services.AddHecoleMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrder>();
    cfg.NotificationPublisher = new TaskWhenAllPublisher();
});
```

Notification sem handler não faz nada. Handlers de um tipo base ou de `INotification` não rodam para uma notification
derivada (tipo exato). A mesma classe de handler registrada duas vezes roda uma vez.

## Pipeline behaviors

Um behavior envolve o handler. Pode rodar código antes e depois, mudar a resposta ou devolver sem chamar `next` (aí o
handler nem é criado).

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

Behaviors rodam na ordem de registro: o primeiro registrado é o mais externo.

```csharp
services.AddHecoleMediator(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<CreateOrder>();
    cfg.AddOpenBehavior(typeof(StopwatchBehavior<,>)); // outermost
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>)); // runs after StopwatchBehavior, before the handler
});
```

| Tema | Regra |
|---|---|
| Registro | `cfg.AddOpenBehavior(typeof(B<,>))`, `cfg.AddOpenBehaviors(...)`, `cfg.AddBehavior<TImplementation>()`, `cfg.AddBehavior<TService, TImplementation>()`; cada um aceita um `ServiceLifetime` opcional (transient por padrão). `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(B<,>))` também funciona e mantém seu lugar na ordem |
| Restrições | Um behavior aberto cujas restrições genéricas não servem para um pedido é pulado naquele pedido |
| Forma do behavior aberto | Exatamente dois parâmetros de tipo, declarado como `B<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>`. Outras formas são recusadas por `AddOpenBehavior`; registre-as fechadas com `AddBehavior` |
| Token de cancelamento | `next()` repassa o token do chamador; `next(token)` repassa `token` |
| Instâncias | Behaviors são resolvidos do container a cada `Send`, então seus tempos de vida são respeitados |

## Behaviors prontos (opcionais)

Quatro behaviors vêm em `Hecole.Mediator.Behaviors`. Nenhum é registrado se você não adicionar.

```csharp
services.AddHecoleMediator(cfg => cfg
    .RegisterServicesFromAssemblyContaining<CreateOrder>()
    .AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>))
    .AddOpenBehavior(typeof(LoggingBehavior<,>))
    .AddOpenBehavior(typeof(PerformanceBehavior<,>))
    .AddOpenBehavior(typeof(ValidationBehavior<,>)));
services.AddSingleton(new PerformanceBehaviorOptions { Threshold = TimeSpan.FromSeconds(1) });
```

| Behavior | O que faz | Eventos de log |
|---|---|---|
| `ValidationBehavior<,>` | Valida o pedido com `System.ComponentModel.DataAnnotations` (atributos de propriedade, depois atributos de classe e `IValidatableObject`). Em falha lança `RequestValidationException` (uma `ValidationException`) com todos os erros em `Errors`; o handler não roda | — |
| `LoggingBehavior<,>` | Registra início, fim, falha (só o nome do tipo da exceção) e cancelamento, com o tempo decorrido | 1001, 1002, 1003, 1004 (Information) |
| `PerformanceBehavior<,>` | Registra um aviso quando o pedido passa de `PerformanceBehaviorOptions.Threshold` (500 ms por padrão) | 2001 (Warning) |
| `UnhandledExceptionBehavior<,>` | Registra a exceção e relança. Cancelamento pedido pelo chamador é registrado em Debug | 3001 (Error), 3002 (Debug) |

O objeto do pedido nunca vai para o log: só o nome do tipo. Os behaviors de log precisam de `ILogger<T>` no container
(`services.AddLogging()`, que os builders do ASP.NET Core e do Generic Host já chamam).

Em record posicional, os atributos de validação vão na propriedade:

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

Se a falha de validação deve ser valor de retorno em vez de exceção, escreva o seu behavior: os prontos são
implementações comuns de `IPipelineBehavior`.

## Verificação na partida

`VerifyHecoleMediator` é opcional e só roda quando você chama, em geral logo depois de montar o container. Só lê os
registros (nenhum handler, behavior ou publisher é criado) e lança `MediatorConfigurationException` com todos os
problemas encontrados; `Problems` traz um por item.

```csharp
using var provider = services.BuildServiceProvider();
provider.VerifyHecoleMediator(typeof(CreateOrder).Assembly);
```

Confere que todo pedido registrado tem exatamente um handler no container, que todo tipo concreto de pedido dos
assemblies informados tem exatamente um handler e implementa uma única interface de pedido, e que o publisher de
notification e os handlers de notification registrados pela biblioteca estão no container. Numa aplicação ASP.NET Core,
chame em `app.Services`. `IRequestCatalog`, resolvido do container, lista os pedidos conhecidos no registro.

## Analyzers

O pacote inclui um analyzer Roslyn. Ele não acrescenta dependência em tempo de execução.

| Id | Detecta | Padrão |
|---|---|---|
| HM0001 | Tipo de pedido sem handler na compilação | Ligada (Info) |
| HM0002 | Mais de um handler para o mesmo pedido na compilação | Ligada (Info) |
| HM1001 | Varredura de assembly | Desligada |
| HM1002 | `Lifetime = ServiceLifetime.Singleton` | Desligada |
| HM1003 | Registro de pipeline behavior | Desligada |
| HM1004 | `TaskWhenAllPublisher` | Desligada |
| HM1005 | Handler sem `AddRequestHandler` correspondente | Desligada |

As regras `HM1xxx` apontam recursos legítimos que um projeto pode decidir proibir; ligue-as no `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.HM1001.severity = error
```

Detalhes, heurísticas e falsos positivos conhecidos: [docs/analyzers.md](docs/analyzers.md) (em inglês).

## Notas de comportamento

| Tema | Comportamento |
|---|---|
| Contratos | `IBaseRequest`, `IRequest`, `IRequest<TResponse>`, `Unit`, `INotification`, `IRequestHandler<TRequest, TResponse>`, `IRequestHandler<TRequest>`, `INotificationHandler<TNotification>`, `ISender`, `IPublisher`, `IMediator`, `IPipelineBehavior<TRequest, TResponse>`, `RequestHandlerDelegate<TResponse>`, `INotificationPublisher`, `NotificationHandlerExecutor`, `ForeachAwaitPublisher`, `TaskWhenAllPublisher` |
| Configuração | `AddHecoleMediator(cfg => ...)` com `HecoleMediatorConfiguration`: `RegisterServicesFromAssembly`, `RegisterServicesFromAssemblies`, `RegisterServicesFromAssemblyContaining`, `AddBehavior`, `AddOpenBehavior`, `AddOpenBehaviors`, `Lifetime`, `NotificationPublisher`, `NotificationPublisherType` |
| Dois handlers para um pedido | Lança no registro, com os dois nomes |
| `AddHecoleMediator` chamado de novo com outro `Lifetime`, `RequireScope` ou publisher | Lança, com os dois valores |
| Tipo de behavior ou de publisher inválido | Lança em `AddHecoleMediator` |
| Pipeline | Behaviors vêm do container na ordem de registro; o handler só é criado quando o pipeline chega nele; `next()` mantém o token do chamador |
| `TaskWhenAllPublisher`, handler que lança antes de devolver a `Task` | A exceção vai para a `Task` daquele handler; os outros handlers rodam |
| Notifications | O publisher padrão roda um handler por vez; a mesma classe de handler roda uma vez; despacho pelo tipo real exato |
| Despacho | Um executor tipado é criado no registro; reflection roda uma vez por tipo de pedido para handler registrado só no container |
| Extras opcionais | `AddRequestHandler`, `AddNotificationHandler`, `RequireScope`, `VerifyHecoleMediator`, `IRequestCatalog`, behaviors prontos, regras do analyzer |

## Ainda não suportado

| Recurso | Alternativa hoje |
|---|---|
| Pedidos com resposta em fluxo (respondidos com `IAsyncEnumerable<T>`, com pipeline próprio) | Nenhuma |
| Pre-processors e post-processors | Um pipeline behavior |
| Exception handlers e exception actions | Um pipeline behavior com `try`/`catch` |
| Classe de mediator pública, ou troca da implementação do mediator por tipo | Resolver `IMediator`, `ISender` ou `IPublisher` |
| Handlers de pedido genéricos abertos | Handlers fechados |
| Behaviors abertos cujos parâmetros de tipo não são `<TRequest, TResponse>`, nessa ordem | Registrar fechados com `AddBehavior` |

Esses itens podem entrar depois sem quebrar a API existente.

## Atualizando da 1.x

Veja [docs/migration-1.x-to-2.0.md](docs/migration-1.x-to-2.0.md) (em inglês).

## Requisitos

| Item | Valor |
|---|---|
| Target framework | `net10.0` (.NET 10 ou superior) |
| Dependências | `Microsoft.Extensions.DependencyInjection.Abstractions` e `Microsoft.Extensions.Logging.Abstractions`, 10.0.0 ou superior |
| Container | Testado com `Microsoft.Extensions.DependencyInjection`. Outros containers não foram testados |

## Mais documentação

| Documento | Conteúdo |
|---|---|
| [docs/analyzers.md](docs/analyzers.md) | Regras e configuração do analyzer (em inglês) |
| [docs/benchmarks.md](docs/benchmarks.md) | Custo medido de `Send` e `Publish`, 1.3.0 contra 2.0, e os limites dessa medição (em inglês) |
| [docs/migration-1.x-to-2.0.md](docs/migration-1.x-to-2.0.md) | Atualização a partir da 1.x (em inglês) |
| [CHANGELOG.md](CHANGELOG.md) | Mudanças por versão |

## Licença

MIT. Veja [LICENSE.txt](LICENSE.txt).
