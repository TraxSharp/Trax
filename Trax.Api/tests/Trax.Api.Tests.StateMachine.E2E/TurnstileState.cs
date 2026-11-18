using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

public enum TurnstileState
{
    Locked,
    Unlocked,
}
