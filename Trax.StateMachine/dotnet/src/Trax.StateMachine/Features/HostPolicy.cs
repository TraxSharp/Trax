using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

/// <summary>
/// The server acting when <see cref="When"/> enters <see cref="Enters"/>: on <see cref="Target"/> by
/// <see cref="By"/>, which is the link field (linked), the state its instances wait in (broadcast) or the system key
/// (toSystem).
/// </summary>
internal sealed record HostPolicy(
    string Shape,
    string When,
    string Enters,
    string Target,
    string Trigger,
    string By
)
{
    /// <summary>The policy's identity: what it waits for and what it fires, <c>shop.Open&gt;order.Bake</c>.</summary>
    internal string Id => $"{When}.{Enters}>{Target}.{Trigger}";
}
