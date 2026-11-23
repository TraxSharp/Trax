using System.Text.Json.Nodes;

namespace Trax.StateMachine.Features;

internal sealed record HostView(
    string Name,
    IReadOnlyList<ViewRead> Reads,
    IReadOnlyList<ViewField> Fields,
    AccessRule Readers
);
