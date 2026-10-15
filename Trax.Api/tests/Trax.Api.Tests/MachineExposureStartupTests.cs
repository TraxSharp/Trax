using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Tests.Fakes.Machines;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.Tests;

/// <summary>
/// Only a declarative, user-owned machine whose every event and read declares a rule can be served through typed
/// operations. Marking and registering any other refuses startup, naming the machine: a raw-delegate
/// machine has nothing a typed surface or a version hash can be built from, a system-owned one has no client, a
/// delegate guard beside declared rules escapes the hash, <c>Anonymous()</c> has no owner to give a user's draft, and
/// an undeclared read rule would leave a read open.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class MachineExposureStartupTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public async Task Exposing_a_raw_delegate_machine_fails_startup_naming_it()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(RawDelegateMachine)]);

        refusal
            .Should()
            .NotBeNull(
                "a raw-delegate machine has no declared inputs to build its typed operations from, per "
                    + Adr
            );
        refusal!.ToString().Should().Contain($"'{RawDelegateMachine.MachineId}'");
    }

    [Test]
    public async Task Exposing_a_system_owned_machine_fails_startup_naming_it()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(SystemOwnedStepMachine)]);

        refusal.Should().NotBeNull("a system-owned machine's instances have no client, per " + Adr);
        refusal!.ToString().Should().Contain($"'{SystemOwnedStepMachine.MachineId}'");
    }

    [Test]
    public async Task Exposing_a_machine_mixing_delegate_guards_with_rules_fails_startup()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(MixedGuardMachine)]);

        refusal
            .Should()
            .NotBeNull(
                "the IR and its hash do not cover a delegate guard, so changing one would never make a "
                    + "client outdated, per "
                    + Adr
            );
        refusal!
            .ToString()
            .Should()
            .Contain($"'{MixedGuardMachine.MachineId}'")
            .And.Contain("Next");
    }

    [Test]
    public async Task Anonymous_on_a_user_owned_machine_fails_startup()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(AnonymousEventMachine)]);

        refusal
            .Should()
            .NotBeNull(
                "an anonymous caller has no user key to own a draft by, so Anonymous() on a user-owned "
                    + "machine refuses startup, per "
                    + Adr
            );
        refusal!
            .ToString()
            .Should()
            .Contain($"'{AnonymousEventMachine.MachineId}'")
            .And.Contain("Next");
    }

    [TestCase(typeof(NoReadRuleMachine), NoReadRuleMachine.MachineId, "get")]
    [TestCase(typeof(NoStartRuleMachine), NoStartRuleMachine.MachineId, "start")]
    public async Task An_undeclared_read_rule_on_get_or_start_fails_startup(
        Type machine,
        string id,
        string read
    )
    {
        var refusal = await MachineSchemaHost.RefusalAsync([machine]);

        refusal
            .Should()
            .NotBeNull(
                "every read declares its rule, owner-only being the floor, and an undeclared one refuses "
                    + "startup, per "
                    + Adr
            );
        refusal!.ToString().Should().Contain($"'{id}'").And.Contain(read);
    }

    [Test]
    public async Task Exposing_a_machine_without_persisted_operations_fails_startup_naming_the_fix()
    {
        var refusal = await MachineSchemaHost.RefusalAsync(
            [typeof(PartsMachine)],
            persistedOperations: false
        );

        refusal
            .Should()
            .NotBeNull(
                "a node orders the versions of an exposed machine through its persisted documents, so the typed "
                    + "surface refuses to start without them, per "
                    + Adr
            );
        refusal!
            .ToString()
            .Should()
            .Contain($"'{PartsMachine.MachineId}'")
            .And.Contain("UsePersistedOperations");
    }

    [Test]
    public async Task Exposing_a_machine_with_persisted_operations_starts()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(PartsMachine)]);

        refusal.Should().BeNull(refusal?.ToString());
    }

    [Test]
    public async Task A_machine_whose_push_field_collides_with_a_subscription_is_refused()
    {
        var refusal = await MachineSchemaHost.RefusalAsync([typeof(OnTrainStateMachine)]);

        refusal
            .Should()
            .NotBeNull(
                "the push of a machine 'on-train-state' is onTrainStateChanged, Trax's own subscription, per "
                    + Adr
            );
        refusal!
            .ToString()
            .Should()
            .Contain($"The machine '{OnTrainStateMachine.MachineId}'")
            .And.Contain("'onTrainStateChanged'")
            .And.Contain("Give the machine another id");
    }

    [Test]
    public async Task A_machine_whose_push_field_collides_with_a_host_subscription_is_refused()
    {
        var refusal = await MachineSchemaHost.RefusalAsync(
            [typeof(OrderStepMachine)],
            graphql => graphql.ConfigureSchema(s => s.AddTypeExtension<HostOrderChanged>())
        );

        refusal
            .Should()
            .NotBeNull(
                "the push of a machine 'order' is orderChanged, which the host has, per " + Adr
            );
        refusal!
            .ToString()
            .Should()
            .Contain($"The machine '{OrderStepMachine.MachineId}'")
            .And.Contain("'orderChanged'")
            .And.Contain(typeof(HostOrderChanged).FullName!);
    }

    /// <summary>A host subscription named like the push of a machine <c>order</c>.</summary>
    [ExtendObjectType("LifecycleSubscriptions")]
    public sealed class HostOrderChanged
    {
        [TraxAuthorize]
        [Subscribe]
        public string OrderChanged([EventMessage] string message) => message;
    }
}
