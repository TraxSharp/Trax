using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.StateMachine;

/// <summary>
/// What a machine replayed from its IR declares about the machine as a whole beyond the public builder: its server-only
/// triggers.
/// </summary>
internal interface IIrMachineBuilder<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>
    /// Marks <paramref name="trigger"/> as fired by the server only (ADR 0058): not served, with no client
    /// rule. Build refuses one that also declares a rule.
    /// </summary>
    void ServerOnly(TTrigger trigger);
}
