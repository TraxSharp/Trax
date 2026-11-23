using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A machine whose only server-written feature is a timer: no rule, no invoke, no effect. The generic save is
/// refused for it because of the timer alone.
/// </summary>
public sealed class PlainTimerMachine : Machine<PlainTimerState, PlainTimerTrigger>
{
    public const string Id = "plain-timer";

    protected override void Configure(IMachineBuilder<PlainTimerState, PlainTimerTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(PlainTimerState.Idle, () => new JsonObject());

        m.In(PlainTimerState.Idle).Context().On(PlainTimerTrigger.Wait).To(PlainTimerState.Waiting);

        m.In(PlainTimerState.Waiting)
            .Context()
            .After(TimeSpan.FromMinutes(5))
            .GoTo(PlainTimerState.Idle)
            .On(PlainTimerTrigger.Stop)
            .To(PlainTimerState.Idle);
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
