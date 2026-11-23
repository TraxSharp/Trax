using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>A system-owned machine, whose instances start in <c>Running</c>.</summary>
public sealed class SystemStepMachine : StepMachine
{
    public const string MachineId = "invoked-step-system";
    protected override string Id => MachineId;
    protected override bool System => true;
}
