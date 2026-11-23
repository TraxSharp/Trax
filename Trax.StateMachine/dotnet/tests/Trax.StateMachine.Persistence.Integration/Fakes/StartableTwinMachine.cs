using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary><see cref="StartableMachine"/> under another id, so one request id can start one of each.</summary>
public sealed class StartableTwinMachine : Machine<ListState, ListTrigger>
{
    public const string Id = "startable-twin";

    protected override void Configure(IMachineBuilder<ListState, ListTrigger> m) =>
        StartableMachine.Declare(m, Id);
}
