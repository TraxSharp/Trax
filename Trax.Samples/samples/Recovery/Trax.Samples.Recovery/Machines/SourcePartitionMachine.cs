using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.Samples.Recovery.Trains.Ingest;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>
/// One instance per partition of an index, a source and a month, owned by the system: discovery starts
/// them, and each runs <see cref="IIngestPartitionTrain"/> for its partition.
/// <code>
/// Discovered --Ingest--> Ingesting --done, unsure--> NeedsReview --Approve--> Approved
///                          │  ▲      --done-------> Ingested
///                          │  └─Retry── Failed     (the run failed, or was reaped)
///                          │  └─Retry── Cancelled  (an operator cancelled the run)
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A failed ingest is retried by entering <c>Ingesting</c> again, which queues a new run under a new
/// token; the old run's late outcome then lands nowhere. The scheduler never retries an invoked run.
/// </para>
/// <para>
/// No user's request reaches an instance: <c>SystemOwned()</c> keeps every <c>stateMachine</c> mutation
/// away from it, and operators see it read-only. The sample's <c>partitionAction</c> mutation, for
/// operators only, fires <c>Retry</c> and <c>Approve</c> through
/// <see cref="IMachineInstances.Advance{TMachine}"/>, as the system.
/// </para>
/// </remarks>
public sealed class SourcePartitionMachine : Machine<PartitionState, PartitionTrigger>
{
    public const string MachineId = "source-partition";

    // Declared before anything reads it: static fields initialize in order.
    private static readonly Reduction KeepWhatWasWritten = SetAll(
        Set((IngestedContext c) => c.Fingerprint)
            .FromInput((IngestPartitionResult o) => o.Fingerprint),
        Set((IngestedContext c) => c.Works).FromInput((IngestPartitionResult o) => o.Works),
        Set((IngestedContext c) => c.Created).FromInput((IngestPartitionResult o) => o.Created),
        Set((IngestedContext c) => c.Merged).FromInput((IngestPartitionResult o) => o.Merged),
        Set((IngestedContext c) => c.NeedsReview)
            .FromInput((IngestPartitionResult o) => o.NeedsReview)
    );

    /// <summary>The instance's context when discovery starts it.</summary>
    public static JsonObject ContextFor(string source, string month) =>
        new() { ["source"] = source, ["month"] = month };

    /// <summary>The key an instance is started and advanced under.</summary>
    public static MachineKey KeyFor(string source, string month) => MachineKey.Of(source, month);

    protected override void Configure(IMachineBuilder<PartitionState, PartitionTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .SystemOwned()
            .StartsAt(PartitionState.Discovered, () => ContextFor("unknown", "unknown"));

        m.In(PartitionState.Discovered)
            .Context<PartitionContext>()
            .On(PartitionTrigger.Ingest)
            .To(PartitionState.Ingesting);

        m.In(PartitionState.Ingesting)
            .Context<PartitionContext>()
            .Invokes<IIngestPartitionTrain, IngestPartitionInput, IngestPartitionResult>(
                ctx => new IngestPartitionInput(
                    ctx["source"].NotNull().GetValue<string>(),
                    ctx["month"].NotNull().GetValue<string>()
                )
            )
            // Tried in order: an unsure ingest goes to review, any other to Ingested.
            .OnDone(
                PartitionState.NeedsReview,
                when: Input((IngestPartitionResult o) => o.Unsure).IsTrue(),
                reduce: KeepWhatWasWritten
            )
            .OnDone(PartitionState.Ingested, reduce: KeepWhatWasWritten)
            .OnFailed(PartitionState.Failed)
            .OnCancelled(PartitionState.Cancelled);

        m.In(PartitionState.Ingested).Context<IngestedContext>();

        m.In(PartitionState.NeedsReview)
            .Context<IngestedContext>()
            .On(PartitionTrigger.Approve)
            .To(PartitionState.Approved);

        m.In(PartitionState.Approved).Context<IngestedContext>();

        m.In(PartitionState.Failed)
            .Context<PartitionContext>()
            .On(PartitionTrigger.Retry)
            .To(PartitionState.Ingesting);

        m.In(PartitionState.Cancelled)
            .Context<PartitionContext>()
            .On(PartitionTrigger.Retry)
            .To(PartitionState.Ingesting);
    }
}
