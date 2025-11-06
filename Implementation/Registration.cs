using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Hecole.Mediator.Implementation;

public static class ServiceCollectionExtensions
{
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
                var handlerIfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>));
                foreach (var iface in handlerIfaces)
                    services.AddTransient(iface, type);

                var notificationIfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>));
                foreach (var iface in notificationIfaces)
                    services.AddTransient(iface, type);

                var pipelineIfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));
                foreach (var iface in pipelineIfaces)
                    services.AddTransient(iface, type);
            }
        }

        return services;
    }
}
