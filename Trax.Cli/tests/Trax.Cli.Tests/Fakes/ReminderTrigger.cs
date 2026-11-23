using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Cli.Tests.Fakes;

public enum ReminderTrigger
{
    Open,
    Approve,
    Extend,
}
