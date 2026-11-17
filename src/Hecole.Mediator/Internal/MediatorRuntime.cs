namespace Hecole.Mediator.Internal;

/// <summary>
/// Singleton state shared by every <see cref="Mediator"/> of one container: the request and notification catalogs and
/// the root provider. Being a singleton, it receives the root <see cref="IServiceProvider"/>; a mediator that receives
/// the same instance was resolved from the root, not from a scope (verified against Microsoft.Extensions.DependencyInjection 10).
/// </summary>
internal sealed class MediatorRuntime(IServiceProvider rootProvider, HandlerRegistry registry, bool requireScope)
{
    public IServiceProvider RootProvider { get; } = rootProvider;

    public RequestCatalog Catalog { get; } = new(registry.Registrations);

    public NotificationCatalog Notifications { get; } = new(registry.NotificationExecutors);

    public bool RequireScope { get; } = requireScope;
}
