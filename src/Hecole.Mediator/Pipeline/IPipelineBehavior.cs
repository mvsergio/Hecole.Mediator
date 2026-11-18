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
    /// <remarks>
    /// The outermost behavior receives the token given to <c>Send</c>; each inner behavior receives the token the
    /// behavior around it continued with. <c>next()</c> continues with <paramref name="cancellationToken"/>;
    /// <c>next(token)</c> with a token that can be canceled (a timeout, for example) continues with that token, so the
    /// inner behaviors and the handler receive it.
    /// </remarks>
    /// <param name="request">The request.</param>
    /// <param name="next">Continues with the next behavior or the handler.</param>
    /// <param name="cancellationToken">Token that cancels the operation: the caller's token for the outermost behavior,
    /// otherwise the token the enclosing behavior continued with.</param>
    /// <returns>The response.</returns>
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
