using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

// The resubscribe backoff the three clients share (conformance/client/PROTOCOL.md): the step is the attempt's delay, the
// last repeating, and the wait is drawn uniformly between half the step and the whole of it, so the streams a host
// dropped together do not all come back together. A draw of 0 waits the whole step.
internal static class Backoff
{
    internal static TimeSpan Delay(
        IReadOnlyList<TimeSpan> steps,
        int attempt,
        Func<double> random
    ) => steps[Math.Min(attempt, steps.Count - 1)] * (1.0 - 0.5 * Math.Clamp(random(), 0.0, 1.0));
}
