using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>Builds the output for a train that has no dedicated interface.</summary>
internal sealed class BuildInterfacelessOutput
    : Junction<InterfacelessTestInput, InterfacelessTestOutput>
{
    public override Task<InterfacelessTestOutput> Run(InterfacelessTestInput input) =>
        Task.FromResult(
            new InterfacelessTestOutput
            {
                Id = input.Id,
                Message = $"Processed {input.Id} without a dedicated interface",
            }
        );
}
