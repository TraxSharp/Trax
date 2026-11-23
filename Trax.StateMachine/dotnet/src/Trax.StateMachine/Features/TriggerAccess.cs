using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>A trigger's access: a client rule, or the server alone (a timer or a policy fires it).</summary>
internal sealed record TriggerAccess(AccessRule? Client)
{
    internal static readonly TriggerAccess Server = new((AccessRule?)null);

    internal bool IsServer => Client is null;
}
