using System.Reflection;
using Hecole.Mediator.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hecole.Mediator.Internal;

/// <summary>
/// One registered request handler. <paramref name="ServiceType"/> is the handler interface registered in the container
/// (<see cref="IRequestHandler{TRequest, TResponse}"/> or <see cref="IRequestHandler{TRequest}"/>);
/// <paramref name="ResponseType"/> is <see cref="Unit"/> for a request without response value.
/// </summary>
internal sealed record HandlerRegistration(Type RequestType, Type ResponseType, Type HandlerType, Type ServiceType, RequestExecutor Executor);

/// <summary>One notification handler registered by <c>AddNotificationHandler</c> or by assembly scan.</summary>
internal sealed record NotificationRegistration(Type NotificationType, Type HandlerType);

/// <summary>Options of the first <c>AddHecoleMediator</c> call, which every later call must repeat.</summary>
internal sealed record MediatorOptions(ServiceLifetime Lifetime, bool RequireScope, PublisherChoice Publisher);

/// <summary>
/// The notification publisher chosen in <see cref="HecoleMediatorConfiguration"/>: a type (registered with the mediator's
/// lifetime) or an instance (singleton). Two choices are the same when they name the same type, are the same instance,
/// or are both instances of the same built-in publisher (stateless).
/// </summary>
internal sealed class PublisherChoice
{
    private PublisherChoice(Type? type, INotificationPublisher? instance)
    {
        Type = type;
        Instance = instance;
    }

    public Type? Type { get; }

    public INotificationPublisher? Instance { get; }

    /// <summary>
    /// Reads the choice of <paramref name="configuration"/>; a type wins over an instance.
    /// </summary>
    public static PublisherChoice From(HecoleMediatorConfiguration configuration)
    {
        if (configuration.NotificationPublisherType is { } type)
        {
            if (type.IsAbstract || type.IsInterface || !type.IsClass || type.ContainsGenericParameters
                || !typeof(INotificationPublisher).IsAssignableFrom(type))
            {
                throw new ArgumentException(
                    $"{nameof(HecoleMediatorConfiguration.NotificationPublisherType)} '{type.FullName ?? type.Name}' must be a concrete, " +
                    $"non-generic class that implements {nameof(INotificationPublisher)}.",
                    "configure");
            }

            return new PublisherChoice(type, null);
        }

        return new PublisherChoice(null, configuration.NotificationPublisher ?? throw new ArgumentException(
            $"{nameof(HecoleMediatorConfiguration.NotificationPublisher)} is null; set an instance or " +
            $"{nameof(HecoleMediatorConfiguration.NotificationPublisherType)}.",
            "configure"));
    }

    public ServiceDescriptor ToDescriptor(ServiceLifetime lifetime)
        => Type is not null
            ? new ServiceDescriptor(typeof(INotificationPublisher), Type, lifetime)
            : new ServiceDescriptor(typeof(INotificationPublisher), Instance!);

    public bool SameAs(PublisherChoice other)
    {
        if (Type is not null || other.Type is not null)
            return Type == other.Type;

        if (ReferenceEquals(Instance, other.Instance))
            return true;

        var type = Instance!.GetType();
        return type == other.Instance!.GetType()
            && (type == typeof(ForeachAwaitPublisher) || type == typeof(TaskWhenAllPublisher));
    }

    public override string ToString()
        => Type is not null
            ? $"{nameof(HecoleMediatorConfiguration.NotificationPublisherType)} = {Type.FullName}"
            : $"{nameof(HecoleMediatorConfiguration.NotificationPublisher)} = an instance of {Instance!.GetType().FullName}";
}

/// <summary>
/// Handlers registered in one <see cref="IServiceCollection"/> (by <c>AddRequestHandler</c>, <c>AddNotificationHandler</c>
/// or by assembly scan), kept in the collection itself as a singleton instance. Used while the container is being
/// configured and by <c>VerifyHecoleMediator</c>; <see cref="RequestCatalog"/> and <see cref="NotificationCatalog"/> take
/// immutable copies.
/// </summary>
internal sealed class HandlerRegistry
{
    private readonly Dictionary<Type, HandlerRegistration> _byRequest = [];
    private readonly List<HandlerRegistration> _registrations = [];
    private readonly HashSet<NotificationRegistration> _notificationHandlers = [];
    private readonly List<NotificationRegistration> _notificationRegistrations = [];
    private readonly Dictionary<Type, NotificationExecutor> _notificationExecutors = [];
    private readonly HashSet<Assembly> _scanned = [];

