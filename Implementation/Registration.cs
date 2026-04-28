using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Hecole.Mediator.Implementation;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ICoreMediator"/> as Scoped and scans the specified assemblies
    /// for <see cref="IRequestHandler{TRequest, TResponse}"/>, <see cref="INotificationHandler{TNotification}"/>
    /// and <see cref="IPipelineBehavior{TRequest, TResponse}"/> implementations.
    /// If no assemblies are specified, all loaded assemblies are scanned.
    /// </summary>
    public static IServiceCollection AddHecoleMediator(this IServiceCollection services, params Assembly[] assembliesToScan)
    {
        var toScan = assembliesToScan == null || assembliesToScan.Length == 0
            ? AppDomain.CurrentDomain.GetAssemblies()
            : assembliesToScan;

        // register mediator as scoped (works with scoped DbContext)
        services.AddScoped<ICoreMediator, CoreMediator>();

        foreach (var asm in toScan)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var type in types.Where(t => t != null && t.IsClass && !t.IsAbstract))
            {
                RegisterByInterface(services, type, typeof(IRequestHandler<,>));
                RegisterByInterface(services, type, typeof(INotificationHandler<>));
                RegisterByInterface(services, type, typeof(IPipelineBehavior<,>));
            }
        }

        return services;
    }

    /// <summary>
    /// Registers <paramref name="implementationType"/> for every closed/open generic interface it implements
    /// matching <paramref name="openGenericServiceType"/> (e.g. <c>typeof(IPipelineBehavior&lt;,&gt;)</c>).
    ///
    /// <para>Open-generic implementations (e.g. <c>ObservabilityBehavior&lt;TRequest, TResponse&gt;</c>)
    /// are registered as <c>(typeof(IPipelineBehavior&lt;,&gt;), typeof(ObservabilityBehavior&lt;,&gt;))</c>
    /// so MS DI constructs the correct closed type per request. Closed-generic implementations are
    /// registered with the parameterized interface they expose.</para>
    /// </summary>
    private static void RegisterByInterface(IServiceCollection services, Type implementationType, Type openGenericServiceType)
    {
        var matchingIfaces = implementationType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericServiceType);

        foreach (var iface in matchingIfaces)
        {
            if (implementationType.IsGenericTypeDefinition)
            {
                // Open-generic: register definition→definition (deduped — many ifaces collapse to same pair)
                if (!services.Any(d => d.ServiceType == openGenericServiceType && d.ImplementationType == implementationType))
                    services.AddTransient(openGenericServiceType, implementationType);
            }
            else
            {
                services.AddTransient(iface, implementationType);
            }
        }
    }
}
