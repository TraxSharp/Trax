using System.Collections.Concurrent;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

internal sealed class ProbeBody : Junction<DeliveryProbeInput, Unit>
{
    public override async Task<Unit> Run(DeliveryProbeInput input)
    {
        DeliveryProbeTrain.Runs.AddOrUpdate(input.Key, 1, (_, count) => count + 1);
        DeliveryProbeTrain.Tokens[input.Key] = CancellationToken;
        DeliveryProbeTrain
            .Started.GetOrAdd(
                input.Key,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            )
            .TrySetResult();

        if (DeliveryProbeTrain.Gates.TryGetValue(input.Key, out var gate))
            await gate.Task.WaitAsync(CancellationToken).ConfigureAwait(false);

        if (input.Fail)
            throw new TrainException($"Probe {input.Key} failed as asked.");

        return Unit.Default;
    }
}
