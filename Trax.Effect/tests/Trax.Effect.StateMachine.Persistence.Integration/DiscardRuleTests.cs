using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// <c>discard</c> declares a rule as <c>get</c> does, owner-only being its floor, and is refused as
/// <c>DISCARD_NOT_ALLOWED</c> from every state only the server puts an instance in: a committed state, an effect's
/// target, a state that invokes a train, a state an invoked train's outcome reaches, and the target of an event gated
/// by a role or a policy. An instance in any of them is the record of something the server did, so its owner cannot
/// erase it.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class DiscardRuleTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private InstanceHost _host = null!;

    [OneTimeSetUp]
    public void SetUp() => _host = InstanceHost.Create(provider);

    [OneTimeTearDown]
    public void TearDown() => _host.Dispose();

    private static string NewUser() => $"u-{Guid.NewGuid():N}";

    private async Task<Guid> Seed(string user, string machine, string state, JsonObject context)
    {
        var id = Guid.NewGuid();
        await _host.Seed(
            user,
            id,
            new Snapshot
            {
                Machine = machine,
                Version = 1,
                State = state,
                Context = context,
            }
        );
        return id;
    }

    private async Task<DiscardOutcome> Discard(string machine, Actor.Client actor, Guid id)
    {
        using var scope = _host.Scope();
        return await _host.Service(scope, machine).Discard(actor, id, $"d-{Guid.NewGuid():N}");
    }

    private static IEnumerable<TestCaseData> ServerStates()
    {
        yield return new TestCaseData(
            LedgerMachine.Id,
            nameof(LedgerState.Closed),
            new JsonObject { ["entries"] = new JsonArray(1) }
        ).SetName("{m}(committed)");
        yield return new TestCaseData(
            "ship",
            nameof(ShipState.Shipped),
            new JsonObject { ["receipt"] = "r-1" }
        ).SetName("{m}(effect target)");
        yield return new TestCaseData(
            RuledBuildMachine.Id,
            nameof(BuildState.Building),
            new JsonObject { ["source"] = "repo" }
        ).SetName("{m}(invoking)");
        yield return new TestCaseData(
            RuledBuildMachine.Id,
            nameof(BuildState.Built),
            new JsonObject { ["artifact"] = "a-1" }
        ).SetName("{m}(outcome target)");
        yield return new TestCaseData(
            RuledDeskMachine.Id,
            nameof(DeskState.Done),
            new JsonObject { ["title"] = "desk" }
        ).SetName("{m}(role-gated event target)");
        yield return new TestCaseData(
            PolicyWriteStartMachine.Id,
            nameof(ApprovalState.Approved),
            new JsonObject { ["approvedBy"] = "approver" }
        ).SetName("{m}(policy-gated event target)");
    }

    [TestCaseSource(nameof(ServerStates))]
    public async Task Discard_from_a_state_only_the_server_reaches_is_refused(
        string machine,
        string state,
        JsonObject context
    )
    {
        var owner = NewUser();
        var id = await Seed(owner, machine, state, context);

        var outcome = await Discard(machine, new Actor.Client(owner), id);

        outcome
            .Should()
            .BeOfType<DiscardOutcome.Rejected>()
            .Which.Code.Should()
            .Be(
                RefusalCode.DiscardNotAllowed,
                $"an instance the server moved is the record of what it did. See {Adr}"
            );
        (await _host.Rows(id)).Should().ContainSingle("the instance stays");
    }

    [Test]
    public async Task Discard_from_a_state_a_client_reaches_deletes()
    {
        var owner = NewUser();
        var id = await Seed(
            owner,
            RuledBuildMachine.Id,
            nameof(BuildState.Draft),
            new JsonObject { ["source"] = "repo" }
        );

        (await Discard(RuledBuildMachine.Id, new Actor.Client(owner), id))
            .Should()
            .BeOfType<DiscardOutcome.Discarded>();
        (await _host.Rows(id)).Should().BeEmpty();
    }

    [Test]
    public async Task Discard_after_an_operator_approved_the_instance_is_refused()
    {
        var owner = NewUser();
        var pending = await Seed(
            owner,
            OperatorApprovalMachine.Id,
            nameof(ApprovalState.Pending),
            new JsonObject { ["approvedBy"] = "" }
        );
        var approved = await Seed(
            owner,
            OperatorApprovalMachine.Id,
            nameof(ApprovalState.Pending),
            new JsonObject { ["approvedBy"] = "" }
        );
        using (var scope = _host.Scope())
            (
                await _host
                    .Service(scope, OperatorApprovalMachine.Id)
                    .Advance(
                        DeskRoles.Client(owner, DeskRoles.Operator),
                        approved,
                        nameof(ApprovalTrigger.Approve),
                        null,
                        "approve-1"
                    )
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        (await Discard(OperatorApprovalMachine.Id, new Actor.Client(owner), approved))
            .Should()
            .BeOfType<DiscardOutcome.Rejected>()
            .Which.Code.Should()
            .Be(
                RefusalCode.DiscardNotAllowed,
                $"an operator's approval is the record of a decision only the server's gate let through. See {Adr}"
            );
        (await _host.Rows(approved)).Should().ContainSingle("the approved instance stays");

        (await Discard(OperatorApprovalMachine.Id, new Actor.Client(owner), pending))
            .Should()
            .BeOfType<DiscardOutcome.Discarded>("a pending instance is still its owner's to drop");
        (await _host.Rows(pending)).Should().BeEmpty();
    }

    [Test]
    public async Task Discard_is_held_to_its_rule()
    {
        var owner = NewUser();
        var id = await Seed(
            owner,
            OperatorDiscardMachine.Id,
            nameof(TurnstileState.Locked),
            new JsonObject()
        );

        (await Discard(OperatorDiscardMachine.Id, DeskRoles.Client(owner), id))
            .Should()
            .BeOfType<DiscardOutcome.Rejected>()
            .Which.Code.Should()
            .Be(RefusalCode.Forbidden, $"the owner without the role may not discard. See {Adr}");
        (await _host.Rows(id)).Should().ContainSingle();

        (await Discard(OperatorDiscardMachine.Id, DeskRoles.Client(owner, DeskRoles.Operator), id))
            .Should()
            .BeOfType<DiscardOutcome.Discarded>();
        (await _host.Rows(id)).Should().BeEmpty();
    }

    [Test]
    public async Task A_machine_with_rules_but_no_discard_rule_refuses_discard()
    {
        var owner = NewUser();
        var id = await Seed(
            owner,
            UndeclaredDiscardMachine.Id,
            nameof(TurnstileState.Locked),
            new JsonObject()
        );

        (await Discard(UndeclaredDiscardMachine.Id, new Actor.Client(owner), id))
            .Should()
            .BeOfType<DiscardOutcome.Rejected>()
            .Which.Code.Should()
            .Be(
                RefusalCode.Forbidden,
                $"once a machine declares rules, an operation without one admits nobody. See {Adr}"
            );
        (await _host.Rows(id)).Should().ContainSingle();
    }
}
