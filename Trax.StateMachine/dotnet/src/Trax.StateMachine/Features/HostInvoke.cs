using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// The train a state invokes and its input: each train field from a context field, or from <see cref="Owner"/>.
/// </summary>
internal sealed record HostInvoke(
    string State,
    string Train,
    IReadOnlyDictionary<string, string> Input
)
{
    /// <summary>
    /// The source that gives a train field the instance owner's qualified principal id (its user key). It is the
    /// server's to set: no context holds it, so no client can write it. A system machine has no owner.
    /// </summary>
    internal const string Owner = "$owner";
}
