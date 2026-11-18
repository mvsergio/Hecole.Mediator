using System.Reflection;
using Hecole.Mediator.Internal;
using Hecole.Mediator.NotificationPublishers;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator;

/// <summary>
/// Options of <c>AddHecoleMediator</c>: lifetime of the mediator, assemblies to scan for handlers and pipeline
/// behaviors to add to the container.
/// </summary>
public sealed class HecoleMediatorConfiguration
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<ServiceDescriptor> _behaviors = [];
    private readonly INotificationPublisher _defaultPublisher;

    /// <summary>Creates a configuration with the defaults described on each property.</summary>
    public HecoleMediatorConfiguration()
    {
        _defaultPublisher = new ForeachAwaitPublisher();
        NotificationPublisher = _defaultPublisher;
    }

    /// <summary>
    /// Lifetime of <see cref="IMediator"/>, <see cref="ISender"/> and <see cref="IPublisher"/>.
    /// Defaults to <see cref="ServiceLifetime.Transient"/>.
    /// When <c>AddHecoleMediator</c> is called more than once, every call must use the same value.
    /// </summary>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Optional guard, off by default. When <see langword="true"/>, <c>Send</c> throws
    /// <see cref="InvalidOperationException"/> if the mediator was resolved from the root <see cref="IServiceProvider"/>
    /// instead of a scope, because handlers resolved there capture scoped services (such as a <c>DbContext</c>) and
    /// disposable transients for the lifetime of the application.
    /// Cannot be combined with <see cref="Lifetime"/> = <see cref="ServiceLifetime.Singleton"/>.
    /// When <c>AddHecoleMediator</c> is called more than once, every call must use the same value.
    /// </summary>
    public bool RequireScope { get; set; }

    /// <summary>
    /// Publisher instance that runs the handlers of each notification, registered as a singleton
    /// <see cref="INotificationPublisher"/>. Defaults to <see cref="ForeachAwaitPublisher"/> (one handler at a time, in
    /// registration order). Ignored when <see cref="NotificationPublisherType"/> is set.
    /// When <c>AddHecoleMediator</c> is called more than once, every call must choose the same publisher.
    /// An <see cref="INotificationPublisher"/> registered directly in the container before the first
    /// <c>AddHecoleMediator</c> call is kept only when this configuration leaves the publisher at its default; if the
    /// configuration chooses a publisher (assigns this property or sets <see cref="NotificationPublisherType"/>),
    /// <c>AddHecoleMediator</c> throws <see cref="InvalidOperationException"/> instead of ignoring the choice.
    /// </summary>
    public INotificationPublisher NotificationPublisher { get; set; }

    /// <summary>
    /// Publisher type, registered as <see cref="INotificationPublisher"/> with <see cref="Lifetime"/>, so it can take
    /// dependencies from the container. Wins over <see cref="NotificationPublisher"/> when both are set.
    /// When <c>AddHecoleMediator</c> is called more than once, every call must choose the same publisher.
    /// Setting it on the first <c>AddHecoleMediator</c> call while an <see cref="INotificationPublisher"/> is already
    /// registered directly in the container throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public Type? NotificationPublisherType { get; set; }

    /// <summary>
    /// <see langword="true"/> when the publisher was chosen explicitly: <see cref="NotificationPublisherType"/> is set,
    /// or <see cref="NotificationPublisher"/> no longer holds the default instance created with this configuration.
    /// </summary>
    internal bool PublisherChosenExplicitly
        => NotificationPublisherType is not null || !ReferenceEquals(NotificationPublisher, _defaultPublisher);

    /// <summary>Assemblies to scan, in the order they were added, without repetition.</summary>
    internal IReadOnlyList<Assembly> AssembliesToRegister => _assemblies;

    /// <summary>Behavior registrations, in the order they were added.</summary>
    internal IReadOnlyList<ServiceDescriptor> BehaviorsToRegister => _behaviors;

    /// <summary>
    /// Scans the assembly that contains <typeparamref name="T"/>. See <see cref="RegisterServicesFromAssembly(Assembly)"/>.
    /// </summary>
    /// <typeparam name="T">Any type of the assembly to scan.</typeparam>
    /// <returns>This configuration.</returns>
    public HecoleMediatorConfiguration RegisterServicesFromAssemblyContaining<T>()
        => RegisterServicesFromAssemblyContaining(typeof(T));

    /// <summary>
    /// Scans the assembly that contains <paramref name="type"/>. See <see cref="RegisterServicesFromAssembly(Assembly)"/>.
    /// </summary>
    /// <param name="type">Any type of the assembly to scan.</param>
    /// <returns>This configuration.</returns>
    public HecoleMediatorConfiguration RegisterServicesFromAssemblyContaining(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return RegisterServicesFromAssembly(type.Assembly);
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> and registers, as transient, every concrete non-generic implementation of
    /// <see cref="IRequestHandler{TRequest, TResponse}"/>, <see cref="IRequestHandler{TRequest}"/> and
    /// <see cref="INotificationHandler{TNotification}"/> it defines (public or not). Pipeline behaviors are not scanned;
    /// add them with <c>AddBehavior</c> or <c>AddOpenBehavior</c>. Only the assemblies given are scanned.
    /// Scanning the same assembly again, here or in another <c>AddHecoleMediator</c> call, registers nothing new.
    /// Two different handlers for the same request throw when <c>AddHecoleMediator</c> runs, and so does a handler whose
    /// request type is an interface or an abstract class (it could never be sent: dispatch is by exact runtime type) or
    /// whose response type differs from the one its request declares (<see cref="ArgumentException"/>).
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>This configuration.</returns>
    public HecoleMediatorConfiguration RegisterServicesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        if (!_assemblies.Contains(assembly))
            _assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    /// Scans each of <paramref name="assemblies"/>. See <see cref="RegisterServicesFromAssembly(Assembly)"/>.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>This configuration.</returns>
    public HecoleMediatorConfiguration RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies)
            RegisterServicesFromAssembly(assembly);
        return this;
    }

    /// <summary>
    /// Adds <typeparamref name="TImplementationType"/> to the container as <typeparamref name="TServiceType"/>,
    /// a closed <see cref="IPipelineBehavior{TRequest, TResponse}"/>.
    /// See <see cref="AddBehavior(Type, Type, ServiceLifetime)"/>.
    /// </summary>
    /// <typeparam name="TServiceType">A closed <see cref="IPipelineBehavior{TRequest, TResponse}"/>.</typeparam>
    /// <typeparam name="TImplementationType">The behavior type.</typeparam>
    /// <param name="serviceLifetime">Lifetime of the behavior.</param>
    /// <returns>This configuration.</returns>
    public HecoleMediatorConfiguration AddBehavior<TServiceType, TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddBehavior(typeof(TServiceType), typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Adds <typeparamref name="TImplementationType"/> to the container once for each closed
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/> it implements.
    /// See <see cref="AddBehavior(Type, ServiceLifetime)"/>.
    /// </summary>
    /// <typeparam name="TImplementationType">The behavior type.</typeparam>
    /// <param name="serviceLifetime">Lifetime of the behavior.</param>
    /// <returns>This configuration.</returns>
    public HecoleMediatorConfiguration AddBehavior<TImplementationType>(ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
        => AddBehavior(typeof(TImplementationType), serviceLifetime);

    /// <summary>
    /// Adds <paramref name="implementationType"/> to the container once for each closed
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/> it implements. Behaviors run in registration order: the
    /// first registered is the outermost. Behaviors registered directly in the container
    /// (<c>services.AddTransient(typeof(IPipelineBehavior&lt;,&gt;), ...)</c>) also run, in the same order.
    /// </summary>
    /// <param name="implementationType">A concrete, non-generic behavior type.</param>
    /// <param name="serviceLifetime">Lifetime of the behavior.</param>
    /// <returns>This configuration.</returns>
    /// <exception cref="InvalidOperationException">The type is open generic, not concrete, or implements no
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/>; or one of the closed behavior interfaces it implements could
    /// never run (see <see cref="AddBehavior(Type, Type, ServiceLifetime)"/>).</exception>
    public HecoleMediatorConfiguration AddBehavior(Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        BehaviorTypes.EnsureClosedImplementation(implementationType);

        var serviceTypes = BehaviorTypes.ClosedBehaviorInterfaces(implementationType);
        if (serviceTypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"'{implementationType.FullName}' does not implement {BehaviorTypes.Name}.");
        }

        foreach (var serviceType in serviceTypes)
            BehaviorTypes.EnsureRunnable(serviceType, implementationType);

        foreach (var serviceType in serviceTypes)
            _behaviors.Add(new ServiceDescriptor(serviceType, implementationType, BehaviorTypes.Lifetime(serviceLifetime)));
        return this;
    }

    /// <summary>
    /// Adds <paramref name="implementationType"/> to the container as <paramref name="serviceType"/>. The service type
    /// is a closed <see cref="IPipelineBehavior{TRequest, TResponse}"/> implemented by the behavior, or the open
    /// <c>IPipelineBehavior&lt;,&gt;</c> with an open generic behavior (as in <see cref="AddOpenBehavior"/>).
    /// A closed behavior runs only for its exact request type, so its <c>TRequest</c> must be a concrete request type
    /// (not an interface or an abstract class) that implements exactly one request interface, and its
    /// <c>TResponse</c> must be the response that request declares (<see cref="Unit"/> for <see cref="IRequest"/>).
    /// To run a behavior for a family of requests, register an open behavior with a constraint.
    /// </summary>
    /// <param name="serviceType">The behavior interface.</param>
    /// <param name="implementationType">The behavior type.</param>
    /// <param name="serviceLifetime">Lifetime of the behavior.</param>
    /// <returns>This configuration.</returns>
    /// <exception cref="InvalidOperationException">The types do not form a valid behavior registration, or the closed
    /// behavior could never run.</exception>
    public HecoleMediatorConfiguration AddBehavior(Type serviceType, Type implementationType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);

        if (serviceType == typeof(IPipelineBehavior<,>))
            return AddOpenBehavior(implementationType, serviceLifetime);

        if (!serviceType.IsGenericType || serviceType.ContainsGenericParameters
            || serviceType.GetGenericTypeDefinition() != typeof(IPipelineBehavior<,>))
        {
            throw new InvalidOperationException(
                $"'{serviceType.FullName ?? serviceType.Name}' is not a closed {BehaviorTypes.Name}.");
        }

        BehaviorTypes.EnsureClosedImplementation(implementationType);
        if (!serviceType.IsAssignableFrom(implementationType))
        {
            throw new InvalidOperationException(
                $"'{implementationType.FullName}' does not implement '{serviceType.FullName}'.");
        }

        BehaviorTypes.EnsureRunnable(serviceType, implementationType);
        _behaviors.Add(new ServiceDescriptor(serviceType, implementationType, BehaviorTypes.Lifetime(serviceLifetime)));
        return this;
    }

    /// <summary>
    /// Adds the open generic behavior <paramref name="openBehaviorType"/> (for example <c>typeof(LoggingBehavior&lt;,&gt;)</c>)
    /// to the container as <c>IPipelineBehavior&lt;,&gt;</c>, so it runs for every request whose types satisfy its
    /// constraints; for a request that does not satisfy them it is skipped. Behaviors run in registration order: the
    /// first registered is the outermost.
    /// </summary>
    /// <param name="openBehaviorType">A generic type definition with exactly two type parameters that implements
    /// <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c> with them, in that order.</param>
    /// <param name="serviceLifetime">Lifetime of the behavior.</param>
    /// <returns>This configuration.</returns>
    /// <exception cref="InvalidOperationException">The type is not an open generic behavior whose type parameters map
    /// to <c>TRequest</c> and <c>TResponse</c>.</exception>
    public HecoleMediatorConfiguration AddOpenBehavior(Type openBehaviorType, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);
        BehaviorTypes.EnsureOpenImplementation(openBehaviorType);

        _behaviors.Add(new ServiceDescriptor(typeof(IPipelineBehavior<,>), openBehaviorType, BehaviorTypes.Lifetime(serviceLifetime)));
        return this;
    }

    /// <summary>
    /// Adds each of <paramref name="openBehaviorTypes"/>, in order. See <see cref="AddOpenBehavior"/>.
    /// </summary>
    /// <param name="openBehaviorTypes">Open generic behavior types.</param>
    /// <param name="serviceLifetime">Lifetime of the behaviors.</param>
    /// <returns>This configuration.</returns>
    public HecoleMediatorConfiguration AddOpenBehaviors(IEnumerable<Type> openBehaviorTypes, ServiceLifetime serviceLifetime = ServiceLifetime.Transient)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorTypes);
        foreach (var openBehaviorType in openBehaviorTypes)
            AddOpenBehavior(openBehaviorType, serviceLifetime);
        return this;
    }
}

