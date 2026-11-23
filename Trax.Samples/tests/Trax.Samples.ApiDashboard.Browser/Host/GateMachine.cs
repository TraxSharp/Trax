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

/// <summary>A gate the system owns; the host starts one instance of it.</summary>
public sealed class GateMachine : Machine<GateState, GateTrigger>
{
    public const string Id = "browser-gate";

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(GateState.Closed, () => new JsonObject());
        m.In(GateState.Closed).Context().On(GateTrigger.Open).To(GateState.Open);
        m.In(GateState.Open).Context().On(GateTrigger.Close).To(GateState.Closed);
        m.SystemOwned();
    }
}
