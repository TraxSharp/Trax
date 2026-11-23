using System.Text.Json.Nodes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>The context of a <see cref="WaitMachine"/>: the run's mode and note, and how long to wait for it.</summary>
public sealed record WaitContext
{
    public string Mode { get; init; } = "";

    public string Note { get; init; } = "";

    public string Artifact { get; init; } = "";

    public TimeSpan? Patience { get; init; }
}
