# Changelog

Todas as mudanças notáveis neste projeto serão documentadas neste arquivo.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/),
e este projeto adere ao [Semantic Versioning](https://semver.org/lang/pt-BR/).

## [2.0.1] - 2026-11-17

Correções de defeitos encontrados na 2.0.0 por uma revisão externa, mais o que
faltava para a aplicação conferir a própria política na partida: o catálogo de
pedidos passa a informar o que de fato roda, e `VerifyHecoleMediator` passa a
conferir os registros da própria biblioteca. Nenhum membro público sai. Os
itens em Changed mudam o comportamento: um registro que antes era aceito e nunca funcionava agora lança
no registro, a verificação aponta casos que antes passavam em silêncio, e o
pipeline muda a regra do token, a forma de entregar exceções e o nome do pedido
nos logs.

### Added
- `RequestDescriptor.Behaviors` - os pipeline behaviors que rodam para o
  pedido, do mais externo para dentro, na ordem em que o container padrão os
  resolve (registros fechados e abertos misturados, na ordem de registro). Cada
  item é o tipo de implementação fechado (behavior aberto fechado sobre o
  pedido e a resposta); registro por instância: o tipo da instância; registro
  por factory: o próprio tipo de serviço `IPipelineBehavior<TRequest, TResponse>`.
- `RequestDescriptor.SkippedBehaviors` - os behaviors abertos registrados como
  `IPipelineBehavior<,>` que não rodam para o pedido porque as restrições
  genéricas não são satisfeitas, na ordem de registro.
- `IRequestCatalog.Find(Type)` - o descritor de um tipo exato de pedido, ou
  `null`. Tem implementação padrão na interface (busca em `Requests`), então
  um tipo que já implementa `IRequestCatalog` continua compilando.
- Igualdade de `RequestDescriptor` compara `Behaviors` e `SkippedBehaviors`
  item a item, em ordem.
- Analyzer: HM1006 (desligada por padrão) - aponta qualquer referência a
  `IRequestHandler<,>`/`IRequestHandler<>` fora da declaração de um handler
  (parâmetro, campo, propriedade, local, retorno, cast, `typeof`, argumento de
  tipo em qualquer profundidade, também por alias `using`): chamar ou resolver
  o handler direto pula o pipeline, e registrá-lo direto no container pula
  `AddRequestHandler`. Lista de bases, implementação explícita da interface,
  restrição genérica e decorator não são apontados.

### Changed
- **O que `IRequestCatalog` lista** - além dos pedidos registrados por
  varredura ou `AddRequestHandler` (primeiro, na ordem de registro), lista
  depois os pedidos cujo handler foi registrado direto no container como
  `IRequestHandler<,>`/`IRequestHandler<>` fechado, na ordem dos registros
  (antes não apareciam). Registro direto cujo pedido nunca pode ser despachado
  (pedido interface ou abstrato, resposta alargada) não é listado. O catálogo é
  calculado sobre uma cópia dos registros do `IServiceCollection` tirada na
  primeira resolução do mediator ou do catálogo (ou por
  `VerifyHecoleMediator`, o que vier antes), com as regras do container
  padrão (registros keyed ignorados), e os behaviors na primeira leitura; o
  `Send` não muda.
- **`RequestDescriptor.HandlerType`** - passa a ser o tipo de implementação do
  registro que o container usa (o último registro não keyed do serviço do
  handler), e não o tipo registrado pela biblioteca: depois de
  `services.Replace` do handler, o catálogo traz o substituto. Registro por
  instância: o tipo da instância; por factory: o próprio tipo de serviço
  (`IRequestHandler<TRequest, TResponse>` ou `IRequestHandler<TRequest>`).
- **Resposta alargada recusada** - `AddRequestHandler<Req, object, H>()` com
  `Req : IRequest<string>` compila (covariância de `IRequest<out T>`) e era
  aceito; um handler `IRequestHandler<Req, object>` achado na varredura também.
  Agora o tipo de resposta do registro precisa ser o que o pedido declara
  (`Unit` para `IRequest`); senão `ArgumentException` no registro, com pedido,
  resposta declarada, handler e resposta do handler.
- **Pedido que nunca pode ser despachado** - `AddRequestHandler` (as duas
  formas) e a varredura recusam com `ArgumentException` um `TRequest` que é
  interface ou classe abstrata (o despacho é pelo tipo real exato).
- **Behavior fechado que nunca roda** - `AddBehavior` (todas as formas) recusa
  com `InvalidOperationException` um `IPipelineBehavior<TRequest, TResponse>`
  fechado cujo `TRequest` é interface, classe abstrata ou não é pedido (não
  implementa exatamente uma interface de pedido), ou cujo `TResponse` difere
  da resposta que o pedido declara. Classe com várias interfaces de behavior
  fechadas: cada uma precisa ser válida. `AddOpenBehavior` não muda.
- **Sem deduplicação de notification handlers em tempo de execução** - antes
  a deduplicação pela classe concreta descartava handlers legítimos (dois
  handlers envolvidos pela mesma classe de decorator, ou duas instâncias da
  mesma classe vindas de duas factories). Agora roda o que o container devolve;
  a biblioteca continua sem registrar o mesmo handler duas vezes. Uma classe de
  handler registrada duas vezes direto no container roda duas vezes e
  `VerifyHecoleMediator` aponta isso.
- **Publisher escolhido e ignorado** - na primeira chamada de
  `AddHecoleMediator`, se já há um `INotificationPublisher` (não keyed) no
  container e a configuração escolheu um publisher (`NotificationPublisherType`
  ou `NotificationPublisher` diferente do padrão), lança
  `InvalidOperationException` com os dois. Com a configuração no padrão, o
  publisher do container continua mantido.
- **`VerifyHecoleMediator`** - não aponta mais como ausente um notification
  handler substituído por decorator (ausentes só contam quando passam do número
  de registros por factory do mesmo serviço); passa a apontar handler de
  notification registrado duas vezes, handler de pedido genérico aberto no
  container (`IRequestHandler<,>`/`IRequestHandler<>`, sem suporte, que antes
  contava como handler de todo pedido) e `IEnumerable<X>` registrado como
  serviço para `IPipelineBehavior`, `INotificationHandler` ou `IRequestHandler`
  fechado (substitui a lista que o container monta).
- **`VerifyHecoleMediator` confere os registros da própria biblioteca** -
  `AddHecoleMediator` guarda os registros que faz de `IMediator`, `ISender`,
  `IPublisher`, `IRequestCatalog`, `INotificationPublisher` (o que adiciona,
  ou o que já estava no container e é mantido) e do estado interno do
  mediator. A verificação aponta, um problema por serviço, quando o registro em
  vigor (o último não keyed do serviço) não é o da biblioteca: substituído
  (`services.Replace`), outro registrado depois, ou removido; a mensagem diz o
  que está no lugar (tipo, instância ou factory) e pede para remover o outro
  registro. Consequência: uma aplicação que substitui `IMediator`, `ISender`
  ou `IPublisher` de propósito (por exemplo, com um decorator próprio) passa a
  ter isso apontado. Um handler de pedido substituído com `services.Replace`
  continua aceito. Registros keyed não contam.
- **`VerifyHecoleMediator` lê o que o provider tem** - resolve primeiro o
  estado interno do mediator (o que também tira a cópia que o catálogo usa, e
  o primeiro `Send` não paga por ela) e faz toda verificação sobre essa mesma
  cópia, não sobre o `IServiceCollection` vivo: mudanças na coleção depois
  disso não mudam o resultado de uma segunda verificação. Um handler que está
  na cópia mas não é serviço do provider (quando o provider oferece
  `IServiceProviderIsService`) é apontado como adicionado à coleção depois que
  o provider foi construído. Falha ao criar o estado interno (factory que
  lança, registro que devolve outro objeto) vira um problema da lista em vez de
  outra exceção.
- Analyzer: as regras restritivas apontam referências a símbolos, não a forma
  de uma chamada. HM1003 aponta, além de `AddBehavior`/`AddOpenBehavior`/
  `AddOpenBehaviors`, a declaração de um tipo que implementa
  `IPipelineBehavior<,>` e todo `typeof` ou argumento de tipo que é, implementa
  ou contém um behavior (`IEnumerable<IPipelineBehavior<A, B>>`, helper
  genérico que não é extensão, `MakeGenericType`, tipos em variáveis passadas a
  `new ServiceDescriptor`, `Remove`/`RemoveAll`, `GetServices`); um projeto
  que só declara behaviors para o host registrar precisa desligar HM1003.
- Analyzer: HM1002 aponta toda atribuição a
  `HecoleMediatorConfiguration.Lifetime`, exceto um `Transient` ou `Scoped`
  constante.
- Analyzer: HM1001, HM1002 e HM1003 apontam a conversão de
  `HecoleMediatorConfiguration` para `dynamic`.
- Analyzer: em HM0001, um handler abstrato e um handler genérico aberto não
  cobrem mais um pedido; uma chamada de `AddRequestHandler` para o pedido na
  mesma compilação cobre.
- **`UnhandledExceptionBehavior` e falha de validação** - uma
  `RequestValidationException` era registrada como erro não tratado (3001,
  Error) com a mensagem completa, que traz o texto de cada erro (pode conter
  dado do usuário). Agora vai para o evento 3003 `RequestValidationFailed`, em
  Warning, só com o nome do pedido e a quantidade de erros; a exceção continua
  sendo relançada.
- **Regra do token no pipeline** - `next()` sem token (e `next(token)` com um
  token que não pode ser cancelado, como `CancellationToken.None`) voltava
  sempre ao token do chamador, mesmo depois de um behavior externo continuar com
  `next(outroToken)`; um behavior de timeout não chegava ao handler através de
  behaviors internos. Agora `next()` continua com o token que aquele behavior
  recebeu, e `next(token)` com token cancelável continua com ele. O behavior
  mais externo recebe o token do chamador; o handler recebe o token com que o
  behavior mais interno continuou. `next` continua podendo ser chamado mais de
  uma vez, cada chamada com o seu token. `LoggingBehavior` e
  `UnhandledExceptionBehavior` classificam o cancelamento pelo token que
  receberam (o do chamador quando são os mais externos).
- **Exceções sempre na `Task`** - `Send` e `Publish` lançam de forma síncrona
  só erros de argumento (argumento nulo, objeto que não é pedido ou
  notification, tipo de pedido com mais de uma interface de pedido). Todo o
  resto chega na `Task` devolvida: a verificação de `RequireScope`, pedido sem
  handler, falha ao criar behavior, handler ou publisher, e exceção lançada de
  forma síncrona por behavior ou handler (por exemplo `ValidationBehavior` como
  mais externo). `Task.WhenAll(Send, Send)` agrega as duas falhas. A exceção
  chega sem embrulho; uma `OperationCanceledException` lançada assim deixa a
  `Task` em estado Faulted, não Canceled.
- **Nome do pedido nos logs** - os behaviors prontos gravavam `Type.Name`
  (`Turmas.Command` e `Alunos.Command` saíam ambos como `Command`; um pedido
  genérico saía como `` Paged`1 ``). Agora o nome tem namespace, sem assembly,
  tipos aninhados unidos por `.` e argumentos genéricos em `<...>`, no mesmo
  formato (`My.App.Paged<My.App.Student>`). A mensagem de
  `RequestValidationException` usa o mesmo nome.

### Fixed
- Dois primeiros `Publish` simultâneos no mesmo mediator podiam usar dois
  publishers diferentes (publisher transient); agora o mediator usa uma única
  instância, sem lock no caminho comum.
- Handler já registrado direto no container
  (`services.AddTransient<IRequestHandler<Ping, string>, PingHandler>()`)
  seguido de `AddRequestHandler` (as duas formas) ou da varredura gerava dois
  registros iguais e `VerifyHecoleMediator` apontava dois handlers. Um registro
  com o mesmo serviço e a mesma classe não é mais repetido.
- A varredura de um assembly é tudo ou nada: tudo é lido e validado antes, e a
  coleção e o catálogo só mudam se o assembly inteiro é válido. Antes, uma
  falha no meio (handler duplicado, pedido inválido) deixava os registros já
  feitos e marcava o assembly como varrido, então uma nova varredura dele não
  fazia nada.
- `ValidationBehavior` com pedido struct: o pedido era convertido para objeto
  duas vezes e o validador lançava `ArgumentException`; agora é validado.
- Analyzer: o cabeçalho `// <auto-generated/>` escrito à mão silenciava
  HM1001 a HM1005 no arquivo. As regras restritivas agora analisam e reportam
  código gerado.
- Analyzer: um construtor não público que recebe a interface do handler
  isentava um handler comum de HM1005 e HM0002 como se fosse decorator; só
  construtores públicos contam.
- Remover o registro interno da biblioteca do `IServiceCollection` e chamar
  `AddHecoleMediator` de novo criava um segundo estado, registrava um segundo
  mediator e trocava as opções em silêncio. O estado fica associado à
  instância da coleção fora dela: uma nova chamada com outras opções lança o
  erro de sempre ("already called with ..."), e uma com as mesmas opções não
  muda nada e devolve o registro removido. Uma coleção cujos registros foram
  copiados para outra instância continua usando o mesmo estado.
- Analyzer: outro assembly referenciado que declarasse um tipo com o mesmo
  nome completo de um tipo da biblioteca desligava todas as regras em
  silêncio.
- Analyzer: HM1004 não via `TaskWhenAllPublisher` usado por um alias `using`
  ou `global using`.
- Analyzer: HM1005 apontava um handler registrado por um helper genérico que
  repassa os próprios parâmetros de tipo para `AddRequestHandler` (um nível de
  repasse agora conta).

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
  `RequestValidationException`; a dependência de validação de terceiros sai.
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
- Dependências de validação de terceiros e `Microsoft.Extensions.DependencyInjection`
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
