using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A user's machine with a timed invoking state: <c>Fetching</c> invokes <see cref="IWaitTrain"/> and leaves for
/// <c>TimedOut</c> once it has been held for the context's <c>Patience</c> (between one minute and one hour). A
/// failed run lands in the timed <c>Cooldown</c>; a cancelled one in the untimed <c>Stopped</c>.
/// </summary>
public sealed class WaitMachine : Machine<WaitState, WaitTrigger>
{
    public const string Id = "wait";

    public static readonly TimeSpan CooldownFor = TimeSpan.FromMinutes(5);

    public sealed record WaitContext
    {
        public string Source { get; init; } = "";

        public TimeSpan? Patience { get; init; }
    }

    protected override void Configure(IMachineBuilder<WaitState, WaitTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(WaitState.Idle, () => Context(TimeSpan.FromMinutes(5)));

        m.In(WaitState.Idle).Context<WaitContext>().On(WaitTrigger.Fetch).To(WaitState.Fetching);

        m.In(WaitState.Fetching)
            .Context<WaitContext>()
            .Invokes<IWaitTrain, WaitInput, WaitOutput>(ctx => new WaitInput(
                ctx["source"]!.GetValue<string>()
            ))
            .OnDone(WaitState.Fetched)
            .OnFailed(WaitState.Cooldown)
            .OnCancelled(WaitState.Stopped)
            .After<WaitContext>(c => c.Patience, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1))
            .GoTo(WaitState.TimedOut)
            .On(WaitTrigger.Abandon)
            .To(WaitState.Idle);

        m.In(WaitState.Cooldown).Context<WaitContext>().After(CooldownFor).GoTo(WaitState.Idle);
        m.In(WaitState.Fetched).Context<WaitContext>();
        m.In(WaitState.Stopped).Context<WaitContext>();
        m.In(WaitState.TimedOut).Context<WaitContext>();
    }

    /// <summary>The context with <paramref name="patience"/> as its duration, written as ISO-8601.</summary>
    public static JsonObject Context(TimeSpan patience) =>
        new() { ["source"] = "repo", ["patience"] = System.Xml.XmlConvert.ToString(patience) };

    public static string Json(string state, TimeSpan patience) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = Context(patience),
        }.ToJsonString();
}
