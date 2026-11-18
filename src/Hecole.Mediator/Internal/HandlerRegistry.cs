using System.Reflection;
using System.Runtime.CompilerServices;
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
/// or by assembly scan), and the descriptors <c>AddHecoleMediator</c> registered for the library's own services. Associated
/// with the collection instance outside the collection (so removing its descriptor does not detach it) and also registered
/// in the collection as a singleton instance (so the provider reaches it). Used while the container is being configured
/// and by <c>VerifyHecoleMediator</c>; <see cref="MediatorRuntime"/>, <see cref="RequestCatalog"/> and
/// <see cref="NotificationCatalog"/> take immutable copies.
/// </summary>
internal sealed class HandlerRegistry
{
    // The registry of each collection instance, out of reach of a change to the collection's descriptors.
    private static readonly ConditionalWeakTable<IServiceCollection, HandlerRegistry> ByCollection = new();

    private readonly Dictionary<Type, HandlerRegistration> _byRequest = [];
    private readonly List<HandlerRegistration> _registrations = [];
    private readonly HashSet<NotificationRegistration> _notificationHandlers = [];
    private readonly List<NotificationRegistration> _notificationRegistrations = [];
    private readonly Dictionary<Type, NotificationExecutor> _notificationExecutors = [];
    private readonly HashSet<Assembly> _scanned = [];
    private readonly List<ServiceDescriptor> _ownDescriptors = [];

    private HandlerRegistry(IServiceCollection services)
    {
        Services = services;
        Descriptor = ServiceDescriptor.Singleton(this);
        _ownDescriptors.Add(Descriptor);
    }

    /// <summary>The descriptor that registers this registry in the collection, so the provider can reach it.</summary>
    public ServiceDescriptor Descriptor { get; }

    /// <summary>
    /// The descriptors of the library's own services, each the registration that must be in effect for its service type:
    /// this registry's <see cref="Descriptor"/> and, once <c>AddHecoleMediator</c> has run, those of
    /// <see cref="MediatorRuntime"/>, <see cref="IRequestCatalog"/>, <see cref="IMediator"/>, <see cref="ISender"/>,
    /// <see cref="IPublisher"/> and <see cref="INotificationPublisher"/> (the one it added or the one it kept).
    /// </summary>
    public IReadOnlyList<ServiceDescriptor> OwnDescriptors => _ownDescriptors;

    /// <summary>Records a descriptor of the library's own services (see <see cref="OwnDescriptors"/>).</summary>
    public void RecordOwn(ServiceDescriptor descriptor) => _ownDescriptors.Add(descriptor);

    /// <summary>
    /// The collection this registry was created for. <see cref="MediatorRuntime"/> copies its descriptors when it is
    /// created; <c>VerifyHecoleMediator</c> reads that copy.
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