/// <summary>Validation of behavior types given to <see cref="HecoleMediatorConfiguration"/>.</summary>
internal static class BehaviorTypes
{
    public const string Name = "IPipelineBehavior<TRequest, TResponse>";

    public static ServiceLifetime Lifetime(ServiceLifetime serviceLifetime)
        => Enum.IsDefined(serviceLifetime)
            ? serviceLifetime
            : throw new ArgumentOutOfRangeException(nameof(serviceLifetime), serviceLifetime, $"Unknown {nameof(ServiceLifetime)}.");

    public static List<Type> ClosedBehaviorInterfaces(Type type)
        => type.GetInterfaces()
            .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
            .ToList();

    public static void EnsureClosedImplementation(Type type)
    {
        if (type.ContainsGenericParameters)
        {
            throw new InvalidOperationException(
                $"'{type.FullName ?? type.Name}' is an open generic type; register it with AddOpenBehavior.");
        }

        EnsureConcrete(type);
    }

    /// <summary>
    /// Refuses a closed behavior interface that the pipeline would never resolve: behaviors are resolved as
    /// <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c> with the exact request type and the response it declares.
    /// </summary>
    public static void EnsureRunnable(Type serviceType, Type implementationType)
    {
        var arguments = serviceType.GetGenericArguments();
        var requestType = arguments[0];
        var responseType = arguments[1];

        if (requestType.IsInterface || requestType.IsAbstract)
        {
            throw new InvalidOperationException(
                $"'{implementationType.FullName}' is registered as '{Display(serviceType)}', whose request type is " +
                $"{(requestType.IsInterface ? "an interface" : "an abstract class")}. Requests are dispatched by exact runtime type, " +
                "so this behavior would never run; register an open behavior with a constraint (AddOpenBehavior) instead.");
        }

        Type? declared;
        try
        {
            declared = RequestTypes.GetResponseType(requestType);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"'{implementationType.FullName}' is registered as '{Display(serviceType)}', whose request type is not a valid " +
                $"request, so this behavior would never run. {ex.Message}",
                ex);
        }

