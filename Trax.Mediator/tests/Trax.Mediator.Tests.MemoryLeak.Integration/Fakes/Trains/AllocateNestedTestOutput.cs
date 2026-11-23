using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>Builds one child payload per declared child input.</summary>
internal sealed class AllocateNestedTestOutput : Junction<NestedTestInput, NestedTestOutput>
{
    public override Task<NestedTestOutput> Run(NestedTestInput input)
    {
        var results = input
            .ChildInputs.Select(child => new MemoryTestOutput
            {
                Id = child.Id,
                ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                ProcessedData = new string('Y', child.DataSizeBytes),
                Success = true,
                Message = $"Child train {child.Id} processed",
            })
            .ToList();

        return Task.FromResult(
            new NestedTestOutput
            {
                Id = input.Id,
                ProcessedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
                ChildResults = results,
                Success = true,
                Message = $"Processed {results.Count} child trains",
            }
        );
    }
}
