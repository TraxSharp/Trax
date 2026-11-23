using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public enum OrderTrigger
{
    Next,
    Back,
    Place,
    Reset,
}
