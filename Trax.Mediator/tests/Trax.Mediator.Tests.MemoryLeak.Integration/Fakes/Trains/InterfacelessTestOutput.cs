using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Output type for the interfaceless test train.
/// </summary>
public record InterfacelessTestOutput
{
    public required string Id { get; init; }
    public required string Message { get; init; }
}
