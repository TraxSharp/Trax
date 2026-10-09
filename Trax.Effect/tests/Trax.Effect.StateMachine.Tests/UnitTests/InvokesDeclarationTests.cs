using System.Text.Json.Nodes;
using AwesomeAssertions;
using Ingest.Contracts;
using Trax.Effect.Models.Metadata;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A state that invokes a train (central ADR 0046), the pure half: what the builder accepts and refuses, how an
/// outcome moves a snapshot, what joins the reserved states, and how outcomes export to the IR.
/// </summary>
public class InvokesDeclarationTests
{
    private static Snapshot At(IngestState state, JsonObject context) =>
        new()
        {
            Machine = "ingest",
            Version = 1,
            State = state.ToString(),
            Context = context,
        };

    private static Snapshot Fetching() =>
        At(IngestState.Fetching, new JsonObject { ["source"] = IngestMachine.InitialSource });

    private static JsonNode? Output(string fingerprint, bool unsure) =>
        IngestMachine
            .Built.Invokes[IngestState.Fetching]
            .SerializeOutput(new FetchOutput { Fingerprint = fingerprint, Unsure = unsure });

    // A machine with one invoking state; configure adds or omits its outcome edges.
    private static MachineBuilder<IngestState, IngestTrigger> Builder(
        Action<IInvokeBuilder<IngestState, IngestTrigger>> outcomes
    )
    {
        var m = new MachineBuilder<IngestState, IngestTrigger>();
        m.Id("ingest").StartsAt(IngestState.Idle, () => new JsonObject());
        m.In(IngestState.Idle).On(IngestTrigger.Start).To(IngestState.Fetching);
        outcomes(
            m.In(IngestState.Fetching)
                .Invokes<IFetchTrain, FetchInput, FetchOutput>(_ => new FetchInput("s"))
        );
        return m;
    }

    [Test]
    public void An_invoking_state_without_OnCancelled_is_refused_at_build()
    {
        var m = Builder(i => i.OnDone(IngestState.Fetched).OnFailed(IngestState.FetchFailed));

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Fetching invokes IFetchTrain but declares no OnCancelled*");
    }

    [Test]
    public void An_invoking_state_without_OnFailed_is_refused_at_build()
    {
        var m = Builder(i => i.OnDone(IngestState.Fetched).OnCancelled(IngestState.Cancelled));

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Fetching invokes IFetchTrain but declares no OnFailed*");
    }

    [Test]
    public void An_invoking_state_without_OnDone_is_refused_at_build()
    {
        var m = Builder(i =>
            i.OnFailed(IngestState.FetchFailed).OnCancelled(IngestState.Cancelled)
        );

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Fetching invokes IFetchTrain but declares no OnDone*");
    }

