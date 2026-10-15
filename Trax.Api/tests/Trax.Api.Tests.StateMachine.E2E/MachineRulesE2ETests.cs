using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Every event's rule is checked below the resolver, on every surface, before anything is written or claimed: a
/// caller without the event's role is answered <c>FORBIDDEN</c> and the row is untouched, the effect-bound event
/// checks its rule before claiming its effect, and the generic opt-in's advance checks the same rule. Reads follow
/// the owner: another user's id, even for an operator, is answered exactly as an unknown id.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
[NonParallelizable]
public class MachineRulesE2ETests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string Database = "trax_sm_machine_rules";

    /// <summary>The generic opt-in's policy here: any signed-in caller, so only the event rules decide.</summary>
    private const string ToolingPolicy = "StateMachineTooling";

    private IHost _host = null!;
    private CountingCharge _charge = null!;

    [OneTimeSetUp]
    public async Task Up()
    {
        await E2EHost.RecreateDatabaseAsync(Database);
        _charge = new CountingCharge();
        _host = await E2EHost.StartAsync(
            Database,
            _charge,
            new E2EHostOptions
            {
                Served = [typeof(WizardMachine)],
                Machines = o => o.ExposeGenericStateMachineMutations(g => g.Policy = ToolingPolicy),
                Services = s =>
                    s.AddAuthorization(o =>
                        o.AddPolicy(ToolingPolicy, p => p.RequireAuthenticatedUser())
                    ),
            }
        );
    }

    [OneTimeTearDown]
    public async Task Down()
    {
        _host.Dispose();
        await E2EHost.DropDatabaseAsync(Database);
    }

    [Test]
    public async Task Discard_of_an_instance_the_server_moved_is_discard_not_allowed_and_keeps_it()
    {
        // Pay runs the effect, so Approving is a state only the server puts the instance in.
        var approving = await _host.WizardInApprovingAsync(E2EHost.CarolKey);
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id);

        using var doc = await _host.PostAsync(
            WizardOps.Discard(approving.Id, WizardOps.NewRequestId()),
            apiKey: E2EHost.CarolKey
        );
        var refused = WizardOps.Result(doc, "discard");

        refused
            .ProblemCode.Should()
            .Be(
                RefusalCode.DiscardNotAllowed.ToWire(),
                "an instance the server moved is the record of what it did, per " + Adr
            );
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id))
            .Should()
            .Be(before, "a refused discard deletes nothing");
    }

    [Test]
    public async Task Discard_follows_whether_the_server_or_its_owner_put_the_instance_in_its_state()
    {
        // Pay (Payer) runs the effect into Approving; its timer moves the instance on to Expired, carrying the record
        // that the server put it there. The owner's Edit then moves it to Naming, which is the owner's own doing.
        var approving = await _host.WizardInApprovingAsync(E2EHost.CarolKey);
        await E2ESql.ExecuteAsync(
            Database,
            "UPDATE trax.snapshot_draft SET due_at = now() - interval '1 minute' WHERE machine = @machine AND id = @id",
            ("machine", WizardMachine.MachineId),
            ("id", approving.Id)
        );
        await _host
            .Services.GetRequiredService<InvokeOutcomeReconciler>()
            .FireDueTimersOnce(CancellationToken.None);
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id))!
            .State.Should()
            .Be(nameof(WizardState.Expired));

        using (
            var doc = await _host.PostAsync(
                WizardOps.Discard(approving.Id, WizardOps.NewRequestId()),
                apiKey: E2EHost.CarolKey
            )
        )
            WizardOps
                .Result(doc, "discard")
                .ProblemCode.Should()
                .Be(
                    RefusalCode.DiscardNotAllowed.ToWire(),
                    "a timer moved on what the server decided, which stays its record, per " + Adr
                );

        (await _host.AdvanceWizardAsync(E2EHost.CarolKey, "edit", approving.Id))
            .ProblemCode.Should()
            .BeNull();
        using (
            var doc = await _host.PostAsync(
                WizardOps.Discard(approving.Id, WizardOps.NewRequestId()),
                apiKey: E2EHost.CarolKey
            )
        )
            WizardOps
                .Result(doc, "discard")
                .ProblemCode.Should()
                .BeNull(
                    "the owner's own event moved it last, so it is the owner's draft again, per "
                        + Adr
                );
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id))
            .Should()
            .BeNull();
    }

    [Test]
    public async Task A_caller_without_the_events_role_is_forbidden_and_nothing_is_written()
    {
        // Carol may pay but not approve.
        var approving = await _host.WizardInApprovingAsync(E2EHost.CarolKey);
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id);

        var refused = await _host.SendWizardEventAsync(E2EHost.CarolKey, "approve", approving.Id);

        refused
            .ProblemCode.Should()
            .Be(
                RefusalCode.Forbidden.ToWire(),
                "approve requires the Approver role, which Carol lacks, per " + Adr
            );
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id))
            .Should()
            .Be(before, "a forbidden event writes nothing: same state, context and token");
        (await E2ESql.CountInvokedRunsAsync(Database, approving.Id))
            .Should()
            .Be(0, "the build's run is never queued for a forbidden caller");
    }

    [Test]
    public async Task The_effect_bound_event_checks_its_rule_before_claiming_the_effect()
    {
        // Bob holds no role: he may describe his wizard but not pay for it.
        var paying = await _host.WizardInPayingAsync(E2EHost.BobKey);
        var callsBefore = _charge.Calls;
        var claimsBefore = await E2ESql.CountEffectClaimsAsync(Database, "wizard:pay");

        var refused = await _host.SendWizardEventAsync(E2EHost.BobKey, "pay", paying.Id);

        refused
            .ProblemCode.Should()
            .Be(RefusalCode.Forbidden.ToWire(), "pay requires the Payer role, per " + Adr);
        _charge
            .Calls.Should()
            .Be(callsBefore, "the rule is checked before the effect is claimed, so it never runs");
        (await E2ESql.CountEffectClaimsAsync(Database, "wizard:pay"))
            .Should()
            .Be(claimsBefore, "no claim is taken for a forbidden caller");
    }

    [Test]
    public async Task The_generic_opt_in_advance_checks_the_same_event_rule()
    {
        var approving = await _host.WizardInApprovingAsync(E2EHost.CarolKey);
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id);

        using var doc = await _host.PostAsync(
            """
            mutation Advance($input: AdvanceSnapshotInput!) {
              dispatch { stateMachine { advanceSnapshot(input: $input) {
                output { snapshot problem { code message } }
              } } }
            }
            """,
            new
            {
                input = new
                {
                    machine = WizardMachine.MachineId,
                    id = approving.Id.ToString(),
                    trigger = "Approve",
                },
            },
            E2EHost.CarolKey
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        doc.RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("stateMachine")
            .GetProperty("advanceSnapshot")
            .GetProperty("output")
            .GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(
                RefusalCode.Forbidden.ToWire(),
                "the rule runs below the resolver, so the generic advance checks it too, per " + Adr
            );
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, approving.Id))
            .Should()
            .Be(before);
    }

    [Test]
    public async Task Get_of_another_users_id_is_answered_like_an_unknown_id()
    {
        var alices = await _host.StartWizardAsync(E2EHost.AliceKey);
        var own = await _host.GetWizardAsync(E2EHost.AliceKey, alices.Id);
        own.ValueKind.Should().Be(JsonValueKind.Object, "the owner reads her own instance");

        var others = await ReadAs(E2EHost.BobKey, alices.Id);
        var unknown = await ReadAs(E2EHost.BobKey, Guid.NewGuid());

        unknown
            .Should()
            .Be(
                NotFound,
                "a read of an id the caller has no instance under answers null, with no error"
            );
        others
            .Should()
            .Be(
                unknown,
                "another user's id reveals nothing: the answer has the shape of a random id's, per "
                    + Adr
            );
    }

    [Test]
    public async Task An_operators_role_does_not_unlock_another_users_draft_through_machines()
    {
        var alices = await _host.WizardInPayingAsync(E2EHost.AliceKey);
        var before = await E2ESql.DraftAsync(Database, WizardMachine.MachineId, alices.Id);

        var unknown = await ReadAs(E2EHost.OperatorKey, Guid.NewGuid());
        unknown.Should().Be(NotFound, "an operator's read of a random id is not found");
        (await ReadAs(E2EHost.OperatorKey, alices.Id))
            .Should()
            .Be(
                unknown,
                "Operator and Admin reach the operations surface, never a user's draft, per " + Adr
            );

        using var sent = await _host.PostAsync(
            WizardOps.Event("back", alices.Id, WizardOps.NewRequestId()),
            apiKey: E2EHost.OperatorKey
        );
        WizardOps
            .Result(sent, "back")
            .Raw.GetProperty("problem")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(
                RefusalCode.NotFound.ToWire(),
                "an operator's event on a user's draft is answered as an unknown id, per " + Adr
            );
        (await E2ESql.DraftAsync(Database, WizardMachine.MachineId, alices.Id))
            .Should()
            .Be(before, "an operator's event cannot move a user's draft");
    }

    // What get answers for an id the caller holds no instance under: null, with no error, whoever else owns it.
    private const string NotFound = """{"data":{"machines":{"wizard":{"get":null}}}}""";

    // The whole response, with the id asked for replaced, so two answers compare by shape and content alone.
    private async Task<string> ReadAs(string apiKey, Guid id)
    {
        using var doc = await _host.PostAsync(WizardOps.Get(id), apiKey: apiKey);
        return doc
            .RootElement.GetRawText()
            .Replace(id.ToString(), "<id>", StringComparison.Ordinal);
    }
}
