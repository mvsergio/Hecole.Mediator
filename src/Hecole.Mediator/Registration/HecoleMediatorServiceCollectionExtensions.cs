using System.Reflection;
using Hecole.Mediator;
using Hecole.Mediator.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the mediator, its handlers and its pipeline behaviors in an <see cref="IServiceCollection"/>, and verifies
/// the result in the built <see cref="IServiceProvider"/>.
/// </summary>
public static class HecoleMediatorServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IMediator"/>, <see cref="ISender"/> and <see cref="IPublisher"/> with
    /// <see cref="HecoleMediatorConfiguration.Lifetime"/>, the <see cref="INotificationPublisher"/>, the
    /// <see cref="IRequestCatalog"/>, the handlers of the assemblies given with <c>RegisterServicesFrom...</c> and the
    /// behaviors given with <c>AddBehavior</c>/<c>AddOpenBehavior</c>.
    /// No assembly is required: handlers can also be registered with <c>AddRequestHandler</c> and
    /// <c>AddNotificationHandler</c>, before or after this call.
    /// <para>
    /// May be called more than once (for example, once per module, each with its own assemblies). Each call adds its
    /// assemblies and behaviors; the mediator itself is registered once, and an assembly, handler or behavior that is
    /// already registered is not registered again. Every call must use the same
    /// <see cref="HecoleMediatorConfiguration.Lifetime"/>, <see cref="HecoleMediatorConfiguration.RequireScope"/> and
    /// notification publisher (the same <see cref="HecoleMediatorConfiguration.NotificationPublisherType"/>, or the same
    /// <see cref="HecoleMediatorConfiguration.NotificationPublisher"/> instance; two instances of the same built-in
    /// publisher count as the same).
    /// </para>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the mediator.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="ArgumentException">The configuration is invalid, or a scanned request handler can never be
    /// dispatched to (its request type is an interface or an abstract class, implements more than one request
    /// interface, or declares a response type other than the handler's).</exception>
    /// <exception cref="InvalidOperationException">A previous call used a different
    /// <see cref="HecoleMediatorConfiguration.Lifetime"/>, <see cref="HecoleMediatorConfiguration.RequireScope"/> or
    /// notification publisher; or, on the first call, the configuration chooses a notification publisher while an
    /// <see cref="INotificationPublisher"/> is already registered in the container; or two different handlers were found
    /// for the same request.</exception>
    public static IServiceCollection AddHecoleMediator(this IServiceCollection services, Action<HecoleMediatorConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new HecoleMediatorConfiguration();
        configure(configuration);

        var lifetime = configuration.Lifetime;
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentException($"Unknown {nameof(ServiceLifetime)} '{lifetime}'.", nameof(configure));

        var requireScope = configuration.RequireScope;
        if (lifetime == ServiceLifetime.Singleton && requireScope)
        {
            throw new ArgumentException(
                $"A Singleton mediator is always resolved from the root provider, so every Send would be refused. " +
                $"Use Transient or Scoped, or set {nameof(HecoleMediatorConfiguration.RequireScope)} = false.",
                nameof(configure));
        }

        var publisher = PublisherChoice.From(configuration);

        var registry = HandlerRegistry.GetOrAdd(services);
        if (registry.Mediator is { } first)
        {
            if (first.Lifetime != lifetime || first.RequireScope != requireScope || !first.Publisher.SameAs(publisher))
            {
                throw new InvalidOperationException(
                    $"AddHecoleMediator was already called with {nameof(HecoleMediatorConfiguration.Lifetime)} = {first.Lifetime}, " +
                    $"{nameof(HecoleMediatorConfiguration.RequireScope)} = {first.RequireScope} and {first.Publisher}; this call uses " +
                    $"{lifetime}, {requireScope} and {publisher}. Every call must use the same values (they apply to the single " +
                    "mediator registration).");
            }

            // The registry stays associated with this collection when its descriptor was removed; put it back.
            registry.EnsureRegistered(services);
        }
        else
        {
            // A publisher registered directly in the container before this call is kept when the configuration left the
            // publisher at its default; an explicit choice that would be ignored is refused.
            var existingPublisher = services.FirstOrDefault(static d => !d.IsKeyedService && d.ServiceType == typeof(INotificationPublisher));
            if (existingPublisher is not null && configuration.PublisherChosenExplicitly)
            {
                throw new InvalidOperationException(
                    $"An {nameof(INotificationPublisher)} ({DescribePublisher(existingPublisher)}) is already registered in the container, " +
                    $"and this AddHecoleMediator call chooses {publisher}. The configured publisher would be ignored; remove one of the " +
                    "two (leave the configuration at its default to keep the publisher registered in the container).");
            }

            registry.Mediator = new MediatorOptions(lifetime, requireScope, publisher);
            registry.EnsureRegistered(services);

            // Each descriptor is recorded: VerifyHecoleMediator checks that it is the registration in effect.
            Register(ServiceDescriptor.Singleton(rootProvider => new MediatorRuntime(rootProvider, registry, requireScope)));
            Register(ServiceDescriptor.Singleton<IRequestCatalog>(static sp => sp.GetRequiredService<MediatorRuntime>().Catalog));
            Register(new ServiceDescriptor(typeof(IMediator), typeof(Mediator), lifetime));
            Register(new ServiceDescriptor(typeof(ISender), static sp => sp.GetRequiredService<IMediator>(), lifetime));
            Register(new ServiceDescriptor(typeof(IPublisher), static sp => sp.GetRequiredService<IMediator>(), lifetime));

            if (existingPublisher is null)
            {
                Register(publisher.ToDescriptor(lifetime));
            }
            else
            {
                // The container returns the last registration; that is the one kept.
                registry.RecordOwn(services.Last(static d => !d.IsKeyedService && d.ServiceType == typeof(INotificationPublisher)));
            }

            void Register(ServiceDescriptor descriptor)
            {
                services.Add(descriptor);
                registry.RecordOwn(descriptor);
            }
        }

        foreach (var assembly in configuration.AssembliesToRegister)
            registry.Scan(services, assembly);

        // A behavior already in the container with the same service and implementation type is not added again.
        foreach (var behavior in configuration.BehaviorsToRegister)
            services.TryAddEnumerable(behavior);

        return services;
    }

    private static string DescribePublisher(ServiceDescriptor descriptor)
        => descriptor.ImplementationType?.FullName is { } type
            ? $"type {type}"
            : descriptor.ImplementationInstance is { } instance
                ? $"an instance of {instance.GetType().FullName}"
                : "a factory registration";

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the single handler of <typeparamref name="TRequest"/>
    /// (as <see cref="IRequestHandler{TRequest, TResponse}"/>, transient). When the container already has a registration
    /// of <see cref="IRequestHandler{TRequest, TResponse}"/> with implementation type <typeparamref name="THandler"/>
    /// (made directly with <c>services.AddTransient</c>, for example), that registration is kept and no second one is added.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TRequest"/> already has a different handler
    /// (registered here or found by an assembly scan). Registering the same handler again does nothing.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TRequest"/> implements more than one request interface,
    /// is an interface or an abstract class (requests are dispatched by exact runtime type, so it could never be sent),
    /// or declares a response type other than <typeparamref name="TResponse"/> (for example <c>IRequest&lt;string&gt;</c>
    /// registered with <c>TResponse</c> = <see cref="object"/>, which compiles because <see cref="IRequest{TResponse}"/>
    /// is covariant).</exception>
    public static IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(this IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (HandlerRegistry.GetOrAdd(services).Add(new HandlerRegistration(
                typeof(TRequest), typeof(TResponse), typeof(THandler), typeof(IRequestHandler<TRequest, TResponse>),
                new Executor<TRequest, TResponse>())))
        {
            // A registration of the same handler made directly in the container is kept, not repeated.
            services.TryAddEnumerable(ServiceDescriptor.Transient<IRequestHandler<TRequest, TResponse>, THandler>());
        }

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the single handler of <typeparamref name="TRequest"/>, a request
    /// without response value (as <see cref="IRequestHandler{TRequest}"/>, transient). When the container already has a
    /// registration of <see cref="IRequestHandler{TRequest}"/> with implementation type <typeparamref name="THandler"/>,
    /// that registration is kept and no second one is added.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TRequest"/> already has a different handler
    /// (registered here or found by an assembly scan). Registering the same handler again does nothing.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TRequest"/> implements more than one request interface,
    /// or is an interface or an abstract class (requests are dispatched by exact runtime type, so it could never be
    /// sent).</exception>
    public static IServiceCollection AddRequestHandler<TRequest, THandler>(this IServiceCollection services)
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (HandlerRegistry.GetOrAdd(services).Add(new HandlerRegistration(
                typeof(TRequest), typeof(Unit), typeof(THandler), typeof(IRequestHandler<TRequest>),
                new VoidExecutor<TRequest>())))
        {
            // A registration of the same handler made directly in the container is kept, not repeated.
            services.TryAddEnumerable(ServiceDescriptor.Transient<IRequestHandler<TRequest>, THandler>());
        }

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as a handler of <typeparamref name="TNotification"/>
    /// (as <see cref="INotificationHandler{TNotification}"/>, transient). A notification may have any number of handlers;
    /// with the default publisher they run in registration order. The same handler registered again, here or by an
    /// assembly scan, is not registered twice.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddNotificationHandler<TNotification, THandler>(this IServiceCollection services)
        where TNotification : INotification
        where THandler : class, INotificationHandler<TNotification>
    {
        ArgumentNullException.ThrowIfNull(services);

        HandlerRegistry.GetOrAdd(services).AddNotification(
            typeof(TNotification), typeof(THandler), NotificationExecutor<TNotification>.Instance);
        services.TryAddEnumerable(ServiceDescriptor.Transient<INotificationHandler<TNotification>, THandler>());

        return services;
    }

    /// <summary>
    /// Optional start-up check of the mediator configuration; runs only when called, typically right after the
    /// container is built. It reads registrations only: no handler, behavior or publisher is created. It first resolves
    /// the mediator's internal state from <paramref name="provider"/>, which takes the copy of the registrations that
    /// <see cref="IRequestCatalog"/> uses (so the first <c>Send</c> does not pay for it), and runs every check below on
    /// that copy, not on the live <see cref="IServiceCollection"/>: registrations changed afterwards do not change the
    /// result of a later call. A failure to resolve that state is reported as a problem.
    /// It checks that:
    /// <list type="bullet">
    /// <item>the registration the container uses (the last non-keyed one of the service type) of <see cref="IMediator"/>,
    /// <see cref="ISender"/>, <see cref="IPublisher"/>, <see cref="IRequestCatalog"/>, <see cref="INotificationPublisher"/>
    /// and the mediator's internal state is the one <c>AddHecoleMediator</c> made (for the publisher, the one it added
    /// or the one already registered that it kept); a replacement, another registration added after it, or its removal
    /// is reported, one problem per service. An application that replaces <see cref="IMediator"/>,
    /// <see cref="ISender"/> or <see cref="IPublisher"/> on purpose (for example with its own decorator) gets that
    /// reported. Replacing a request handler is not reported;</item>
    /// <item>every request handler checked below that is in the copy is a service of <paramref name="provider"/>, when it
    /// offers <see cref="IServiceProviderIsService"/> (a handler added to the collection after the provider was built is
    /// reported);</item>
    /// <item>every request registered by an assembly scan or <c>AddRequestHandler</c> has exactly one handler in the container
    /// (a handler registered directly in the container for a request that also has one from a scan or
    /// <c>AddRequestHandler</c> is reported, with both names);</item>
    /// <item>every concrete request type in <paramref name="requestAssemblies"/> (implementing <see cref="IRequest"/> or
    /// <see cref="IRequest{TResponse}"/>) implements only one request interface and has exactly one handler;</item>
    /// <item>the notification handlers registered by scan or
    /// <c>AddNotificationHandler</c> are in the container (a handler replaced by a decorator, that is by a factory
    /// registration of the same service type, is not reported: missing handlers of a notification are reported only
    /// when there are more of them than factory registrations of its <see cref="INotificationHandler{TNotification}"/>);</item>
    /// <item>no notification handler class is registered twice in the container for the same notification (two
    /// registrations with the same implementation type run the handler twice);</item>
    /// <item>no open generic request handler is registered (<c>IRequestHandler&lt;,&gt;</c> or
    /// <c>IRequestHandler&lt;&gt;</c> as an open service type): not supported, and the container would count it as a
    /// handler of every request;</item>
    /// <item>no <c>IEnumerable&lt;X&gt;</c> is registered as a service, where X is a closed
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/>, <see cref="INotificationHandler{TNotification}"/>,
    /// <see cref="IRequestHandler{TRequest, TResponse}"/> or <see cref="IRequestHandler{TRequest}"/>: such a registration
    /// replaces the list the container builds from the registrations of X.</item>
    /// </list>
    /// An open behavior whose constraints do not fit a request is skipped for that request; that is allowed and is not
    /// reported (<see cref="RequestDescriptor.SkippedBehaviors"/> in <see cref="IRequestCatalog"/> lists it).
    /// </summary>
    /// <param name="provider">The built service provider.</param>
    /// <param name="requestAssemblies">Assemblies whose request types must all have a handler; none to check only the
    /// registered requests.</param>
    /// <exception cref="MediatorConfigurationException">At least one problem was found; the message lists all of them.</exception>
    public static void VerifyHecoleMediator(this IServiceProvider provider, params Assembly[] requestAssemblies)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(requestAssemblies);

        var problems = MediatorVerification.FindProblems(provider, requestAssemblies);
        if (problems.Count > 0)
            throw new MediatorConfigurationException(problems);
    }
}
