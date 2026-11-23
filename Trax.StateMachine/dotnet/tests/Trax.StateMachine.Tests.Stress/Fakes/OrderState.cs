using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.StateMachine.Tests.Stress.Fakes;

public enum OrderState
{
    Draft,
    Review,
    Placed,
}
