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
    /// <exception cref="ArgumentException">The configuration is invalid.</exception>
    /// <exception cref="InvalidOperationException">A previous call used a different
    /// <see cref="HecoleMediatorConfiguration.Lifetime"/>, <see cref="HecoleMediatorConfiguration.RequireScope"/> or
    /// notification publisher, or two different handlers were found for the same request.</exception>
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
        }
        else
        {
            registry.Mediator = new MediatorOptions(lifetime, requireScope, publisher);
            services.AddSingleton(rootProvider => new MediatorRuntime(rootProvider, registry, requireScope));
            services.AddSingleton<IRequestCatalog>(static sp => sp.GetRequiredService<MediatorRuntime>().Catalog);
            services.Add(new ServiceDescriptor(typeof(IMediator), typeof(Mediator), lifetime));
            services.Add(new ServiceDescriptor(typeof(ISender), static sp => sp.GetRequiredService<IMediator>(), lifetime));
            services.Add(new ServiceDescriptor(typeof(IPublisher), static sp => sp.GetRequiredService<IMediator>(), lifetime));

            // A publisher registered directly in the container before this call is kept.
            services.TryAdd(publisher.ToDescriptor(lifetime));
        }

        foreach (var assembly in configuration.AssembliesToRegister)
            registry.Scan(services, assembly);

        // A behavior already in the container with the same service and implementation type is not added again.
        foreach (var behavior in configuration.BehaviorsToRegister)
            services.TryAddEnumerable(behavior);

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the single handler of <typeparamref name="TRequest"/>
    /// (as <see cref="IRequestHandler{TRequest, TResponse}"/>, transient).
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TRequest"/> already has a different handler
    /// (registered here or found by an assembly scan). Registering the same handler again does nothing.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TRequest"/> implements more than one request interface.</exception>
    public static IServiceCollection AddRequestHandler<TRequest, TResponse, THandler>(this IServiceCollection services)
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (HandlerRegistry.GetOrAdd(services).Add(new HandlerRegistration(
                typeof(TRequest), typeof(TResponse), typeof(THandler), typeof(IRequestHandler<TRequest, TResponse>),
                new Executor<TRequest, TResponse>())))
        {
            services.AddTransient<IRequestHandler<TRequest, TResponse>, THandler>();
        }

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> as the single handler of <typeparamref name="TRequest"/>, a request
    /// without response value (as <see cref="IRequestHandler{TRequest}"/>, transient).
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TRequest"/> already has a different handler
    /// (registered here or found by an assembly scan). Registering the same handler again does nothing.</exception>
    /// <exception cref="ArgumentException"><typeparamref name="TRequest"/> implements more than one request interface.</exception>
    public static IServiceCollection AddRequestHandler<TRequest, THandler>(this IServiceCollection services)
        where TRequest : IRequest
        where THandler : class, IRequestHandler<TRequest>
    {
        ArgumentNullException.ThrowIfNull(services);

        if (HandlerRegistry.GetOrAdd(services).Add(new HandlerRegistration(
                typeof(TRequest), typeof(Unit), typeof(THandler), typeof(IRequestHandler<TRequest>),
                new VoidExecutor<TRequest>())))
        {
            services.AddTransient<IRequestHandler<TRequest>, THandler>();
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
    /// container is built. It reads registrations only: no handler, behavior or publisher is created.
    /// It checks that:
    /// <list type="bullet">
    /// <item>every request known at registration (<see cref="IRequestCatalog"/>) has exactly one handler in the container
    /// (a handler registered directly in the container for a request that also has one from a scan or
    /// <c>AddRequestHandler</c> is reported, with both names);</item>
    /// <item>every concrete request type in <paramref name="requestAssemblies"/> (implementing <see cref="IRequest"/> or
    /// <see cref="IRequest{TResponse}"/>) implements only one request interface and has exactly one handler;</item>
    /// <item>the <see cref="INotificationPublisher"/> and the notification handlers registered by scan or
    /// <c>AddNotificationHandler</c> are in the container.</item>
    /// </list>
    /// An open behavior whose constraints do not fit a request is skipped for that request; that is allowed and is not
    /// reported.
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