        var expected = declared ?? typeof(Unit);
        if (responseType != expected)
        {
            throw new InvalidOperationException(
                $"'{implementationType.FullName}' is registered as '{Display(serviceType)}', but the request " +
                $"'{requestType.FullName}' declares the response '{expected.FullName}', so this behavior would never run. " +
                $"Use IPipelineBehavior<{requestType.Name}, {expected.Name}>.");
        }
    }

    private static string Display(Type type)
        => type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>"
            : type.FullName ?? type.Name;

    public static void EnsureOpenImplementation(Type type)
    {
        if (!type.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' must be an open generic type definition such as typeof(MyBehavior<,>); " +
                "register a closed behavior with AddBehavior.");
        }

        EnsureConcrete(type);

        var behaviorInterfaces = ClosedBehaviorInterfaces(type);
        if (behaviorInterfaces.Count == 0)
            throw new InvalidOperationException($"'{type.FullName}' does not implement {Name}.");

        // The container closes the behavior by position: IPipelineBehavior<A, B> becomes Behavior<A, B>. That only works
        // when the behavior has exactly two type parameters and implements IPipelineBehavior<TRequest, TResponse> with them,
        // in that order. Inferring other shapes is not supported.
        var parameters = type.GetGenericArguments();
        var maps = parameters.Length == 2 && behaviorInterfaces.Any(i =>
        {
            var arguments = i.GetGenericArguments();
            return arguments[0] == parameters[0] && arguments[1] == parameters[1];
        });

        if (!maps)
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' has {parameters.Length} type parameter(s) that do not map, in order, to {Name}. " +
                $"An open behavior must be declared as Behavior<TRequest, TResponse> : {Name}; register other shapes " +
                "closed, with AddBehavior.");
        }
    }

    private static void EnsureConcrete(Type type)
    {
        if (type.IsAbstract || type.IsInterface || !type.IsClass)
            throw new InvalidOperationException($"'{type.FullName ?? type.Name}' must be a concrete class.");
    }
}
