using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Ingest.Contracts;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.Fakes;

public enum IngestState
{
    Idle,
    Fetching,
    NeedsReview,
    Fetched,
    FetchFailed,
    Cancelled,
    Approved,
}

public enum IngestTrigger
{
    Start,
    Retry,
    Abandon,
    Approve,
}

/// <summary>
/// One stage of an ingest: <c>Fetching</c> invokes <see cref="IFetchTrain"/>, a sure output goes to
/// <c>Fetched</c>, an unsure one to <c>NeedsReview</c>, a failure to <c>FetchFailed</c> (retried by entering
/// <c>Fetching</c> again) and a cancel to <c>Cancelled</c>. Its IR is committed under
/// <c>Trax.Api.StateMachine/machines/ingest/</c>, and the TypeScript twin's differential corpus of it is replayed
/// here.
/// </summary>
public static class IngestMachine
{
    public sealed record SourceContext
    {
        [MinLength(1)]
        public string Source { get; init; } = "";
    }

    public sealed record FetchedContext
    {
        [MinLength(1)]
        public string Source { get; init; } = "";

        [MinLength(1)]
        public string Fingerprint { get; init; } = "";
    }

    public const string InitialSource = "s3://bucket/partition-1";

    // Declared before Built: static fields initialize in order, and Build reads it.
    private static readonly Reduction KeepFingerprint = Set((FetchedContext c) => c.Fingerprint)
        .FromInput((FetchOutput o) => o.Fingerprint);

    public static readonly BuiltMachine<IngestState, IngestTrigger> Built = Build();
    public static readonly SnapshotMachine<IngestState, IngestTrigger> Machine = Built.Engine;

    private static BuiltMachine<IngestState, IngestTrigger> Build()
    {
        var m = new MachineBuilder<IngestState, IngestTrigger>();
        m.Id("ingest")
            .Version(1)
            .StartsAt(IngestState.Idle, () => new JsonObject { ["source"] = InitialSource });

        m.In(IngestState.Idle)
            .Context<SourceContext>()
            .On(IngestTrigger.Start)
            .To(IngestState.Fetching);

        m.In(IngestState.Fetching)
            .Context<SourceContext>()
            .Invokes<IFetchTrain, FetchInput, FetchOutput>(ctx => new FetchInput(
                ctx["source"]!.GetValue<string>()
            ))
            .OnDone(
                IngestState.NeedsReview,
                when: Input((FetchOutput o) => o.Unsure).IsTrue(),
                reduce: KeepFingerprint
            )
            .OnDone(IngestState.Fetched, reduce: KeepFingerprint)
            .OnFailed(IngestState.FetchFailed)
            .OnCancelled(IngestState.Cancelled)
            .On(IngestTrigger.Abandon)
            .To(IngestState.Idle);

        m.In(IngestState.NeedsReview)
            .Context<FetchedContext>()
            .On(IngestTrigger.Approve)
            .To(IngestState.Approved);

        m.In(IngestState.Fetched).Context<FetchedContext>();
        m.In(IngestState.Approved).Context<FetchedContext>();

        m.In(IngestState.FetchFailed)
            .Context<SourceContext>()
            .On(IngestTrigger.Retry)
            .To(IngestState.Fetching);

        m.In(IngestState.Cancelled)
            .Context<SourceContext>()
            .On(IngestTrigger.Retry)
            .To(IngestState.Fetching);

        m.Differential(d =>
            d.OutcomeSample(
                    IngestState.Fetching,
                    new FetchOutput { Fingerprint = "sha256:abc", Unsure = false }
                )
                .OutcomeSample(
                    IngestState.Fetching,
                    new FetchOutput { Fingerprint = "sha256:def", Unsure = true }
                )
                .OutcomeSample(
                    IngestState.Fetching,
                    new FetchOutput { Fingerprint = "", Unsure = false }
                )
        );

        return m.Build();
    }
}
