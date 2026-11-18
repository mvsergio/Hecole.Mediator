using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Singleton state shared by every <see cref="Mediator"/> of one container: the request and notification catalogs and
/// the root provider. Created on the first resolution of the mediator or of <see cref="IRequestCatalog"/>, or by
/// <c>VerifyHecoleMediator</c>; it copies, at that moment, the registrations of <see cref="HandlerRegistry"/>, the
/// non-keyed service descriptors of its collection and the descriptors of the library's own services. The request
/// catalog and <c>VerifyHecoleMediator</c> read the same copies. Being a singleton, it receives the root
/// <see cref="IServiceProvider"/>; a mediator that receives the same instance was resolved from the root, not from a
/// scope (verified against Microsoft.Extensions.DependencyInjection 10).
/// </summary>
internal sealed class MediatorRuntime
{
    public MediatorRuntime(IServiceProvider rootProvider, HandlerRegistry registry, bool requireScope)
    {
        RootProvider = rootProvider;
        Registry = registry;
        RequireScope = requireScope;
        Registrations = [.. registry.Registrations];
        NotificationRegistrations = [.. registry.NotificationRegistrations];
        OwnDescriptors = [.. registry.OwnDescriptors];
        Services = registry.Services.Where(static d => !d.IsKeyedService).ToArray();
        Catalog = new RequestCatalog(Registrations, Services);
        Notifications = new NotificationCatalog(registry.NotificationExecutors);
    }

    public IServiceProvider RootProvider { get; }

    /// <summary>The registry this runtime was created from.</summary>
    public HandlerRegistry Registry { get; }

    /// <summary>Copy of <see cref="HandlerRegistry.Registrations"/>.</summary>
    public HandlerRegistration[] Registrations { get; }

    /// <summary>Copy of <see cref="HandlerRegistry.NotificationRegistrations"/>.</summary>
    public NotificationRegistration[] NotificationRegistrations { get; }

    /// <summary>Copy of <see cref="HandlerRegistry.OwnDescriptors"/>.</summary>
    public ServiceDescriptor[] OwnDescriptors { get; }

    /// <summary>Copy of the non-keyed descriptors of <see cref="HandlerRegistry.Services"/>, in registration order.</summary>
    public ServiceDescriptor[] Services { get; }

    public RequestCatalog Catalog { get; }

    public NotificationCatalog Notifications { get; }

    public bool RequireScope { get; }
}
