using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A machine lists every trigger it declares with its kind (who may fire it) and its input type, and the IR keeps
/// the inputs a client sends apart from the outputs an invoked train hands back.
/// </summary>
[Property("adr", TriggerInputValidationTests.Adr)]
public class TriggerMetadataTests
{
    private const string Adr = TriggerInputValidationTests.Adr;

    [Test]
    public void An_effect_bound_trigger_is_listed_as_effect_bound()
    {
        IMachine checkout = FluentCheckout.AsMachine();

        checkout
            .Triggers.Should()
            .ContainSingle(t => t.Name == nameof(CheckoutTrigger.Pay))
            .Which.Kind.Should()
            .Be(
                TriggerKind.EffectBound,
                $"Pay runs the checkout's irreversible effect, so only the effect runner fires it ({Adr})"
            );
    }

    [Test]
    public void A_raw_delegate_machine_lists_its_triggers_without_input_types()
    {
        IMachine checkout = FluentCheckout.AsMachine();

        checkout
            .Triggers.Should()
            .BeEquivalentTo(
                [
                    new MachineTrigger(nameof(CheckoutTrigger.Back), TriggerKind.Client, null),
                    new MachineTrigger(nameof(CheckoutTrigger.Next), TriggerKind.Client, null),
                    new MachineTrigger(nameof(CheckoutTrigger.Pay), TriggerKind.EffectBound, null),
                    new MachineTrigger(nameof(CheckoutTrigger.Restart), TriggerKind.Client, null),
                ],
                $"a delegate machine declares no input records, so no trigger has an input type ({Adr})"
            );
    }

    [Test]
    public void The_IR_separates_client_inputs_from_outcome_inputs()
    {
        var ir = (JsonObject)JsonNode.Parse(IrExporter.Export(IngestMachine.Built))!;

        ((JsonObject)ir["inputs"]!)
            .Select(kv => kv.Key)
            .Should()
            .NotContain(
                key => key.Contains('.'),
                $"inputs holds only what a client may send; an outcome's output is not one ({Adr})"
            );

        // The success outcome's output schema is still exported, under its outcome.
        var done = (JsonObject)ir["outcomes"]!["Fetching.done"]!;
        Descendants(done)
            .OfType<JsonObject>()
            .Where(o => o["fields"] is JsonArray)
            .Select(o => o["fields"]!.AsArray().Select(f => f!["name"]!.GetValue<string>()))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Equal("fingerprint", "unsure");
    }

    private static IEnumerable<JsonNode> Descendants(JsonNode node)
    {
        yield return node;
        var children = node switch
        {
            JsonObject o => o.Select(kv => kv.Value),
            JsonArray a => a.AsEnumerable(),
            _ => [],
        };
        foreach (var child in children.OfType<JsonNode>())
        foreach (var d in Descendants(child))
            yield return d;
    }
}
