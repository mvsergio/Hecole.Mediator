using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Internal;

/// <summary>
/// Checks behind <c>VerifyHecoleMediator</c>. Reads the descriptors kept by <see cref="HandlerRegistry"/> (the
/// <see cref="IServiceCollection"/> the container was built from) and asks <see cref="IServiceProviderIsService"/>;
/// never resolves a handler, behavior or publisher.
/// </summary>
internal static class MediatorVerification
{
    public static List<string> FindProblems(IServiceProvider provider, IReadOnlyList<Assembly> requestAssemblies)
    {
        var problems = new List<string>();

        if (provider.GetService<HandlerRegistry>() is not { Mediator: not null } registry)
        {
            problems.Add("Hecole.Mediator is not registered in this container: call services.AddHecoleMediator(...) before building it.");
            return problems;
        }

        var isService = provider.GetService<IServiceProviderIsService>();
        var services = registry.Services;

        // (a) Requests known at registration: exactly one handler in the container.
        var known = new HashSet<Type>();
        foreach (var registration in registry.Registrations)
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

        // Notifications: the publisher and the handlers registered by scan or AddNotificationHandler.
        if (!IsRegistered(typeof(INotificationPublisher), services, isService))
        {
            problems.Add(
                $"No {nameof(INotificationPublisher)} is registered; Publish would fail. It is registered by AddHecoleMediator " +
                "and must not be removed.");
        }

        foreach (var registration in registry.NotificationRegistrations)
        {
            var serviceType = typeof(INotificationHandler<>).MakeGenericType(registration.NotificationType);
            if (!Registrations(serviceType, services).Any(d => ImplementationType(d) == registration.HandlerType))
            {
                problems.Add(
                    $"The handler '{registration.HandlerType.FullName}' of the notification '{registration.NotificationType.FullName}' " +
                    "was registered by Hecole.Mediator but is no longer in the container.");
            }
        }

        return problems;
    }

    /// <summary>
    /// For each request known at registration, the open behaviors registered as <c>IPipelineBehavior&lt;,&gt;</c> whose
    /// constraints do not fit it, so the container skips them for that request. Allowed; not a problem by default.
    /// </summary>
    public static List<(Type RequestType, Type BehaviorType)> FindSkippedOpenBehaviors(IServiceProvider provider)
    {
        var skipped = new List<(Type, Type)>();
        if (provider.GetService<HandlerRegistry>() is not { } registry)
            return skipped;

        var openBehaviors = Registrations(typeof(IPipelineBehavior<,>), registry.Services)
            .Select(static d => d.ImplementationType)
            .OfType<Type>()
            .Where(static t => t.IsGenericTypeDefinition)
            .Distinct()
            .ToList();

        foreach (var registration in registry.Registrations)
        {
            foreach (var behavior in openBehaviors)
            {
                if (!CanClose(behavior, registration.RequestType, registration.ResponseType))
                    skipped.Add((registration.RequestType, behavior));
            }
        }

        return skipped;
    }

    private static void CheckSingleHandler(
        Type requestType, Type serviceType, Type? expectedHandler, IServiceCollection services, IServiceProviderIsService? isService, List<string> problems)
    {
        var handlers = Registrations(serviceType, services).ToList();
        if (handlers.Count > 1)
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

    private static bool IsRegistered(Type serviceType, IServiceCollection services, IServiceProviderIsService? isService)
        => isService?.IsService(serviceType) ?? Registrations(serviceType, services).Any();

    private static IEnumerable<ServiceDescriptor> Registrations(Type serviceType, IServiceCollection services)
        => services.Where(d => !d.IsKeyedService && d.ServiceType == serviceType);

    private static Type? ImplementationType(ServiceDescriptor descriptor)
        => descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();

    private static string Describe(ServiceDescriptor descriptor)
        => ImplementationType(descriptor)?.FullName ?? "a factory registration";

    private static List<Type> RequestInterfaces(Type type)
        => type.GetInterfaces()
            .Where(static i => i == typeof(IRequest) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .ToList();

    private static bool CanClose(Type openBehavior, Type requestType, Type responseType)
    {
        try
        {
            openBehavior.MakeGenericType(requestType, responseType);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

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
