using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Scoped marker service whose unique ID identifies a DI scope.
/// Register as Scoped — each child scope gets a distinct instance.
/// </summary>
public class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}
