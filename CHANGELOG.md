# Changelog

Todas as mudanças notáveis neste projeto serão documentadas neste arquivo.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/),
e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

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
