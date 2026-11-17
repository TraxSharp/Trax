using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>A one-state machine whose only edge appends <c>input.text</c> to the text it already holds.</summary>
public sealed class LogMachine : Machine<LogState, LogTrigger>
{
    protected override void Configure(IMachineBuilder<LogState, LogTrigger> m)
    {
        m.Id("log").Version(1).StartsAt(LogState.Open, () => new JsonObject());
        m.In(LogState.Open)
            .On(LogTrigger.Append)
            .Reduce(
                (ctx, input) =>
                    new JsonObject
                    {
                        ["text"] =
                            (ctx["text"]?.GetValue<string>() ?? "")
                            + (input?["text"]?.GetValue<string>() ?? ""),
                    }
            )
            .To(LogState.Open);
    }
}
