using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>A user's machine whose initial state is timed: a started instance closes itself after an hour.</summary>
public sealed class ExpiringMachine : Machine<ExpiringState, ExpiringTrigger>
{
    public const string Id = "expiring";

    public static readonly TimeSpan OpenFor = TimeSpan.FromHours(1);

    protected override void Configure(IMachineBuilder<ExpiringState, ExpiringTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ExpiringState.Open, () => new JsonObject())
            .Start<ExpiringStart>(s => s.AnyAuthenticated());

        m.In(ExpiringState.Open)
            .Context()
            .After(OpenFor)
            .GoTo(ExpiringState.Closed)
            .On(ExpiringTrigger.Close)
            .AnyAuthenticated()
            .To(ExpiringState.Closed);

        m.In(ExpiringState.Closed).Context();
    }
}
