using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// Who a client rule admits: every role set met by one of its roles, or, with no sets, any signed-in caller. The
/// same reading as <see cref="CallerRule"/>'s role sets.
/// </summary>
internal sealed record AccessRule(IReadOnlyList<IReadOnlyList<string>> RoleSets)
{
    internal static readonly AccessRule AnyAuthenticated = new([]);

    internal bool IsAnyAuthenticated => RoleSets.Count == 0;
}
