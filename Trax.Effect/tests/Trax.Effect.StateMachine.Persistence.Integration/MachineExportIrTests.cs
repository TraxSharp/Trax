using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// <see cref="IMachine.ExportIr"/> is the in-process entry point the <c>trax machine</c> CLI calls to turn a
/// compiled machine into its neutral IR. These pin the bridge from a discoverable
/// <see cref="Machine{TState,TTrigger}"/> to <c>IrExporter.Export</c>: it must match a direct export of the
/// same definition byte-for-byte, work through the <see cref="IMachine"/> interface (the CLI's actual path),
/// stay canonical and deterministic, and fail with a clear message on a raw-delegate machine that has no
/// exportable data. The full IR golden itself lives in the core project's <c>IrExporterTests</c>; here we
/// only prove the base-class method is faithful to the exporter, so there is no golden to keep in two places.
/// </summary>
public class MachineExportIrTests
{
    /// <summary>Export the same turnstile the fake configures, straight through the exporter.</summary>
    private static string DirectExport()
    {
        var builder = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        DeclarativeTurnstileMachine.ConfigureTurnstile(builder);
        return IrExporter.Export(builder.Build());
    }

    [Test]
    public void ExportIr_matches_a_direct_exporter_call_on_the_same_definition()
    {
        new DeclarativeTurnstileMachine().ExportIr().Should().Be(DirectExport());
    }

    [Test]
    public void ExportIr_through_the_IMachine_interface_produces_the_same_ir()
    {
        // The CLI holds machines as IMachine (from assembly scan), so the interface method is the real path.
        IMachine machine = new DeclarativeTurnstileMachine();
        machine.ExportIr().Should().Be(DirectExport());
    }

    [Test]
    public void ExportIr_is_canonical_single_line_and_deterministic()
    {
        var machine = new DeclarativeTurnstileMachine();
        var first = machine.ExportIr();

        // Deterministic: the same machine exports byte-identically every call (a stable golden/drift source).
        first.Should().Be(new DeclarativeTurnstileMachine().ExportIr());
        // Canonical single-line JSON: the CLI writes this verbatim (+ a trailing newline) as the .ir.json.
        first.Should().NotContain("\n");
        first.Should().StartWith("{").And.EndWith("}");
    }

    [Test]
    public void ExportIr_carries_the_declarative_data_a_generator_needs()
    {
        // A legible structural check so a regression in the bridge points at what broke, not just "bytes
        // differ": identity, the per-state context schema, and the guard-as-data all survive the round trip.
        var ir = (JsonObject)JsonNode.Parse(new DeclarativeTurnstileMachine().ExportIr())!;

        ir["id"]!.GetValue<string>().Should().Be("declarative-turnstile");
        ((JsonObject)ir["context"]!["Unlocked"]!)["fields"]!.AsArray().Single()!["name"]!
            .GetValue<string>()
            .Should()
            .Be("paidWith");
        var coin = ir["transitions"]!
            .AsArray()
            .Select(t => (JsonObject)t!)
            .Single(t => t["trigger"]!.GetValue<string>() == "Coin");
        ((JsonObject)coin["guard"]!)["rule"]!.GetValue<string>().Should().Be("oneOf");
    }

    [Test]
    public void ExportIr_on_a_raw_delegate_machine_throws_a_clear_error()
    {
        // TurnstileMachine is authored with raw Func<> delegates: guards/reducers are opaque closures with no
        // exportable data, so the exporter must refuse rather than emit a lossy IR. The message names the fix.
        var export = () => new TurnstileMachine().ExportIr();

        export.Should().Throw<InvalidOperationException>().WithMessage("*declaratively-authored*");
    }
}
