using Trax.Effect.Services.EffectJunction;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.Trains.PartitionAction.Junctions;

/// <summary>Fires the operator's action on the partition's instance, as the system.</summary>
public class AdvancePartition(IMachineInstances instances)
    : EffectJunction<PartitionActionInput, PartitionActionOutput>
{
    public override async Task<PartitionActionOutput> Run(PartitionActionInput input) =>
        await instances.Advance<SourcePartitionMachine>(
            SourcePartitionMachine.KeyFor(input.Source, input.Month),
            input.Action switch
            {
                PartitionAction.Retry => nameof(PartitionTrigger.Retry),
                PartitionAction.Approve => nameof(PartitionTrigger.Approve),
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            },
            cancellationToken: CancellationToken
        ) switch
        {
            AdvanceOutcome.Advanced advanced => new() { State = advanced.Snapshot.State },
            AdvanceOutcome.Rejected rejected => new() { Problem = rejected.Reason },
            AdvanceOutcome.NotFound => new() { Problem = "not-found" },
            AdvanceOutcome.Conflict => new() { Problem = "conflict" },
            AdvanceOutcome.LoadError error => new() { Problem = error.Code },
            _ => new() { Problem = "internal-error" },
        };
}
