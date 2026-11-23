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

/// <summary>What <see cref="RelayMachine"/>'s run is handed: the key it holds on (<see cref="Holds"/>).</summary>
public record RelayInput
{
    public string Key { get; init; } = "";
}
