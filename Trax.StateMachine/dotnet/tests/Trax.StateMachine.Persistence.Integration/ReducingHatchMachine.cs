using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>The hatch whose <c>Shut</c> edge writes the context, and which has no guard.</summary>
public sealed class ReducingHatchMachine : Machine<HatchState, HatchTrigger>
{
    public const string Id = "reducing-hatch";

    protected override void Configure(IMachineBuilder<HatchState, HatchTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(HatchState.Shut, () => new JsonObject());
        m.In(HatchState.Shut).On(HatchTrigger.Open).To(HatchState.Open);
        m.In(HatchState.Open)
            .On(HatchTrigger.Shut)
            .Reduce((_, _) => new JsonObject { ["shutBy"] = "server" })
            .To(HatchState.Shut);
    }
}