    private HandlerRegistry(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// The collection this registry lives in. Read by <c>VerifyHecoleMediator</c> to count the registrations of a
    /// service without resolving it.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>Options of the first <c>AddHecoleMediator</c> call; <see langword="null"/> before it.</summary>
    public MediatorOptions? Mediator { get; set; }

    /// <summary>Request handlers, in registration order.</summary>
    public IReadOnlyList<HandlerRegistration> Registrations => _registrations;

    /// <summary>Notification handlers, in registration order, without repetition.</summary>
    public IReadOnlyList<NotificationRegistration> NotificationRegistrations => _notificationRegistrations;

    /// <summary>Executors of the notification types known at registration.</summary>
    public IEnumerable<NotificationExecutor> NotificationExecutors => _notificationExecutors.Values;

    public static HandlerRegistry GetOrAdd(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(HandlerRegistry)
                && !descriptor.IsKeyedService
                && descriptor.ImplementationInstance is HandlerRegistry existing)
            {
                return existing;
            }
        }

        var registry = new HandlerRegistry(services);
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>
    /// Adds the handler of a request. Returns <see langword="false"/> when the same handler is already registered for
    /// that request (nothing to add); throws when a different handler is.
    /// </summary>
    public bool Add(HandlerRegistration registration)
    {
        if (_byRequest.TryGetValue(registration.RequestType, out var existing))
        {
            if (existing.HandlerType == registration.HandlerType && existing.ResponseType == registration.ResponseType)
                return false;

            throw new InvalidOperationException(
                $"The request '{registration.RequestType.FullName}' already has the handler '{existing.HandlerType.FullName}'; " +
                $"cannot register '{registration.HandlerType.FullName}'. A request has exactly one handler.");
        }

        RequestTypes.GetResponseType(registration.RequestType);
        _byRequest.Add(registration.RequestType, registration);
        _registrations.Add(registration);
        return true;
    }

    /// <summary>
    /// Records a notification handler and the executor of its notification type. Returns <see langword="false"/> when
    /// the same handler is already recorded for that notification.
    /// </summary>
    public bool AddNotification(Type notificationType, Type handlerType, NotificationExecutor executor)
    {
        var registration = new NotificationRegistration(notificationType, handlerType);
        if (!_notificationHandlers.Add(registration))
            return false;

        _notificationRegistrations.Add(registration);
        _notificationExecutors.TryAdd(notificationType, executor);
        return true;
    }

    /// <summary>
    /// Registers the handlers defined in <paramref name="assembly"/>, once per assembly per collection: request handlers
    /// enter this registry (executor created here, the only reflection) and the container; notification handlers are
    /// recorded here (executor of the notification type taken here) and enter the container unless the same handler is
    /// already there for the same notification.
    /// </summary>
    public void Scan(IServiceCollection services, Assembly assembly)
    {
        if (!_scanned.Add(assembly))
            return;

        foreach (var type in LoadableTypes(assembly))
        {
            if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
                continue;

            foreach (var contract in Interfaces(type))
            {
                if (!contract.IsGenericType)
                    continue;

                var definition = contract.GetGenericTypeDefinition();
                if (definition == typeof(IRequestHandler<,>))
                {
                    var arguments = contract.GetGenericArguments();
                    var executor = (RequestExecutor)Activator.CreateInstance(typeof(Executor<,>).MakeGenericType(arguments))!;
                    if (Add(new HandlerRegistration(arguments[0], arguments[1], type, contract, executor)))
                        services.AddTransient(contract, type);
                }
                else if (definition == typeof(IRequestHandler<>))
                {
                    var requestType = contract.GetGenericArguments()[0];
                    var executor = (RequestExecutor)Activator.CreateInstance(typeof(VoidExecutor<>).MakeGenericType(requestType))!;
                    if (Add(new HandlerRegistration(requestType, typeof(Unit), type, contract, executor)))
                        services.AddTransient(contract, type);
                }
                else if (definition == typeof(INotificationHandler<>))
                {
                    var notificationType = contract.GetGenericArguments()[0];
                    if (!_notificationExecutors.TryGetValue(notificationType, out var notificationExecutor))
                        notificationExecutor = NotificationExecutor.For(notificationType);

                    AddNotification(notificationType, type, notificationExecutor);
                    services.TryAddEnumerable(ServiceDescriptor.Transient(contract, type));
                }
            }
        }
    }

    private static Type[] Interfaces(Type type)
    {
        try
        {
            return type.GetInterfaces();
        }
        catch (Exception ex) when (ex is TypeLoadException or FileNotFoundException or FileLoadException)
        {
            return [];
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Types whose dependencies cannot be loaded come back as null; the others are still scanned.
            return ex.Types.OfType<Type>();
        }
    }
}
