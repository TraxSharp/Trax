using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public enum ConsignmentState
{
    Open,
    Packing,
    Sent,
    Lost,
    Recalled,
    Abandoned,
}
