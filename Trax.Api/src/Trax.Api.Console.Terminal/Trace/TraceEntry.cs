using System.Globalization;
using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Trace;

/// <summary>One line of a run's trace.</summary>
/// <param name="Kind">A step, a decision or a log line.</param>
/// <param name="Key">What makes it this line: a step's position, a decision's or log's id. A step that changes keeps its key.</param>
/// <param name="At">When it happened; null for a log line, which the host records without a time.</param>
/// <param name="Text">The line as the terminal shows it.</param>
/// <param name="Json">What the host answered for it.</param>
/// <param name="Ended">For a step, whether it had ended (completed, failed or cancelled) when it was read.</param>
internal sealed record TraceEntry(
    TraceKind Kind,
    string Key,
    DateTimeOffset? At,
    string Text,
    JsonNode? Json,
    bool Ended = false
)
{
    /// <summary>
    /// Of two readings of one line, the one to keep: a step that had ended is newer than one still running, and of two
    /// equally far along, <paramref name="later"/>, the one read last.
    /// </summary>
    public static TraceEntry Newest(TraceEntry earlier, TraceEntry later) =>
        earlier.Ended && !later.Ended ? earlier : later;
}
