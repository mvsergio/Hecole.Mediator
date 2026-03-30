using System.Threading;
using System.Threading.Tasks;

namespace Hecole.Mediator.Interfaces.Behaviors
{
    /// <summary>
    /// Represents a delegate that invokes the next behavior or the final handler in the pipeline.
    /// </summary>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

    /// <summary>
    /// Defines a pipeline behavior that wraps the execution of a request handler.
    /// Behaviors are executed in order of registration (first registered = outermost).
    /// </summary>
    /// <typeparam name="TRequest">The type of the request.</typeparam>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    public interface IPipelineBehavior<in TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        /// <summary>
        /// Handles the pipeline step. Call <paramref name="next"/> to continue the pipeline.
        /// </summary>
        Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken);
    }
}