    /// <summary>
    /// The registry of <paramref name="services"/>: the one associated with this collection instance; else, for a
    /// collection whose descriptors were copied from another one, the registry found among its descriptors (then
    /// associated with this collection too); else a new one, registered in the collection.
    /// </summary>
    public static HandlerRegistry GetOrAdd(IServiceCollection services)
    {
        if (ByCollection.TryGetValue(services, out var associated))
            return associated;

        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(HandlerRegistry)
                && !descriptor.IsKeyedService
                && descriptor.ImplementationInstance is HandlerRegistry existing)
            {
                ByCollection.AddOrUpdate(services, existing);
                return existing;
            }
        }

        var registry = new HandlerRegistry(services);
        services.Add(registry.Descriptor);
        ByCollection.AddOrUpdate(services, registry);
        return registry;
    }

    /// <summary>
    /// Puts <see cref="Descriptor"/> back in <paramref name="services"/> when it was removed, so the provider can reach
    /// this registry again.
    /// </summary>
    public void EnsureRegistered(IServiceCollection services)
    {
        if (!services.Contains(Descriptor))
            services.Add(Descriptor);
    }

    /// <summary>
    /// Adds the handler of a request. Returns <see langword="false"/> when the same handler is already registered for
    /// that request (nothing to add); throws <see cref="InvalidOperationException"/> when a different handler is, and
    /// <see cref="ArgumentException"/> when the request type can never be dispatched to this handler (see
    /// <see cref="EnsureDispatchable"/>).
    /// </summary>
    public bool Add(HandlerRegistration registration)
    {
        if (!IsNew(registration, _byRequest))
            return false;

        _byRequest.Add(registration.RequestType, registration);
        _registrations.Add(registration);
        return true;
    }

    /// <summary>
    /// Validates <paramref name="registration"/> against <paramref name="known"/> without changing anything: returns
    /// <see langword="false"/> when the same handler is already there, throws as <see cref="Add"/> does otherwise.
    /// </summary>
    private static bool IsNew(HandlerRegistration registration, IReadOnlyDictionary<Type, HandlerRegistration> known)
    {
        EnsureDispatchable(registration);

        if (known.TryGetValue(registration.RequestType, out var existing))
        {
            if (existing.HandlerType == registration.HandlerType && existing.ResponseType == registration.ResponseType)
                return false;

            throw new InvalidOperationException(
                $"The request '{registration.RequestType.FullName}' already has the handler '{existing.HandlerType.FullName}'; " +
                $"cannot register '{registration.HandlerType.FullName}'. A request has exactly one handler.");
        }

        return true;
    }

    /// <summary>
    /// Dispatch is by exact runtime type, so a registration is refused when the request type is an interface or an
    /// abstract class (no instance can have it as runtime type), when it does not implement exactly one request
    /// interface, or when the handler's response type differs from the response the request declares
    /// (<see cref="IRequest{TResponse}"/> is covariant, so a handler of a wider response type compiles).
    /// </summary>
    private static void EnsureDispatchable(HandlerRegistration registration)
    {
        var requestType = registration.RequestType;
        if (requestType.IsInterface || requestType.IsAbstract)
        {
            throw new ArgumentException(
                $"The request type '{requestType.FullName}' of the handler '{registration.HandlerType.FullName}' is " +
                $"{(requestType.IsInterface ? "an interface" : "an abstract class")}. Requests are dispatched by exact runtime type, " +
                "so no request would ever reach this handler; register the handler for a concrete request type.");
        }

        var declared = RequestTypes.GetResponseType(requestType);
        var handled = registration.ServiceType.IsGenericType
                      && registration.ServiceType.GetGenericTypeDefinition() == typeof(IRequestHandler<>)
            ? null
            : registration.ResponseType;

        if (declared != handled)
        {
            throw new ArgumentException(
                $"The request '{requestType.FullName}' declares the response '{ResponseName(declared)}', but its handler " +
                $"'{registration.HandlerType.FullName}' is registered with the response '{ResponseName(handled)}'. The handler's " +
                "response type must be the one the request declares.");
        }
    }

    private static string ResponseName(Type? responseType)
        => responseType is null ? $"none ({nameof(IRequest)}, {nameof(Unit)})" : responseType.FullName ?? responseType.Name;

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
    /// recorded here (executor of the notification type taken here) and enter the container. A handler already in the
    /// container with the same service and implementation type is not added again.
    /// <para>
    /// All or nothing: every handler of the assembly is read and validated first, and the registry and
    /// <paramref name="services"/> change only when the whole assembly is valid. When it is not, the exception leaves
    /// both exactly as they were and the assembly is not marked as scanned, so it can be scanned again.
    /// </para>
    /// </summary>
    public void Scan(IServiceCollection services, Assembly assembly)
    {
        if (_scanned.Contains(assembly))
            return;

        // Validation: nothing below changes this registry or the collection.
        var requests = new List<HandlerRegistration>();
        var planned = new Dictionary<Type, HandlerRegistration>(_byRequest);
        var notifications = new List<(Type NotificationType, Type HandlerType, Type Contract, NotificationExecutor Executor)>();

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
                    Plan(new HandlerRegistration(arguments[0], arguments[1], type, contract, executor));
                }
                else if (definition == typeof(IRequestHandler<>))
                {
                    var requestType = contract.GetGenericArguments()[0];
                    var executor = (RequestExecutor)Activator.CreateInstance(typeof(VoidExecutor<>).MakeGenericType(requestType))!;
                    Plan(new HandlerRegistration(requestType, typeof(Unit), type, contract, executor));
                }
                else if (definition == typeof(INotificationHandler<>))
                {
                    var notificationType = contract.GetGenericArguments()[0];
                    if (!_notificationExecutors.TryGetValue(notificationType, out var notificationExecutor))
                        notificationExecutor = NotificationExecutor.For(notificationType);

                    notifications.Add((notificationType, type, contract, notificationExecutor));
                }
            }
        }

        // The whole assembly is valid: apply.
        foreach (var registration in requests)
        {
            _byRequest.Add(registration.RequestType, registration);
            _registrations.Add(registration);
            services.TryAddEnumerable(ServiceDescriptor.Transient(registration.ServiceType, registration.HandlerType));
        }

        foreach (var (notificationType, handlerType, contract, executor) in notifications)
        {
            AddNotification(notificationType, handlerType, executor);
            services.TryAddEnumerable(ServiceDescriptor.Transient(contract, handlerType));
        }

        _scanned.Add(assembly);

        void Plan(HandlerRegistration registration)
        {
            if (!IsNew(registration, planned))
                return;

            planned.Add(registration.RequestType, registration);
            requests.Add(registration);
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
