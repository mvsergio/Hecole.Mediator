# \# 🧩 Hecole.Mediator

# 

# Uma implementação \*\*leve\*\*, \*\*performática\*\* e \*\*sem dependências externas\*\* do padrão \*\*Mediator\*\* para \*\*.NET 8\*\*, inspirada no \*\*MediatR\*\*.  

# Projetada para projetos \*\*modulares\*\* baseados em \*\*Clean Architecture\*\*, com suporte a \*\*CQRS\*\* (Commands, Queries, Requests e Notifications), \*\*pipelines de behaviors\*\* (ex.: validação, logging, performance monitoring e tratamento de exceções) e \*\*integração nativa\*\* com o \*\*Dependency Injection (DI)\*\* de `Microsoft.Extensions`.

# 

# Ideal para sistemas como \*\*gerenciamento escolar\*\* (ex.: o projeto \_Hecole\_), onde módulos independentes (\_Cadastro\_, \_Pedagógico\_, \_Tesouraria\_) precisam de \*\*orquestração de use cases com baixa acoplagem\*\*.

# 

# ---

# 

# \## 🚀 Features Principais

# 

# \- ✅ \*\*Suporte a CQRS\*\*  

# &nbsp; Interfaces para `IRequest<TResponse>`, `IRequestHandler<TRequest, TResponse>`, `INotification` e `INotificationHandler<TNotification>`.

# 

# \- 🧠 \*\*Pipelines e Behaviors\*\*  

# &nbsp; Cadeia de middlewares para cross-cutting concerns, como \*\*validação assíncrona\*\* (com FluentValidation), \*\*logging estruturado\*\*, \*\*monitoramento de performance\*\* e \*\*captura de exceções\*\*.

# 

# \- ⚙️ \*\*Registro Automático via DI\*\*  

# &nbsp; Extensão `AddHecoleMediator` para scan automático de assemblies e registro de handlers/behaviors.

# 

# \- ⚡ \*\*Performance Otimizada\*\*  

# &nbsp; Caching de invokers com `ConcurrentDictionary` para evitar reflection no hot path; execução paralela de notifications.

# 

# \- 🧱 \*\*Robustez\*\*  

# &nbsp; Tratamento isolado de exceções em handlers, suporte a múltiplos validators e \_fire-and-forget\_ para events.

# 

# \- 🧩 \*\*Zero Dependências Externas\*\*  

# &nbsp; Apenas `Microsoft.Extensions.DependencyInjection` (e opcionais como FluentValidation).

# 

# \- 🧭 \*\*Alinhado com Clean Architecture\*\*  

# &nbsp; Interfaces puras para Domain/SharedKernel e implementações plugáveis na camada Infrastructure.

# 

# ---

# 

# \## 🧰 Requisitos

# 

# \- .NET \*\*8.0\*\* ou superior.  

# \- Pacotes opcionais:

# &nbsp; - `FluentValidation` (para `ValidationBehavior`)

# &nbsp; - `Microsoft.Extensions.Logging` (para logging estruturado)

# 

# ---

# 

# \## 💾 Instalação

# 

# Clone o repositório:

# 

# ```bash

# git clone https://github.com/seu-usuario/hecole-mediator.git

# ```

# 

# Adicione como referência ao seu projeto (`.csproj`):

# 

# ```xml

# <ItemGroup>

# &nbsp; <ProjectReference Include="..\\Hecole.Mediator\\Hecole.Mediator.csproj" />

# </ItemGroup>

# ```

# 

# Ou (futuramente, via NuGet):

# 

# ```bash

# dotnet add package Hecole.Mediator

# ```

# 

# ---

# 

# \## 🧠 Uso

# 

# \### Registro no DI (`Program.cs` ou `Bootstrap.cs`)

# 

# ```csharp

# using Hecole.Mediator.Implementation.Extensions;

# using System.Reflection;

# 

# var builder = WebApplication.CreateBuilder(args);

# 

# // Registra o Mediator e scannea assemblies (ex.: camada Application)

# builder.Services.AddHecoleMediator(

# &nbsp;   Assembly.GetAssembly(typeof(CadastrarInstituicaoCommandHandler)) // Seu handler

# );

# 

# // Behaviors globais (ordem importa: o último adicionado é o mais externo)

# builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));

# builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

# builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

# builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

# 

# // Validator específico

# builder.Services.AddTransient<IValidator<CadastrarInstituicaoCommand>, CadastrarInstituicaoCommandValidator>();

# ```

# 

# ---

# 

# \### Exemplo de Handler (em `Application.UseCases`)

# 

# ```csharp

# using Hecole.Mediator.Interfaces;

# 

# public class CadastrarInstituicaoCommandHandler 

# &nbsp;   : IRequestHandler<CadastrarInstituicaoCommand, CadastrarInstituicaoCommandResponse>

# {

# &nbsp;   private readonly IRepository \_repository;

# 

# &nbsp;   public CadastrarInstituicaoCommandHandler(IRepository repository)

# &nbsp;   {

# &nbsp;       \_repository = repository;

# &nbsp;   }

# 

# &nbsp;   public async Task<CadastrarInstituicaoCommandResponse> Handle(

# &nbsp;       CadastrarInstituicaoCommand request, 

# &nbsp;       CancellationToken ct)

# &nbsp;   {

# &nbsp;       // Lógica do use case

# &nbsp;       await \_repository.SaveAsync(request);

# &nbsp;       return new CadastrarInstituicaoCommandResponse(/\* resultado \*/);

# &nbsp;   }

# }

# ```

# 

# ---

# 

# \### Exemplo em Controller (`WebApi`)

# 

# ```csharp

# using Hecole.Mediator.Interfaces;

# using Microsoft.AspNetCore.Mvc;

# 

# \[ApiController]

# \[Route("api/instituicoes")]

# public class InstituicaoController : ControllerBase

# {

# &nbsp;   private readonly ICoreMediator \_mediator;

# 

# &nbsp;   public InstituicaoController(ICoreMediator mediator)

# &nbsp;   {

# &nbsp;       \_mediator = mediator;

# &nbsp;   }

# 

# &nbsp;   \[HttpPost]

# &nbsp;   public async Task<IActionResult> Cadastrar(\[FromBody] CadastrarInstituicaoCommand command)

# &nbsp;   {

# &nbsp;       var response = await \_mediator.Send(command);

# &nbsp;       return Ok(response);

# &nbsp;   }

# }

# ```

# 

# ---

# 

# \### Exemplo de Behavior Custom

# 

# Crie e registre behaviors para estender o pipeline, como validações assíncronas, auditoria, métricas, etc.

# 

# ---

# 

# \## 🤝 Contribuições

# 

# Contribuições são \*\*bem-vindas\*\*!  

# 

# Siga estes passos:

# 

# ```bash

# \# Fork o repositório

# git clone https://github.com/seu-usuario/hecole-mediator.git

# 

# \# Crie uma branch

# git checkout -b feature/nova-feature

# 

# \# Commit suas mudanças

# git commit -m "Adiciona nova feature"

# 

# \# Push para a branch

# git push origin feature/nova-feature

# ```

# 

# Abra um \*\*Pull Request\*\* e descreva suas alterações.  

# Lembre-se de adicionar \*\*testes unitários\*\* e seguir o estilo de código existente (`#nullable enable`, `async/await`, etc).

# 

# ---

# 

# \## ⚖️ Licença

# 

# Distribuído sob a \*\*MIT License\*\*.  

# Veja o arquivo \[LICENSE](./LICENSE) para mais detalhes.

# 

# ---



