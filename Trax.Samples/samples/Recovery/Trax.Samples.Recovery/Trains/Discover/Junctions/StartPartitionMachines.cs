using Trax.Effect.Services.EffectJunction;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.Trains.Discover.Junctions;

/// <summary>
/// Starts one <see cref="SourcePartitionMachine"/> instance per partition found, keyed by its source and
/// month, and sends each new one into <c>Ingesting</c>, which queues its ingest run. The instance's id
/// is derived from the key, so running discovery again finds the instances it started and creates none.
/// </summary>
/// <remarks>
/// Starting an instance and sending it into <c>Ingesting</c> are two writes. An instance left in
/// <c>Discovered</c> between them (the host died, say) is sent on by the next discovery; one already past
/// <c>Discovered</c> is left alone, so a rerun queues nothing for it.
/// </remarks>
public class StartPartitionMachines(IMachineInstances instances)
    : EffectJunction<DiscoveredPartitions, DiscoveredPartitions>
{
    public override async Task<DiscoveredPartitions> Run(DiscoveredPartitions discovered)
    {
        var started = 0;
        var sent = 0;
        foreach (var partition in discovered.Partitions)
        {
            var key = SourcePartitionMachine.KeyFor(partition.Source, partition.Month);
            var instance = await instances.Start<SourcePartitionMachine>(
                key,
                SourcePartitionMachine.ContextFor(partition.Source, partition.Month),
                CancellationToken
            );
            if (instance.Created)
                started++;

            if (instance.State != nameof(PartitionState.Discovered))
                continue;

            switch (
                await instances.Advance<SourcePartitionMachine>(
                    key,
                    nameof(PartitionTrigger.Ingest),
                    cancellationToken: CancellationToken
                )
            )
            {
                case AdvanceOutcome.Advanced:
                    sent++;
                    break;
                // Another discovery sent it on first: it is ingesting either way.
                case AdvanceOutcome.Conflict:
                    break;
                case var refused:
                    throw new InvalidOperationException(
                        $"The {partition.Source}/{partition.Month} instance could not be sent to ingest: "
                            + $"{refused}."
                    );
            }
        }

        return discovered with
        {
            InstancesStarted = started,
            InstancesSentToIngest = sent,
        };
    }
}
