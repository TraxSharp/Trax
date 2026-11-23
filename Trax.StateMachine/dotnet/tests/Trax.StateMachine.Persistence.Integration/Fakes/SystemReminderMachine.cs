using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The reminder as a system-owned machine, with no rule on <c>Open</c>.</summary>
public sealed class SystemReminderMachine : Machine<ReminderState, ReminderTrigger>
{
    public const string Id = "system-reminder";

    protected override void Configure(IMachineBuilder<ReminderState, ReminderTrigger> m)
    {
        ReminderMachine.Declare(m, Id, gated: false);
        m.SystemOwned();
    }
}
