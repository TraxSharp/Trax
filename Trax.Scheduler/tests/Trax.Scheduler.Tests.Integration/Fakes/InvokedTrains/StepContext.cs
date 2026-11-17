using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>The context the steps read and the outcome reduces into.</summary>
public sealed record StepContext
{
    public string Mode { get; init; } = "";

    public string Note { get; init; } = "";

    public string Artifact { get; init; } = "";

    public string Pad { get; init; } = "";
}
