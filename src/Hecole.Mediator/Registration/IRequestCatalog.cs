namespace Hecole.Mediator;

/// <summary>
/// The requests known at registration (assembly scan and <c>AddRequestHandler</c>), with their handlers.
/// Resolve it from the container. Requests whose handler was registered directly in the container
/// (<c>services.AddTransient&lt;IRequestHandler&lt;A, B&gt;, H&gt;()</c>) still work but are not listed.
/// </summary>
public interface IRequestCatalog
{
    /// <summary>The known requests, in registration order.</summary>
    IReadOnlyList<RequestDescriptor> Requests { get; }
}
