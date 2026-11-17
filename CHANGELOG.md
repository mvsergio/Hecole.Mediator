# Changelog

Todas as mudanças notáveis neste projeto serão documentadas neste arquivo.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/),
e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [2.0.0] - 2026-11-17

Reescrita com contratos novos no namespace `Hecole.Mediator`. Sem
compatibilidade de código com a 1.x: veja o
[guia de migração](docs/migration-1.x-to-2.0.md).

### Breaking
- **Só `net10.0`** — o alvo `net8.0` sai; quem está em .NET 8 fica na 1.3.0.
- **Namespaces** — contratos em `Hecole.Mediator` (saem
  `Hecole.Mediator.Interfaces` e `Hecole.Mediator.Interfaces.Behaviors`);
  registro em `Microsoft.Extensions.DependencyInjection`; behaviors prontos em
  `Hecole.Mediator.Behaviors`; publishers em `Hecole.Mediator.NotificationPublishers`.
- **`ICoreMediator` substituído por `IMediator`, `ISender` e `IPublisher`**; a classe
  `CoreMediator` deixa de ser pública.
- **Registro** — `AddHecoleMediator(params Assembly[])` e `AddCoreMediator`
  dão lugar a `AddHecoleMediator(cfg => ...)`. Só os assemblies informados em
  `cfg.RegisterServicesFrom...` são varridos (nunca o `AppDomain` inteiro);
  behaviors não são mais varridos.
- **Tempo de vida do mediator** — Transient por padrão (era Scoped em
  `AddHecoleMediator` e Singleton em `AddCoreMediator`); `cfg.Lifetime` muda.
- **`IPipelineBehavior`** — restrição passa de `TRequest : IRequest<TResponse>`
  para `TRequest : notnull`; `RequestHandlerDelegate<TResponse>` recebe
  `CancellationToken` opcional.
- **Notifications sequenciais por padrão** (`ForeachAwaitPublisher`); a
  execução em paralelo passa a ser opção (`TaskWhenAllPublisher`).
- **`ValidationBehavior`** usa `System.ComponentModel.DataAnnotations` e lança
  `RequestValidationException`; FluentValidation sai.
- **Dois handlers para o mesmo pedido** lançam `InvalidOperationException` no
  registro, com os dois nomes (antes o último registrado vencia).

### Added
- Pedido sem resposta: `IRequest`, `IRequestHandler<TRequest>`, `Unit`,
  `IBaseRequest`.
- `ISender.Send(object)` e `IPublisher.Publish(object)`.
- `HecoleMediatorConfiguration` com `RegisterServicesFromAssembly`,
  `RegisterServicesFromAssemblies`, `RegisterServicesFromAssemblyContaining`,
  `AddBehavior`, `AddOpenBehavior`, `AddOpenBehaviors`, `Lifetime`,
  `NotificationPublisher` e `NotificationPublisherType`.
- Registro explícito, opcional: `AddRequestHandler` e `AddNotificationHandler`.
- `INotificationPublisher`, `NotificationHandlerExecutor`,
  `ForeachAwaitPublisher` e `TaskWhenAllPublisher`.
- `RequireScope` (opcional, desligado): recusa `Send`/`Publish` de mediator
  resolvido do provider raiz.
- `VerifyHecoleMediator` (verificação opcional na partida),
  `MediatorConfigurationException` com `Problems`, e `IRequestCatalog`.
- `PerformanceBehaviorOptions` (limite configurável) e event ids estáveis nos
  behaviors de log.
- Analyzer Roslyn dentro do pacote: HM0001 e HM0002 ligadas; HM1001 a HM1005
  desligadas por padrão ([docs/analyzers.md](docs/analyzers.md)).
- `AddHecoleMediator` pode ser chamado mais de uma vez sem duplicar registros.
- Benchmark sem pacote extra (`benchmarks/`) e [docs/benchmarks.md](docs/benchmarks.md).

### Changed
- Despacho por executor tipado criado no registro, sem reflection por chamada;
  reflection uma vez por tipo só para handler registrado direto no container.
- Handler resolvido só quando o pipeline chega nele: behavior que interrompe a
  cadeia não cria o handler.
- Notification handlers deduplicados pela classe concreta.
- Exceção síncrona do handler sai do `Send` sem embrulho em
  `TargetInvocationException`.
- Behaviors prontos não gravam mais o objeto do pedido no log, só o nome do tipo.
- Dependências: `Microsoft.Extensions.DependencyInjection.Abstractions` e
  `Microsoft.Extensions.Logging.Abstractions` 10.0.0.
- README reescrito em inglês, com versão separada em português.

### Fixed
- `AddCoreMediator` registrava o mediator como Singleton, capturando serviços
  Scoped (como `DbContext`) do provider raiz.
- `ValidationBehavior` rodava os validators em paralelo sobre um mesmo contexto,
  repetindo erros.
