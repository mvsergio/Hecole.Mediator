namespace Hecole.Mediator;

/// <summary>
/// Continues the pipeline: calls the next behavior or, at the end, the handler.
/// </summary>
/// <typeparam name="TResponse">The type of the response.</typeparam>
/// <param name="t">Token that cancels the operation.</param>
/// <returns>The response.</returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken t = default);
