namespace Hecole.Mediator;

/// <summary>
/// Sends requests and publishes notifications.
/// Prefer <see cref="ISender"/> or <see cref="IPublisher"/> when only one side is needed.
/// </summary>
public interface IMediator : ISender, IPublisher;
