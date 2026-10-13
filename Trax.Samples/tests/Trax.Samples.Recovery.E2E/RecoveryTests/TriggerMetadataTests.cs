using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Machines;
using Trax.Samples.Recovery.Trains.Topics;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The topic map's triggers as <see cref="IMachine.Triggers"/> lists them: who may fire each one, and the
/// record it takes. The GraphQL module builds the machine's typed operations from this list, so a client
/// trigger with the wrong input type is a mutation with the wrong argument.
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TriggerMetadataTests
{
    [Test]
    public void The_topic_map_lists_each_trigger_with_its_kind_and_input_type()
    {
        IMachine machine = new TopicMapMachine();

        var triggers = machine.Triggers;

        triggers
            .Should()
            .BeEquivalentTo(
                new MachineTrigger[]
                {
                    new(
                        nameof(TopicMapTrigger.ChooseFields),
                        TriggerKind.Client,
                        typeof(FieldsChoice)
                    ),
                    new(nameof(TopicMapTrigger.Build), TriggerKind.Client, typeof(RangeChoice)),
                    new(nameof(TopicMapTrigger.Back), TriggerKind.Client, null),
                    new(nameof(TopicMapTrigger.CancelBuild), TriggerKind.Client, null),
                    new(nameof(TopicMapTrigger.Rebuild), TriggerKind.Client, null),
                    new(nameof(TopicMapTrigger.Edit), TriggerKind.Client, null),
                    new(nameof(TopicMapTrigger.StartOver), TriggerKind.Client, null),
                    new("Building.done", TriggerKind.Outcome, typeof(TopicMap)),
                    new("Building.failed", TriggerKind.Outcome, null),
                    new("Building.cancelled", TriggerKind.Outcome, null),
                    new("Built.after", TriggerKind.Timer, null),
                },
                "the typed surface is built from each trigger's kind and input record "
                    + "(Trax.Docs/adr/0048), and only client triggers become mutations"
            );
    }
}
