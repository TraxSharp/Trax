using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// A declarative machine whose state is checked by a delegate (<c>Holds</c>), which the IR cannot carry. Its IR
/// export is refused by design, so it has no schema hash.
/// </summary>
public sealed class UnexportableMachine : Machine<GateState, GateTrigger>
{
    public const string Id = "unexportable";

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(GateState.A, () => new JsonObject());
        m.In(GateState.A).Context().On(GateTrigger.Go).To(GateState.B);
        m.In(GateState.B).Holds(_ => null);
    }
}

/// <summary>
/// A declarative machine with one delegate guard beside its declared context: its IR export is refused by design,
/// so it has no schema hash.
/// </summary>
public sealed class MixedDelegateGuardMachine : Machine<GateState, GateTrigger>
{
    public const string Id = "mixed-delegate-guard";

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(GateState.A, () => new JsonObject());
        m.In(GateState.A)
            .Context()
            .On(GateTrigger.Go)
            .When((ctx, _) => ctx.Count == 0)
            .To(GateState.B);
        m.In(GateState.B).Context();
    }
}

/// <summary>
/// A fully declarative machine that should export, but whose initial context throws when the export reads it, when
/// made by <see cref="Broken"/>: a genuine failure to compute its hash, not a refusal by design. The assembly scan
/// registers it in every host built over this assembly, so the instance the scan makes is healthy.
/// </summary>
public sealed class BrokenExportMachine : Machine<GateState, GateTrigger>
{
    public const string Id = "broken-export";

    private bool _broken;

    /// <summary>An instance whose initial context throws.</summary>
    public static BrokenExportMachine Broken() => new() { _broken = true };

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(
                GateState.A,
                () =>
                    _broken
                        ? throw new InvalidOperationException("the initial context is unavailable")
                        : new JsonObject()
            );
        m.In(GateState.A).Context().On(GateTrigger.Go).To(GateState.B);
        m.In(GateState.B).Context();
    }
}

/// <summary>
/// Every write records the IR hash of the machine that made it, so a machine whose hash fails unexpectedly is
/// refused when the host starts, rather than failing each write it makes. A machine whose IR export is refused by
/// design (raw delegates, or rules mixed with a delegate) has no hash and starts: only serving it through typed
/// operations is refused. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class MachineSchemaStartupTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public void A_machine_mixing_delegates_with_rules_has_no_schema_hash_and_its_export_is_still_refused()
    {
        IMachine[] mixed = [new UnexportableMachine(), new MixedDelegateGuardMachine()];

        foreach (var machine in mixed)
        {
            machine
                .SchemaHash.Should()
                .BeNull(
                    "its IR export is refused by design, so it has no hash, as a raw-delegate one"
                );
            var export = () => machine.ExportIr();
            export
                .Should()
                .Throw<InvalidOperationException>("the IR still cannot carry a delegate")
                .WithMessage("*IR export refuses*");
        }
    }

    [Test]
    public void A_write_to_a_mixed_machine_records_no_ir_hash_rather_than_throwing()
    {
        var hashes = new MachineIrHashes([
            new UnexportableMachine(),
            new MixedDelegateGuardMachine(),
        ]);

        var unexportable = () => hashes.Of(UnexportableMachine.Id);
        var mixedGuard = () => hashes.Of(MixedDelegateGuardMachine.Id);

        unexportable.Should().NotThrow().Which.Should().BeNull();
        mixedGuard.Should().NotThrow().Which.Should().BeNull();
    }

    [Test]
    public async Task Machines_mixing_delegates_with_rules_start()
    {
        var refusal = await StartingHost.Start(
            _ => { },
            new UnexportableMachine(),
            new MixedDelegateGuardMachine()
        );

        refusal
            .Should()
            .BeNull(
                "a machine whose IR export is refused by design has no hash, which is not a failure; only "
                    + $"exposing it is refused ({Adr})"
            );
    }

    [Test]
    public void The_broken_machine_builds_but_its_schema_hash_throws()
    {
        var machine = BrokenExportMachine.Broken();

        machine.Name.Should().Be(BrokenExportMachine.Id, "the machine itself builds");
        var hash = () => machine.SchemaHash;
        hash.Should().Throw<InvalidOperationException>().WithMessage("*initial context*");
    }

    [Test]
    public async Task A_machine_whose_hash_fails_unexpectedly_refuses_the_host_start()
    {
        var refusal = await StartingHost.Start(_ => { }, BrokenExportMachine.Broken());

        refusal
            .Should()
            .BeOfType<InvalidOperationException>(
                $"every write records the machine's IR hash, so a machine whose hash throws fails at startup ({Adr})"
            )
            .Which.Message.Should()
            .Contain(typeof(BrokenExportMachine).FullName)
            .And.Contain("IR hash");
    }

    [Test]
    public async Task Declarative_and_raw_delegate_machines_start()
    {
        var refusal = await StartingHost.Start(
            _ => { },
            new SystemDeclarativeTurnstileMachine(),
            new TurnstileMachine()
        );

        refusal.Should().BeNull("a raw-delegate machine has no IR hash, which is not a failure");
    }
}
