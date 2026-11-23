using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>A user's machine.</summary>
public sealed class UserStepMachine : StepMachine
{
    public const string MachineId = "invoked-step-user";
    protected override string Id => MachineId;
}