- `UnhandledExceptionBehavior` gravava o pedido inteiro no log (`{@Request}`).
- `PerformanceBehavior` com limite fixo de 500 ms e tempo medido duas vezes.
- Registro do mesmo assembly mais de uma vez fazia notification e behavior
  rodarem duas vezes.
- `Send<TResponse>` com `TResponse` estático diferente do real (covariância de
  `IRequest<out TResponse>`) não achava o handler.
- `Publish`: exceção síncrona de um handler impedia os seguintes; classe que
  trata duas notifications lançava `AmbiguousMatchException`.
- Handler ausente só aparecia no primeiro `Send`; agora pode ser detectado na
  partida com `VerifyHecoleMediator` e na compilação com o analyzer.
- README afirmava "sem reflection no hot path", "tratamento isolado de exceções"
  e "fire-and-forget" para notifications, o que o código não fazia; link da
  licença quebrado.

### Removed
- Alvo `net8.0`.
- `ICoreMediator`, `CoreMediator` público, `AddCoreMediator` e
  `AddHecoleMediator(params Assembly[])`.
- Dependências `FluentValidation` e `Microsoft.Extensions.DependencyInjection`
  (fica só o pacote `Abstractions`).
- Varredura do `AppDomain` quando nenhum assembly é informado.

## [1.3.0] - 2026-04-28

### Fixed
- **`AddHecoleMediator` open-generic `IPipelineBehavior<,>` registration** —
  scan automático do assembly registrava behaviors open-generic
  (ex: `ObservabilityBehavior<TRequest, TResponse>`) com tipos parametrizados
  da classe ao invés de `GetGenericTypeDefinition()`. Resultado: MS DI lançava
  `ArgumentException: "Cannot instantiate implementation type"` em
  `BuildServiceProvider`/resolve. O fix detecta `IsGenericTypeDefinition` e
  registra como `(typeof(IPipelineBehavior<,>), implementationType)` aberto.

### Added
- Tests dedicados em `AddHecoleMediatorTests`:
  - `OpenGenericBehavior_RegistersAsDefinitionMapping_DoesNotThrowOnBuild`
  - `OpenGenericBehavior_RegisteredAsDefinitionToDefinition`
  - `OpenGenericBehavior_RegisteredViaScan_RunsInPipeline`
  - `ClosedGenericHandlers_StillRegistered`
  - `NotificationHandlers_StillRegistered`
- Fake `OpenGenericTestBehavior<TRequest, TResponse>` em test fixtures

### Migration
Sem breaking change. Consumidores que usaram workaround manual
`services.Remove(...) + AddTransient(typeof(IPipelineBehavior<,>), typeof(MyBehavior<,>))`
após `AddHecoleMediator(...)` podem remover o hack — `AddHecoleMediator`
agora registra corretamente.

## [1.2.1] - 2026-03-30

### Changed
- README bilíngue: inglês como principal, seguido de cópia em português, e
  `README.pt-BR.md` separado
- Versão atualizada para 1.2.1

## [1.2.0] - 2026-03-30

### Fixed
- **ValidationBehavior agora usa `ValidateAsync()` corretamente** —
  validators com `MustAsync()` não são mais ignorados silenciosamente.
  Anteriormente, o behavior chamava `Validate()` (síncrono), fazendo com que
  regras assíncronas fossem ignoradas e dados inválidos passassem sem erro.
  O `CancellationToken` agora é propagado para `ValidateAsync()`.

### Added
- **Multi-targeting: suporte a `net8.0` e `net10.0`** —
  consumidores em .NET 8 continuam funcionando; consumidores em .NET 10
  ganham as otimizações nativas da plataforma. O NuGet resolve
  automaticamente o target correto.
- Projeto de testes unitários (`Hecole.Mediator.Tests`) com cobertura de:
  - Dispatch de commands e queries
  - ValidationBehavior (síncrono e assíncrono, incluindo `MustAsync`)
  - Pipeline ordering (behaviors executam na ordem correta)
  - Registro de DI (`AddHecoleMediator`, `AddCoreMediator`)
  - Notifications (múltiplos handlers, handlers com exceção)
  - Propagação de `CancellationToken`
- XML documentation em todas as interfaces e classes públicas

### Changed
- Versão atualizada para 1.2.0

## [1.1.0] - 2026-03-28

### Fixed
- Correção de falha silenciosa no cast de pipeline behaviors no CoreMediator

### Changed
- Atualização da versão do projeto e README com link para o NuGet

## [1.0.0] - 2026-03-27

### Added
- Implementação inicial do Mediator pattern para .NET 8
- Suporte a CQRS (Commands, Queries, Requests e Notifications)
- Pipeline de behaviors (ValidationBehavior, LoggingBehavior,
  PerformanceBehavior, UnhandledExceptionBehavior)
- Registro automático via DI com `AddHecoleMediator` e `AddCoreMediator`
- Caching de reflection com `ConcurrentDictionary`
- Execução paralela de notification handlers via `Task.WhenAll`
