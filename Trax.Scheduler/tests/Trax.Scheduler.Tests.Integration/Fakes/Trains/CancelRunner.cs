using System.Collections.Concurrent;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Cancels the registered runner token and returns without observing it.</summary>
internal sealed class CancelRunner : Junction<CancelsItsRunnerInput, Unit>
{
    public override async Task<Unit> Run(CancelsItsRunnerInput input)
    {
        if (CancelsItsRunnerTrain.Runners.TryGetValue(input.Key, out var runner))
            await runner.CancelAsync().ConfigureAwait(false);

        return Unit.Default;
    }
}
