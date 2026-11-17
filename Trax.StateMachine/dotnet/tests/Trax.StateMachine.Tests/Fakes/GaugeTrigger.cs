using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Tests.Fakes;

public enum GaugeTrigger
{
    Lower,
    Raise,
    Settle,
}
