using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Trains.Ingest;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

public enum PartitionState
{
    /// <summary>Discovery found the partition and started its instance.</summary>
    Discovered,

    /// <summary>The ingest train is running for the partition.</summary>
    Ingesting,

    /// <summary>The partition's works are in the corpus.</summary>
    Ingested,

    /// <summary>Ingested, but the model was unsure about some works, which wait for a person.</summary>
    NeedsReview,

    /// <summary>A person looked at the works held for review and let the partition through.</summary>
    Approved,

    /// <summary>The ingest run failed, or was reaped after its host died.</summary>
    Failed,

    /// <summary>An operator cancelled the ingest run.</summary>
    Cancelled,
}

public enum PartitionTrigger
{
    /// <summary>Discovery's first move: run the ingest.</summary>
    Ingest,

    /// <summary>Run the ingest again after it failed or was cancelled: a new run, under a new token.</summary>
    Retry,

    /// <summary>A person let a partition held for review through.</summary>
    Approve,
}

/// <summary>Which partition the instance is for. Every state carries it.</summary>
public sealed record PartitionContext
{
    [MinLength(1)]
    public string Source { get; init; } = "";

    [MinLength(1)]
    public string Month { get; init; } = "";
}

/// <summary>
/// An ingested partition: which one, and what the ingest wrote, as counts and a fingerprint. Pointers,
/// never rows: the works are in <c>topic_map.ingested_works</c>.
/// </summary>
public sealed record IngestedContext
{
    [MinLength(1)]
    public string Source { get; init; } = "";

    [MinLength(1)]
    public string Month { get; init; } = "";

    [MinLength(1)]
    public string Fingerprint { get; init; } = "";

    public int Works { get; init; }

    public int Created { get; init; }

    public int Merged { get; init; }

    public int NeedsReview { get; init; }
}

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
                    ctx["source"]!.GetValue<string>(),
                    ctx["month"]!.GetValue<string>()
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

    // One reduction that sets several fields. Rules.Set sets one; the Set reduction holds a list of steps.
    private static Reduction SetAll(params Reduction[] sets) =>
        new Reduction.Set(sets.SelectMany(s => ((Reduction.Set)s).Steps).ToList());
}
