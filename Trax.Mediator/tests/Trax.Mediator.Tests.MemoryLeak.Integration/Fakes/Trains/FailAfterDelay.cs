using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Models;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.Fakes.Trains;

/// <summary>Throws after a delay, so the error path allocates the way a real failure would.</summary>
internal sealed class FailAfterDelay : Junction<FailingTestInput, MemoryTestOutput>
{
    public override async Task<MemoryTestOutput> Run(FailingTestInput input)
    {
        // allowed-delay: the delay is the allocation pattern under test, not a wait for a signal.
        await Task.Delay(input.ProcessingDelayMs, CancellationToken).ConfigureAwait(false);

        throw new InvalidOperationException($"Intentional failure in train {input.Id}");
    }
}
