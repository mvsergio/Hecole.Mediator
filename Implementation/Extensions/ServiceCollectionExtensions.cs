using System;
using System.Linq;
using System.Reflection;
using Hecole.Mediator.Implementation;
using Hecole.Mediator.Interfaces;
using Hecole.Mediator.Interfaces.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Hecole.Mediator.Implementation.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the CoreMediator and scans assemblies for IRequestHandler<,>, INotificationHandler<>,
        /// and IPipelineBehavior<,> implementations. Similar to MediatR's RegisterServicesFromAssemblies but simpler.
        /// </summary>
        public static IServiceCollection AddCoreMediator(this IServiceCollection services, params Assembly[] assembliesToScan)
        {
            if (assembliesToScan == null || assembliesToScan.Length == 0)
            {
                assembliesToScan = [Assembly.GetCallingAssembly()];
            }

            services.AddSingleton<ICoreMediator, CoreMediator>();
            services.AddSingleton<CoreMediator>(); // in case direct type is needed

            foreach (var assembly in assembliesToScan.Distinct())
            {
                var types = assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface).ToArray();

                foreach (var type in types)
                {
                    // IRequestHandler<,>
                    var requestHandlerIfaces = type.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                        .ToArray();
                    foreach (var iface in requestHandlerIfaces)
                        services.AddTransient(iface, type);

                    // INotificationHandler<>
                    var notificationIfaces = type.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
                        .ToArray();
                    foreach (var iface in notificationIfaces)
                        services.AddTransient(iface, type);

                    // IPipelineBehavior<,>
                    var pipelineIfaces = type.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))
                        .ToArray();
                    foreach (var iface in pipelineIfaces)
                        services.AddTransient(iface, type);
                }
            }

            return services;
        }
    }
}
