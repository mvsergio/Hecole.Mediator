using Hecole.Mediator.Tests.ScanTarget;

namespace Hecole.Mediator.Tests.ScanConflict;

/// <summary>A second handler for <see cref="ScanQuery"/>, which already has <see cref="ScanQueryHandler"/>.</summary>
public sealed class ConflictingScanQueryHandler : IRequestHandler<ScanQuery, string>
{
    public Task<string> Handle(ScanQuery request, CancellationToken cancellationToken) => Task.FromResult("conflict");
}
