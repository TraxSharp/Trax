using System.Text.Json.Nodes;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

// What the browser tests' host runs. Two trains are scheduled at startup in two dependent manifest groups (the
// groups page's dependency graph) and a system-owned machine has an instance (the state-machine pages); the rest
// is what the seed builders (Fixtures/Seed.cs) drive into the states a console page shows: a train that fails
// while its key is down, one that holds while its subject is held, one whose queue entry stays staged, one with
// a Gate and a checkpoint, one that never runs, and two machines, one invoking a train and one with a timer.
namespace Trax.Samples.ApiDashboard.Browser.Host;

/// <summary>
/// Started with a key in its context; <c>Send</c> enters <c>Relaying</c>, which invokes
/// <see cref="IRelayParcelTrain"/>: a run started by a machine.
/// </summary>
public sealed class RelayMachine : Machine<RelayState, RelayTrigger>
{
    public const string Id = "browser-relay";

    protected override void Configure(IMachineBuilder<RelayState, RelayTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(RelayState.Waiting, () => new JsonObject());
        m.In(RelayState.Waiting)
            .Context<RelayContext>()
            .On(RelayTrigger.Send)
            .To(RelayState.Relaying);
        m.In(RelayState.Relaying)
            .Context<RelayContext>()
            .Invokes<IRelayParcelTrain, RelayInput, Unit>(c => new RelayInput
            {
                Key = c["key"]?.GetValue<string>() ?? "",
            })
            .OnDone(RelayState.Relayed)
            .OnFailed(RelayState.Lost)
            .OnCancelled(RelayState.Lost);
        m.In(RelayState.Relayed).Context<RelayContext>();
        m.In(RelayState.Lost).Context<RelayContext>();
        m.SystemOwned();
    }
}
