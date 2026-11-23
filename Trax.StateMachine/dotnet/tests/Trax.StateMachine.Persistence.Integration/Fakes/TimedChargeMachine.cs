using System.Text.Json.Nodes;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A user's machine whose timed <c>Review</c> is also left by an irreversible effect: the effect's target and the
/// timer race for the same row.
/// </summary>
public sealed class TimedChargeMachine : Machine<TimedChargeState, TimedChargeTrigger>
{
    public const string Id = "timed-charge";

    protected override void Configure(IMachineBuilder<TimedChargeState, TimedChargeTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(TimedChargeState.Review, () => new JsonObject());
        m.In(TimedChargeState.Review)
            .After(TimeSpan.FromMinutes(10))
            .GoTo(TimedChargeState.Expired)
            .On(TimedChargeTrigger.Charge)
            .RunsOnce<IOrderCharge>($"{Id}:charge")
            .Reduce((_, input) => new JsonObject { ["receipt"] = input?["receipt"]?.DeepClone() })
            .To(TimedChargeState.Charged);
        m.In(TimedChargeState.Charged);
        m.In(TimedChargeState.Expired);
    }

    public static string Json(string state) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = new JsonObject(),
        }.ToJsonString();
}
