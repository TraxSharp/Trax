using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A machine with nothing the server does: no guard, no reducer, no gated rule and a start that reads no input. The
/// generic save serves only a machine like this, so it is the one the tests of a successful save use.
/// </summary>
public sealed class PlainHatchMachine : Machine<HatchState, HatchTrigger>
{
    public const string MachineId = "plain-hatch";

    protected override void Configure(IMachineBuilder<HatchState, HatchTrigger> m)
    {
        // Every caller rule is the ungated AnyAuthenticated or OwnerOnly, which the save does not step around.
        m.Id(MachineId)
            .Version(1)
            .StartsAt(HatchState.Shut, () => new JsonObject())
            .Start<EmptyStart>(s => s.Reduce(Rules.Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());
        m.In(HatchState.Shut).On(HatchTrigger.Open).AnyAuthenticated().To(HatchState.Open);
        m.In(HatchState.Open).On(HatchTrigger.Shut).AnyAuthenticated().To(HatchState.Shut);
    }

    /// <summary>A snapshot at <paramref name="state"/> as canonical JSON, with <paramref name="context"/>.</summary>
    public static string Json(HatchState state, JsonObject? context = null) =>
        new JsonObject
        {
            ["machine"] = MachineId,
            ["version"] = 1,
            ["state"] = state.ToString(),
            ["context"] = context ?? new JsonObject(),
        }.ToJsonString();
}
