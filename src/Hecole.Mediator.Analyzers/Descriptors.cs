using Microsoft.CodeAnalysis;

namespace Hecole.Mediator.Analyzers;

/// <summary>
/// Diagnostic descriptors. Ids are stable: HM0xxx are general rules (on by default), HM1xxx are restrictive rules
/// (off by default; a project turns them on in .editorconfig). Never reuse or renumber an id.
/// </summary>
internal static class Descriptors
{
    public const string UsageCategory = "Hecole.Mediator.Usage";
    public const string RestrictionCategory = "Hecole.Mediator.Restriction";

    private const string HelpBase = "https://github.com/mvsergio/Hecole.Mediator/blob/master/docs/analyzers.md#";

    public static readonly DiagnosticDescriptor RequestWithoutHandler = new(
        id: "HM0001",
        title: "Request has no handler in this compilation",
        messageFormat: "Request '{0}' has no IRequestHandler in this compilation; sending it fails at run time",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A concrete request type is declared in a compilation that declares request handlers, but none of them handles it. "
            + "The mediator resolves handlers by the exact request type, so sending this request throws. "
            + "Not reported when the compilation declares no request handler at all (contracts assembly), "
            + "nor when an open generic handler could handle the request.",
        helpLinkUri: HelpBase + "hm0001",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor MultipleHandlers = new(
        id: "HM0002",
        title: "Request has more than one handler",
        messageFormat: "Request '{0}' has {1} handlers in this compilation: {2}",
        category: UsageCategory,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "More than one concrete class in the same compilation handles the same request. "
            + "Registering both (assembly scanning, or AddRequestHandler with each) throws when the services are registered. "
            + "Decorators (a handler that takes the same handler interface in its constructor) are not counted, and nothing is "
            + "reported when the compilation registers the request with AddRequestHandler (the handler is chosen explicitly).",
        helpLinkUri: HelpBase + "hm0002",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor AssemblyScanning = new(
        id: "HM1001",
        title: "Assembly scanning is not allowed",
        messageFormat: "'{0}' scans an assembly; register handlers explicitly with AddRequestHandler and AddNotificationHandler",
        category: RestrictionCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "Off by default. For projects that require every handler to be registered explicitly: "
            + "reports RegisterServicesFromAssembly, RegisterServicesFromAssemblies and RegisterServicesFromAssemblyContaining.",
        helpLinkUri: HelpBase + "hm1001");

    public static readonly DiagnosticDescriptor SingletonLifetime = new(
        id: "HM1002",
        title: "Singleton mediator lifetime is not allowed",
        messageFormat: "HecoleMediatorConfiguration.Lifetime is set to ServiceLifetime.Singleton",
        category: RestrictionCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "Off by default. For projects that forbid a Singleton mediator (it captures the root provider, so scoped "
            + "dependencies of handlers and behaviors are resolved from it). Reports a constant ServiceLifetime.Singleton "
            + "assigned to HecoleMediatorConfiguration.Lifetime.",
        helpLinkUri: HelpBase + "hm1002");

    public static readonly DiagnosticDescriptor BehaviorRegistration = new(
        id: "HM1003",
        title: "Pipeline behavior registration is not allowed in this project",
        messageFormat: "'{0}' registers a pipeline behavior; behaviors are registered only in the composition project",
        category: RestrictionCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "Off by default. Turned on in every project except the one that composes the application, so the pipeline "
            + "is declared in one place. Reports AddBehavior, AddOpenBehavior, AddOpenBehaviors and IPipelineBehavior<,> "
            + "registered directly in an IServiceCollection (generic type argument or typeof argument).",
        helpLinkUri: HelpBase + "hm1003");

    public static readonly DiagnosticDescriptor ParallelPublisher = new(
        id: "HM1004",
        title: "TaskWhenAllPublisher is not allowed",
        messageFormat: "TaskWhenAllPublisher runs notification handlers concurrently; use the default ForeachAwaitPublisher",
        category: RestrictionCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "Off by default. For projects whose notification handlers share non thread-safe state (a DbContext, a unit "
            + "of work) and must run one at a time. Reports every reference to TaskWhenAllPublisher.",
        helpLinkUri: HelpBase + "hm1004");

    public static readonly DiagnosticDescriptor HandlerNotRegistered = new(
        id: "HM1005",
        title: "Request handler is not registered with AddRequestHandler",
        messageFormat: "'{0}' handles '{1}' but this compilation has no matching AddRequestHandler call",
        category: RestrictionCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "Off by default. For projects that forbid scanning and register handlers in the same project that declares "
            + "them: every concrete request handler needs AddRequestHandler<TRequest, TResponse, THandler>() or "
            + "AddRequestHandler<TRequest, THandler>() for each request it handles, in the same compilation.",
        helpLinkUri: HelpBase + "hm1005",
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}
