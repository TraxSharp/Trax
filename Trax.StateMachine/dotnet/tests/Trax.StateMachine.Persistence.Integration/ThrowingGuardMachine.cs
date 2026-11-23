using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Testing;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>A machine whose only guard throws, carrying text that must stay on the server.</summary>
public sealed class ThrowingGuardMachine : Machine<ProbeState, ProbeTrigger>
{
    public const string Secret = "Host=db.internal;Password=hunter2";

    protected override void Configure(IMachineBuilder<ProbeState, ProbeTrigger> m)
    {
        m.Id("throwing-guard").Version(1).StartsAt(ProbeState.Open, () => new JsonObject());
        m.In(ProbeState.Open)
            .On(ProbeTrigger.Close)
            .When((_, _) => throw new InvalidOperationException(Secret))
            .To(ProbeState.Closed);
    }
}
