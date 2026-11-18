namespace Hecole.Mediator;

/// <summary>
/// Continues the pipeline: calls the next behavior or, at the end, the handler.
/// </summary>
/// <remarks>
/// <c>next(token)</c> with a token that can be canceled continues with that token. <c>next()</c>, or a token that
/// cannot be canceled (such as <see cref="CancellationToken.None"/>; an omitted argument and an explicit
/// <see langword="default"/> are the same value), continues with the token the calling behavior received in
/// <see cref="IPipelineBehavior{TRequest, TResponse}.Handle"/>. The delegate may be called more than once (retry);
/// each call runs the rest of the pipeline with the token given to that call.
/// </remarks>
/// <typeparam name="TResponse">The type of the response.</typeparam>
/// <param name="t">Token for the rest of the pipeline; one that cannot be canceled means the token the calling
/// behavior received.</param>
/// <returns>The response.</returns>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken t = default);
