using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>
/// Allocates the large payload the leak tests measure. The work sits in a junction because a
/// train declares which junctions run, it does not do the work itself.
/// </summary>
internal sealed class AllocateMemoryTestOutput : Junction<MemoryTestInput, MemoryTestOutput>
{
    public override async Task<MemoryTestOutput> Run(MemoryTestInput input)
    {
        // allowed-delay: the delay is the allocation pattern under test, not a wait for a signal.
        await Task.Delay(input.ProcessingDelayMs, CancellationToken).ConfigureAwait(false);

        return new MemoryTestOutput
        {
            Id = input.Id,
            ProcessedAt = DateTime.UtcNow,
            ProcessedData = new string('X', input.DataSizeBytes),
            Success = true,
            Message = string.Create(
                CultureInfo.InvariantCulture,
                $"Successfully processed train {input.Id} with {input.DataSizeBytes} bytes of data"
            ),
        };
    }
}
