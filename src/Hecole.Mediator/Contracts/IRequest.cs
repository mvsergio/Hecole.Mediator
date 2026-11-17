namespace Hecole.Mediator;

/// <summary>
/// Marker for a request that produces no response value.
/// </summary>
public interface IRequest : IBaseRequest;

/// <summary>
/// Marker for a request that produces a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public interface IRequest<out TResponse> : IBaseRequest;
