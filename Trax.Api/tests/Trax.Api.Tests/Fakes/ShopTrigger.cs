using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

public enum ShopTrigger
{
    Submit,
    Ship,
    Approve,
    Reopen,
    Restart,
}
