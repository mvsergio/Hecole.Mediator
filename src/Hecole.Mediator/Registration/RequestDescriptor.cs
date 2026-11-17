namespace Hecole.Mediator;

/// <summary>
/// One request known at registration and its handler.
/// </summary>
/// <param name="RequestType">The request type.</param>
/// <param name="ResponseType">The response type; <see cref="Unit"/> for a request without response value.</param>
/// <param name="HandlerType">The handler type.</param>
public sealed record RequestDescriptor(Type RequestType, Type ResponseType, Type HandlerType);
