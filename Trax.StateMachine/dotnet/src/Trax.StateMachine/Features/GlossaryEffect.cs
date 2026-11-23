using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// A trigger's run-once effect: the full name of its <c>ISnapshotEffect</c> interface (server-only, written to the host
/// file), the key prefix its claims are taken under (in the IR), and the receipt an in-memory run answers with.
/// </summary>
internal sealed record GlossaryEffect(string Type, string? KeyPrefix, string Example);
