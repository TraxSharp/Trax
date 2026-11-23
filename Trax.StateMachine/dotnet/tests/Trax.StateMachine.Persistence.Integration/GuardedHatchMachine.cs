using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>The hatch with one guard on <c>Open</c> and nothing else the server does.</summary>
public sealed class GuardedHatchMachine : Machine<HatchState, HatchTrigger>
{
    public const string Id = "guarded-hatch";

    protected override void Configure(IMachineBuilder<HatchState, HatchTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(HatchState.Shut, () => new JsonObject());
        m.In(HatchState.Shut)
            .On(HatchTrigger.Open)
            .When(
                (_, input) => input?["key"]?.GetValueKind() == System.Text.Json.JsonValueKind.String
            )
            .To(HatchState.Open);
        m.In(HatchState.Open).On(HatchTrigger.Shut).To(HatchState.Shut);
    }
}
