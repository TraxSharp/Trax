using System.Diagnostics.CodeAnalysis;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.StateMachine.Persistence;

/// <summary>A machine state that invokes a train, as the startup check describes it to the launcher.</summary>
/// <param name="Machine">The machine's id.</param>
/// <param name="State">The invoking state.</param>
/// <param name="TrainType">The train's interface, its canonical name.</param>
/// <param name="InputType">The train's input type, as the state declared it.</param>
/// <param name="OutputType">The train's output type, as the state declared it.</param>
/// <param name="SystemOwned">Whether the machine's instances belong to the system rather than to users.</param>
internal sealed record InvokedTrainDeclaration(
    string Machine,
    string State,
    Type TrainType,
    Type InputType,
    Type OutputType,
    bool SystemOwned
)
{
    /// <summary>
    /// Every way a caller enters the state: each event with an edge into it from another state, and the machine's
    /// start when the state is the initial one. Entering it runs the train, so a user-owned machine's startup check
    /// compares each one's rule with the train's <c>[TraxAuthorize]</c>.
    /// </summary>
    public IReadOnlyList<EnteringEvent> EnteringEvents { get; init; } = [];
}
