using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>A one-state machine whose only edge copies <c>input.text</c> into the context.</summary>
public sealed class NoteMachine : Machine<NoteState, NoteTrigger>
{
    protected override void Configure(IMachineBuilder<NoteState, NoteTrigger> m)
    {
        m.Id("note").Version(1).StartsAt(NoteState.Draft, () => new JsonObject());
        m.In(NoteState.Draft)
            .On(NoteTrigger.Write)
            .Reduce((_, input) => new JsonObject { ["text"] = input?["text"]?.DeepClone() })
            .To(NoteState.Draft);
    }
}
