using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Checks behind <c>VerifyHecoleMediator</c>. Resolves <see cref="MediatorRuntime"/> from the provider and reads the copies
/// it took when it was created (registrations, non-keyed descriptors of the collection, descriptors of the library's own
/// services: the same copies the request catalog reads), and asks <see cref="IServiceProviderIsService"/>; never resolves
/// a handler, behavior or publisher.
/// </summary>
internal static class MediatorVerification
{
    private const string InternalState = "the mediator's internal state";

    public static List<string> FindProblems(IServiceProvider provider, IReadOnlyList<Assembly> requestAssemblies)
    {
        var problems = new List<string>();

        if (ResolveRuntime(provider, problems) is not { } runtime)
            return problems;

        var isService = provider.GetService<IServiceProviderIsService>();
        IReadOnlyList<ServiceDescriptor> services = runtime.Services;

        // The library's own services: the registration in effect must be the one AddHecoleMediator made.
        CheckOwnServices(runtime, problems);

        // (a) Requests known at registration: exactly one handler in the container.
        var known = new HashSet<Type>();
        foreach (var registration in runtime.Registrations)
        {
            known.Add(registration.RequestType);
            CheckSingleHandler(registration.RequestType, registration.ServiceType, registration.HandlerType, services, isService, problems);
        }

        // (b) Request types of the assemblies given: one request interface, exactly one handler.
        var assemblies = new HashSet<Assembly>();
        foreach (var assembly in requestAssemblies)
        {
            if (!assemblies.Add(assembly))
                continue;

            foreach (var type in LoadableTypes(assembly).OrderBy(static t => t.FullName, StringComparer.Ordinal))
            {
                if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || known.Contains(type))
                    continue;

                var contracts = RequestInterfaces(type);
                if (contracts.Count == 0)
                    continue;

                if (contracts.Count > 1)
                {
                    problems.Add(
                        $"The request '{type.FullName}' implements more than one request interface " +
                        $"({string.Join(", ", contracts.Select(Name))}); it must implement exactly one.");
                    continue;
                }

                var serviceType = contracts[0] == typeof(IRequest)
                    ? typeof(IRequestHandler<>).MakeGenericType(type)
                    : typeof(IRequestHandler<,>).MakeGenericType(type, contracts[0].GetGenericArguments()[0]);
                CheckSingleHandler(type, serviceType, expectedHandler: null, services, isService, problems);
            }
        }

        // Notifications: the handlers registered by scan or AddNotificationHandler (the publisher is checked with the
        // library's own services). A handler replaced by a decorator (a factory registration in its place) is not
        // missing: missing handlers of a notification are reported only when there are more of them than factory
        // registrations of that service type.
        foreach (var group in runtime.NotificationRegistrations.GroupBy(static r => r.NotificationType))
        {
            var serviceType = typeof(INotificationHandler<>).MakeGenericType(group.Key);
            var descriptors = Registrations(serviceType, services).ToList();
            var missing = group
                .Where(r => !descriptors.Any(d => ImplementationType(d) == r.HandlerType))
                .ToList();
            var factories = descriptors.Count(static d => d.ImplementationType is null);

            if (missing.Count > factories)
            {
                foreach (var registration in missing)
                {
                    problems.Add(
                        $"The handler '{registration.HandlerType.FullName}' of the notification '{registration.NotificationType.FullName}' " +
                        "was registered by Hecole.Mediator but is no longer in the container.");
                }
            }
        }

        // The same handler class registered twice for a notification runs twice.
        foreach (var duplicate in services
                     .Where(static d => !d.IsKeyedService && d.ImplementationType is not null && IsClosed(d.ServiceType, typeof(INotificationHandler<>)))
                     .GroupBy(static d => (d.ServiceType, d.ImplementationType))
                     .Where(static g => g.Count() > 1))
        {
            var (serviceType, handlerType) = duplicate.Key;
            problems.Add(
                $"The handler '{handlerType!.FullName}' of the notification '{serviceType.GetGenericArguments()[0].FullName}' is registered " +
                $"{duplicate.Count()} times in the container, so it runs {duplicate.Count()} times for each notification. Remove the extra " +
                "registration (AddNotificationHandler and the assembly scan never register the same handler twice).");
        }

