namespace Hecole.Mediator.Interfaces
{
    /// <summary>
    /// Marker request that expects a response of type TResponse.
    /// </summary>
    public interface IRequest<out TResponse> { }
}