    [Test]
    public void OnFailed_declared_twice_is_refused_at_build()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnFailed(IngestState.Cancelled)
                .OnCancelled(IngestState.Cancelled)
        );

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Fetching declares OnFailed 2 times*");
    }

    [Test]
    public void A_state_invoking_two_trains_is_refused_at_build()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
                .Invokes<IFetchTrain, FetchInput, FetchOutput>(_ => new FetchInput("again"))
                .OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Fetching invokes 2 trains*a state invokes at most one*");
    }

    [Test]
    public void A_train_named_by_its_class_rather_than_its_interface_is_refused_at_build()
    {
        var m = new MachineBuilder<IngestState, IngestTrigger>();
        m.Id("ingest").StartsAt(IngestState.Idle, () => new JsonObject());
        m.In(IngestState.Fetching)
            .Invokes<FetchTrainClass, FetchInput, FetchOutput>(_ => new FetchInput("s"))
            .OnDone(IngestState.Fetched)
            .OnFailed(IngestState.FetchFailed)
            .OnCancelled(IngestState.Cancelled);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*FetchTrainClass, which is a class*by its interface*");
    }

    [Test]
    public void A_tuple_output_is_refused_at_build()
    {
        // A tuple's elements are fields, which the stored output does not carry: built, an unsure output took the
        // unguarded OnDone to Fetched and its reduction wrote a null fingerprint.
        var m = new MachineBuilder<IngestState, IngestTrigger>();
        m.Id("ingest").StartsAt(IngestState.Idle, () => new JsonObject());
        m.In(IngestState.Idle).On(IngestTrigger.Start).To(IngestState.Fetching);
        m.In(IngestState.Fetching)
            .Invokes<ITupleFetchTrain, FetchInput, (string Fingerprint, bool Unsure)>(
                _ => new FetchInput("s")
            )
            .OnDone(
                IngestState.NeedsReview,
                when: Input(((string Fingerprint, bool Unsure) o) => o.Unsure).IsTrue()
            )
            .OnDone(IngestState.Fetched)
            .OnFailed(IngestState.FetchFailed)
            .OnCancelled(IngestState.Cancelled);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*Fetching invokes ITupleFetchTrain, whose output*is a tuple*Return a record*"
            );
    }

    [Test]
    public void A_user_edge_into_an_outcome_target_is_refused_at_build()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );
        m.In(IngestState.NeedsReview).On(IngestTrigger.Approve).To(IngestState.Fetched);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*enters Fetched from NeedsReview on Approve, but Fetched is where the outcome of IFetchTrain*"
            );
    }

    [Test]
    public void An_outcome_sent_to_the_initial_state_is_refused_at_build_naming_why()
    {
        // The initial state as an outcome target would join the reserved states, so no save could create a draft
        // of the machine at all, and the abandon edge back to it would be refused as an edge into an outcome target.
        foreach (
            var outcomes in new Action<IInvokeBuilder<IngestState, IngestTrigger>>[]
            {
                i =>
                    i.OnDone(IngestState.Fetched)
                        .OnFailed(IngestState.Idle)
                        .OnCancelled(IngestState.Cancelled),
                i =>
                    i.OnDone(IngestState.Fetched)
                        .OnFailed(IngestState.FetchFailed)
                        .OnCancelled(IngestState.Idle),
            }
        )
        {
            var m = Builder(outcomes);
            m.In(IngestState.Fetching).On(IngestTrigger.Abandon).To(IngestState.Idle);

            var build = () => m.Build();

            build
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage(
                    "*starts at Idle, but Idle is also where the outcome of IFetchTrain, invoked in Fetching, goes*"
                );
        }
    }

    [Test]
    public void A_user_edge_into_an_outcome_target_that_itself_invokes_a_train_is_allowed()
    {
        // A chained stage: Fetching's outcome enters Fetched, which invokes the next run. Retrying that stage
        // re-enters Fetched by a user edge, which only queues a new run and forges no result.
        var m = Builder(i =>
            i.OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );
        m.In(IngestState.Fetched)
            .Invokes<IFetchTrain, FetchInput, FetchOutput>(_ => new FetchInput("next"))
            .OnDone(IngestState.NeedsReview)
            .OnFailed(IngestState.FetchFailed)
            .OnCancelled(IngestState.Cancelled);
        m.In(IngestState.FetchFailed).On(IngestTrigger.Approve).To(IngestState.Fetched);

        var build = () => m.Build();

        build.Should().NotThrow();
    }

    [Test]
    public void A_self_loop_on_an_outcome_target_is_allowed()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );
        m.In(IngestState.Fetched).On(IngestTrigger.Approve).To(IngestState.Fetched);

        var build = () => m.Build();

        build.Should().NotThrow();
    }

    [Test]
    public void An_outcome_sent_to_an_effects_target_is_refused_at_build()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.Approved)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );
        m.In(IngestState.NeedsReview)
            .On(IngestTrigger.Approve)
            .RunsOnce<object>()
            .To(IngestState.Approved);

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*outcome of IFetchTrain to Approved, which is the target of the effect*");
    }

    [Test]
    public void An_invoked_train_does_not_count_against_the_one_irreversible_effect()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );
        m.In(IngestState.NeedsReview)
            .On(IngestTrigger.Approve)
            .RunsOnce<object>()
            .To(IngestState.Approved);

        var built = m.Build();

        built.Effects.Should().ContainSingle();
        built.Invokes.Should().ContainKey(IngestState.Fetching);
    }

    [Test]
    public void An_outcome_sample_for_a_state_that_invokes_nothing_is_refused_at_build()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );
        m.In(IngestState.Fetched).Context();
        m.Differential(d => d.OutcomeSample(IngestState.Idle, new FetchOutput()));

        var build = () => m.Build();

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*outcome sample for Idle, which invokes no train*");
    }

    [Test]
    public void The_reserved_states_are_the_invoking_states_and_every_outcome_target()
    {
        IngestMachine
            .Built.ReservedStates.Should()
            .BeEquivalentTo([
                IngestState.Fetching,
                IngestState.NeedsReview,
                IngestState.Fetched,
                IngestState.FetchFailed,
                IngestState.Cancelled,
            ]);
    }

    [Test]
    public void The_reserved_states_keep_committed_states_and_effect_targets()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.Fetched)
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );
        m.In(IngestState.NeedsReview)
            .On(IngestTrigger.Approve)
            .RunsOnce<object>()
            .To(IngestState.Approved);
        m.In(IngestState.Idle).Committed();

        m.Build()
            .ReservedStates.Should()
            .Contain([IngestState.Idle, IngestState.Approved, IngestState.Fetching]);
    }

    [Test]
    public void The_invoke_declaration_names_the_train_and_builds_its_input_from_the_context()
    {
        var invoke = IngestMachine.Built.Invokes[IngestState.Fetching];

        invoke.TrainType.Should().Be(typeof(IFetchTrain));
        invoke.TrainName.Should().Be(typeof(IFetchTrain).FullName);
        invoke.InputType.Should().Be(typeof(FetchInput));
        invoke.OutputType.Should().Be(typeof(FetchOutput));
        invoke
            .CreateInput(new JsonObject { ["source"] = "s3://x" })
            .Should()
            .Be(new FetchInput("s3://x"));
    }

    [Test]
    public void A_guarded_OnDone_routes_an_unsure_output_to_its_own_target()
    {
        var result = IngestMachine.Machine.ApplyOutcome(
            Fetching(),
            new InvokeOutcome.Done(Output("sha256:def", unsure: true))
        );

        var moved = result.Should().BeOfType<AdvanceResult.Transitioned>().Subject.Snapshot;
        moved.State.Should().Be(nameof(IngestState.NeedsReview));
        moved.Context["fingerprint"]!.GetValue<string>().Should().Be("sha256:def");
        moved.Context["source"]!.GetValue<string>().Should().Be(IngestMachine.InitialSource);
    }

    [Test]
    public void A_sure_output_takes_the_unguarded_OnDone_after_the_guarded_one()
    {
        var result = IngestMachine.Machine.ApplyOutcome(
            Fetching(),
            new InvokeOutcome.Done(Output("sha256:abc", unsure: false))
        );

        var moved = result.Should().BeOfType<AdvanceResult.Transitioned>().Subject.Snapshot;
        moved.State.Should().Be(nameof(IngestState.Fetched));
        moved.Context["fingerprint"]!.GetValue<string>().Should().Be("sha256:abc");
    }

    [Test]
    public void A_failed_and_a_cancelled_run_take_their_own_edges()
    {
        var failed = IngestMachine.Machine.ApplyOutcome(Fetching(), new InvokeOutcome.Failed());
        var cancelled = IngestMachine.Machine.ApplyOutcome(
            Fetching(),
            new InvokeOutcome.Cancelled()
        );

        failed
            .Should()
            .BeOfType<AdvanceResult.Transitioned>()
            .Which.Snapshot.State.Should()
            .Be(nameof(IngestState.FetchFailed));
        cancelled
            .Should()
            .BeOfType<AdvanceResult.Transitioned>()
            .Which.Snapshot.State.Should()
            .Be(nameof(IngestState.Cancelled));
    }

    [Test]
    public void An_output_no_OnDone_edge_accepts_is_a_no_transition()
    {
        var m = Builder(i =>
            i.OnDone(IngestState.NeedsReview, when: Input((FetchOutput o) => o.Unsure).IsTrue())
                .OnFailed(IngestState.FetchFailed)
                .OnCancelled(IngestState.Cancelled)
        );
        var engine = m.Build().Engine;

        var result = engine.ApplyOutcome(
            At(IngestState.Fetching, new JsonObject()),
            new InvokeOutcome.Done(Output("sha256:abc", unsure: false))
        );

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [Test]
    public void An_outcome_after_the_state_was_left_is_a_no_transition()
    {
        var left = At(
            IngestState.Fetched,
            new JsonObject { ["source"] = "s", ["fingerprint"] = "sha256:abc" }
        );

        var result = IngestMachine.Machine.ApplyOutcome(left, new InvokeOutcome.Failed());

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [Test]
    public void An_outcome_trigger_naming_another_state_is_a_no_transition()
    {
        var idle = At(IngestState.Idle, new JsonObject { ["source"] = "s" });

        var result = IngestMachine.Machine.AdvanceOutcome(idle, "Fetching.done", null);

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [Test]
    public void An_output_whose_reduction_breaks_the_target_context_is_invalid_context()
    {
        var result = IngestMachine.Machine.ApplyOutcome(
            Fetching(),
            new InvokeOutcome.Done(Output("", unsure: false))
        );

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.InvalidContext);
    }

    [Test]
    public void Advance_never_applies_an_outcome_trigger()
    {
        var result = IngestMachine.Machine.Advance(
            Fetching(),
            "Fetching.done",
            Output("sha256:abc", unsure: false)
        );

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
        IngestMachine.Machine.CanFire(Fetching(), "Fetching.done").Should().BeFalse();
    }

    [Test]
    public void Describe_lists_the_outcome_edges_under_their_outcome_triggers()
    {
        var structure = IngestMachine.Machine.Describe();

        structure["triggers"]!
            .AsArray()
            .Select(t => t!.GetValue<string>())
            .Should()
            .Contain(["Fetching.done", "Fetching.failed", "Fetching.cancelled"]);
        structure["transitions"]!
            .AsArray()
            .Select(t => t!.ToJsonString())
            .Should()
            .Contain("""{"from":"Fetching","trigger":"Fetching.done","to":"NeedsReview"}""");
    }

    [Test]
    public void Outcomes_export_as_their_own_trigger_kind_per_invoke_edge_with_the_output_schema()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(IngestMachine.Built))!;

        // The user's triggers are unchanged; outcomes are their own kind, one per outcome of the invoking state.
        ir["triggers"]!
            .AsArray()
            .Select(t => t!.GetValue<string>())
            .Should()
            .Equal("Abandon", "Approve", "Retry", "Start");
        var outcomes = (JsonObject)ir["outcomes"]!;
        outcomes
            .Select(kv => kv.Key)
            .Should()
            .BeEquivalentTo("Fetching.done", "Fetching.failed", "Fetching.cancelled");

        var done = (JsonObject)outcomes["Fetching.done"]!;
        done["state"]!.GetValue<string>().Should().Be("Fetching");
        done["outcome"]!.GetValue<string>().Should().Be("done");
        done["train"]!.GetValue<string>().Should().Be(typeof(IFetchTrain).FullName);
        var edges = done["edges"]!.AsArray().Select(e => (JsonObject)e!).ToList();
        edges.Select(e => e["to"]!.GetValue<string>()).Should().Equal("NeedsReview", "Fetched");
        edges[0]["guard"]!["rule"]!.GetValue<string>().Should().Be("boolEquals");
        edges[0]["guard"]!["source"]!.GetValue<string>().Should().Be("input");
        edges[0]["reduce"]!["reduce"]!.GetValue<string>().Should().Be("set");
        edges[1].ContainsKey("guard").Should().BeFalse();

        outcomes["Fetching.cancelled"]!["edges"]!.AsArray().Single()!["to"]!
            .GetValue<string>()
            .Should()
            .Be("Cancelled");

        // The success outcome's input schema is the train's output type; failure and cancel take none.
        var fields = ir["inputs"]!["Fetching.done"]!["fields"]!
            .AsArray()
            .Select(f =>
                (Name: f!["name"]!.GetValue<string>(), Type: f["type"]!.GetValue<string>())
            )
            .ToList();
        fields.Should().Equal(("fingerprint", "string"), ("unsure", "boolean"));
        ((JsonObject)ir["inputs"]!).ContainsKey("Fetching.failed").Should().BeFalse();

        // Outcome edges are not ordinary transitions, and the input mapping is never exported.
        ir["transitions"]!
            .AsArray()
            .Select(t => t!["trigger"]!.GetValue<string>())
            .Should()
            .NotContain(t => t.Contains('.'));
        done.Select(kv => kv.Key).Should().BeEquivalentTo("edges", "outcome", "state", "train");
    }

    [Test]
    public void A_machine_that_invokes_nothing_exports_no_outcomes()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(DeclarativeTurnstile.Built))!;

        ir.ContainsKey("outcomes").Should().BeFalse();
    }

    [Test]
    public void The_ingest_IR_matches_the_committed_golden_the_twin_reads()
    {
        var file = FixturePaths.IrFile("ingest");
        file.Should().NotBeNull("the shared machines/ directory is part of the repository");
        var exported = IrExporter.Export(IngestMachine.Built);

        if (Environment.GetEnvironmentVariable("UPDATE_IR") == "1")
            File.WriteAllText(file!, exported + "\n");

        File.ReadAllText(file!)
            .TrimEnd('\n')
            .Should()
            .Be(exported, "regenerate the golden deliberately with UPDATE_IR=1");
    }

    /// <summary>A train named by its class instead of its interface; never constructed.</summary>
    public abstract class FetchTrainClass : IFetchTrain
    {
        public abstract Task<FetchOutput> Run(
            FetchInput input,
            CancellationToken cancellationToken = default
        );

        public abstract Metadata? Metadata { get; }

        public abstract void Dispose();
    }

    /// <summary>A train whose output is a tuple; never constructed.</summary>
    public interface ITupleFetchTrain
        : Trax.Effect.Services.ServiceTrain.IServiceTrain<
            FetchInput,
            (string Fingerprint, bool Unsure)
        >;
}
