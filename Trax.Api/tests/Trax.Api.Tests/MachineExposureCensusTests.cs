using AwesomeAssertions;
using Trax.Api.Tests.Fakes.Machines;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.Tests;

/// <summary>
/// The GraphQL exposure census covers machines as a kind of their own, beside trains
/// (<see cref="TrainExposureAuthorizationTests"/>) and query models: a marked machine whose event declares no
/// rule refuses the host, naming the machine and the event, and the machine census lists a timer as a trigger only
/// the server fires, guarded by the union of the rules of the events that enter its state.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class MachineExposureCensusTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public async Task The_GraphQL_exposure_census_covers_machines_as_their_own_kind()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(UndeclaredRuleMachine)]);

        refusal
            .Should()
            .NotBeNull(
                "a served machine's every event declares its rule, as an exposed train declares its "
                    + "posture, per "
                    + Adr
            );
        var message = refusal!.ToString();
        message.Should().Contain($"'{UndeclaredRuleMachine.MachineId}'").And.Contain("Back");
        message
            .Should()
            .ContainEquivalentOf(
                "machine",
                "the census names a machine as a machine, not as one of its internal trains"
            );
        message
            .Should()
            .NotContain(
                "Trax.Effect.StateMachine.Persistence.Mutations",
                "the internal snapshot trains are not what a client reaches"
            );
    }

    [Test]
    public void The_census_lists_the_timer_as_server_only_with_its_union_rule()
    {
        var entries = MachineCensus.Describe([new ShopMachine()]);

        var timer = entries
            .Where(e => e.Kind == nameof(TriggerKind.Timer))
            .Should()
            .ContainSingle("the shop declares one timer, on Ordered, per " + Adr)
            .Subject;

        timer
            .Surfaces.Should()
            .ContainSingle("only the server fires a timer")
            .Which.Should()
            .ContainEquivalentOf("server");
        timer
            .Rule.Should()
            .Contain(
                "Clerk",
                "Reopen (Clerk) enters Ordered, so the timer leaving it carries that rule"
            )
            .And.ContainEquivalentOf(
                "authenticated",
                "Submit and Ship (any authenticated caller) enter Ordered too"
            );
        entries
            .Where(e => e.Kind == nameof(TriggerKind.Timer))
            .Should()
            .OnlyContain(e => e.Machine == ShopMachine.MachineId);
    }
}
