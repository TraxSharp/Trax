using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A hatch with no rule, timer, invoked train, effect, guard or reducer: <c>Shut -Open-&gt; Open -Shut-&gt; Shut</c>.
/// The soft save serves it.
/// </summary>
public sealed class PlainHatchMachine : Machine<HatchState, HatchTrigger>
{
    public const string Id = "plain-hatch";

    protected override void Configure(IMachineBuilder<HatchState, HatchTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(HatchState.Shut, () => new JsonObject());
        m.In(HatchState.Shut).On(HatchTrigger.Open).To(HatchState.Open);
        m.In(HatchState.Open).On(HatchTrigger.Shut).To(HatchState.Shut);
    }

    public static string Json(HatchState state) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state.ToString(),
            ["context"] = new JsonObject(),
        }.ToJsonString();
}
