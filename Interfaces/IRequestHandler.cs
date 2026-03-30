namespace Hecole.Mediator.Interfaces
{
    /// <summary>
    /// Defines a handler for a request that returns a response.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request.</typeparam>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        /// <summary>
        /// Handles the request and returns a response.
        /// </summary>
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }
}
