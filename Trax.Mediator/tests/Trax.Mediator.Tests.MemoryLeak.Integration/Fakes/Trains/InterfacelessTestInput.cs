using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Input type for the interfaceless test train.
/// </summary>
public record InterfacelessTestInput
{
    public required string Id { get; init; }
}
