namespace Hecole.Mediator;

/// <summary>
/// Wraps the handling of a request: runs code before and after the rest of the pipeline.
/// </summary>
/// <typeparam name="TRequest">The type of the request.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Handles the pipeline step. Call <paramref name="next"/> to continue the pipeline.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="next">Continues with the next behavior or the handler.</param>
    /// <param name="cancellationToken">Token that cancels the operation.</param>
    /// <returns>The response.</returns>
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
