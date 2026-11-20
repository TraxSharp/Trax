using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Ingest.Contracts;
using Trax.Core.Extensions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Cli.Tests.Fakes;

/// <summary>
/// A machine whose <c>Fetching</c> state invokes a train, identical to the committed <c>ingest</c> machine in
/// Trax.StateMachine/ts, so the CLI's IR, twin and corpus for a machine with outcome triggers can be
/// byte-compared against the committed artifacts.
/// </summary>
public sealed class DeclarativeIngestMachine : Machine<IngestState, IngestTrigger>
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

    protected override void Configure(IMachineBuilder<IngestState, IngestTrigger> m)
    {
        var keepFingerprint = Set((FetchedContext c) => c.Fingerprint)
            .FromInput((FetchOutput o) => o.Fingerprint);

        m.Id("ingest")
            .Version(1)
            .StartsAt(
                IngestState.Idle,
                () => new JsonObject { ["source"] = "s3://bucket/partition-1" }
            );

        m.In(IngestState.Idle)
            .Context<SourceContext>()
            .On(IngestTrigger.Start)
            .To(IngestState.Fetching);

        m.In(IngestState.Fetching)
            .Context<SourceContext>()
            .Invokes<IFetchTrain, FetchInput, FetchOutput>(ctx => new FetchInput(
                ctx["source"].NotNull().GetValue<string>()
            ))
            .OnDone(
                IngestState.NeedsReview,
                when: Input((FetchOutput o) => o.Unsure).IsTrue(),
                reduce: keepFingerprint
            )
            .OnDone(IngestState.Fetched, reduce: keepFingerprint)
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
    }
}