        // Open generic request handlers: not supported, and the container would report them as a handler of every request.
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService
                && (descriptor.ServiceType == typeof(IRequestHandler<,>) || descriptor.ServiceType == typeof(IRequestHandler<>)))
            {
                problems.Add(
                    $"An open generic request handler ({Describe(descriptor)}) is registered as {OpenName(descriptor.ServiceType)}. Open generic " +
                    "request handlers are not supported: each request needs its own closed handler. Remove the registration.");
            }
        }

        // IEnumerable<X> registered as a service replaces the list the container builds from the registrations of X.
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService
                && descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                && descriptor.ServiceType.GetGenericArguments()[0] is var item
                && (IsClosed(item, typeof(IPipelineBehavior<,>)) || IsClosed(item, typeof(INotificationHandler<>))
                    || IsClosed(item, typeof(IRequestHandler<,>)) || IsClosed(item, typeof(IRequestHandler<>))))
            {
                problems.Add(
                    $"{Name(descriptor.ServiceType)} is registered as a service ({Describe(descriptor)}); it replaces the list the container " +
                    $"builds from the registrations of {Name(item)}, so those registrations are ignored. Register each item as {Name(item)} instead.");
            }
        }

        return problems;
    }

    private static bool IsClosed(Type type, Type definition)
        => type.IsGenericType && !type.ContainsGenericParameters && type.GetGenericTypeDefinition() == definition;

    private static string OpenName(Type definition)
    {
        var name = definition.FullName!;
        return $"{name[..name.IndexOf('`')]}<{new string(',', definition.GetGenericArguments().Length - 1)}>";
    }

    /// <summary>
    /// Resolves the runtime (which also builds the copies the catalog reads, so the first <c>Send</c> does not pay for
    /// them). A failure, a missing runtime or an object that is not the library's runtime becomes one problem.
    /// </summary>
    private static MediatorRuntime? ResolveRuntime(IServiceProvider provider, List<string> problems)
    {
        object? resolved;
        try
        {
            resolved = provider.GetService(typeof(MediatorRuntime));
        }
        catch (Exception ex)
        {
            problems.Add(
                $"Hecole.Mediator could not create {InternalState}: {ex.GetType().FullName}: {ex.Message.TrimEnd('.')}. A registration that " +
                "replaces the one made by AddHecoleMediator is the usual cause; remove it.");
            return null;
        }

        if (resolved is MediatorRuntime runtime)
            return runtime;

        if (resolved is not null)
        {
            problems.Add(
                $"The registration of {InternalState} returns an instance of {resolved.GetType().FullName}, not the one " +
                "AddHecoleMediator registers. Remove the registration that replaces it.");
            return null;
        }

        HandlerRegistry? registry;
        try
        {
            registry = provider.GetService<HandlerRegistry>();
        }
        catch (Exception)
        {
            registry = null;
        }

        problems.Add(registry is { Mediator: not null }
            ? $"The registration of {InternalState} made by AddHecoleMediator was removed from the container; Send and Publish " +
              "would fail. It must not be removed: remove the code that removes it."
            : "Hecole.Mediator is not registered in this container: call services.AddHecoleMediator(...) before building it.");
        return null;
    }

    /// <summary>
    /// The registration in effect for each service <c>AddHecoleMediator</c> registered (the last non-keyed descriptor of
    /// its service type) must be the descriptor it recorded: one problem per service otherwise.
    /// </summary>
    private static void CheckOwnServices(MediatorRuntime runtime, List<string> problems)
    {
        var services = runtime.Services;
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var own in runtime.OwnDescriptors)
        {
            var serviceType = own.ServiceType;
            ServiceDescriptor? effective = null;
            for (var i = services.Length - 1; i >= 0; i--)
            {
                if (services[i].ServiceType == serviceType)
                {
                    effective = services[i];
                    break;
                }
            }

            if (ReferenceEquals(effective, own))
                continue;

            var isInternal = serviceType == typeof(MediatorRuntime) || serviceType == typeof(HandlerRegistry);
            var subject = isInternal ? "The registration of " + InternalState : $"The registration of {serviceType.FullName}";
            string problem;

            if (effective is null)
            {
                problem = serviceType == typeof(INotificationPublisher)
                    ? $"No {nameof(INotificationPublisher)} is registered; Publish would fail. It is registered by AddHecoleMediator " +
                      "and must not be removed."
                    : $"{subject} made by AddHecoleMediator was removed from the container. It must not be removed: remove the code " +
                      "that removes it.";
            }
            else
            {
                var where = Array.IndexOf(services, own) >= 0 ? "registered after it" : "registered in its place";
                problem =
                    $"{subject} made by AddHecoleMediator is not the one in effect: the container uses {DescribeRegistration(effective)}, " +
                    $"{where}. Remove the other registration; Hecole.Mediator must provide {(isInternal ? InternalState : serviceType.Name)}" +
                    Hint(serviceType);
            }

            if (reported.Add(problem))
                problems.Add(problem);
        }
    }

    private static string Hint(Type serviceType)
        => serviceType == typeof(INotificationPublisher)
            ? $" (choose the publisher with {nameof(HecoleMediatorConfiguration)}.{nameof(HecoleMediatorConfiguration.NotificationPublisherType)} " +
              $"or {nameof(HecoleMediatorConfiguration.NotificationPublisher)}, or register it before AddHecoleMediator)."
            : serviceType == typeof(IMediator) || serviceType == typeof(ISender) || serviceType == typeof(IPublisher)
                ? " (to run code around every request, add a pipeline behavior instead of replacing the mediator)."
                : ".";

    private static string DescribeRegistration(ServiceDescriptor descriptor)
        => descriptor.ImplementationType is { } type
            ? $"'{type.FullName}'"
            : descriptor.ImplementationInstance is { } instance
                ? $"an instance of '{instance.GetType().FullName}'"
                : "a factory registration";

    private static void CheckSingleHandler(
        Type requestType, Type serviceType, Type? expectedHandler, IReadOnlyList<ServiceDescriptor> services, IServiceProviderIsService? isService,
        List<string> problems)
    {
        var handlers = Registrations(serviceType, services).ToList();
        if (handlers.Count > 0 && isService is not null && !isService.IsService(serviceType))
        {
            // The copy has the handler but this provider does not: the collection changed after the provider was built.
            problems.Add(
                $"The handler of the request '{requestType.FullName}' ({string.Join(", ", handlers.Select(Describe))}) was added to the " +
                "service collection after this provider was built, so this provider cannot resolve it. Register it before building " +
                "the provider.");
        }
        else if (handlers.Count > 1)
        {
            problems.Add(
                $"The request '{requestType.FullName}' has {handlers.Count} handlers in the container " +
                $"({string.Join(", ", handlers.Select(Describe))}); a request has exactly one handler. Remove the extra registration " +
                "(a handler registered directly in the container is not checked by AddHecoleMediator or AddRequestHandler).");
        }
        else if (handlers.Count == 0 && isService?.IsService(serviceType) != true)
        {
            problems.Add(expectedHandler is null
                ? $"The request '{requestType.FullName}' has no handler. Register one with services.AddRequestHandler, an assembly scan " +
                  $"(cfg.RegisterServicesFromAssembly) or as {Name(serviceType)} in the container."
                : $"The handler '{expectedHandler.FullName}' of the request '{requestType.FullName}' was registered by Hecole.Mediator " +
                  "but is no longer in the container.");
        }
    }

    private static IEnumerable<ServiceDescriptor> Registrations(Type serviceType, IEnumerable<ServiceDescriptor> services)
        => services.Where(d => !d.IsKeyedService && d.ServiceType == serviceType);

    private static Type? ImplementationType(ServiceDescriptor descriptor)
        => descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

    private static string Describe(ServiceDescriptor descriptor)
        => ImplementationType(descriptor)?.FullName ?? "a factory registration";

    private static List<Type> RequestInterfaces(Type type)
        => type.GetInterfaces()
            .Where(static i => i == typeof(IRequest) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToList();

    private static string Name(Type type)
    {
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        var definition = type.GetGenericTypeDefinition().FullName!;
        return $"{definition[..definition.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
