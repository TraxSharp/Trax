using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

internal sealed record GlossaryView(
    string Name,
    IReadOnlyList<ViewRead> Reads,
    IReadOnlyList<ViewField> Fields
);
