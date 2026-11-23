using System.Globalization;
using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Trace;

/// <summary>What one line of a run's trace is.</summary>
internal enum TraceKind
{
    /// <summary>A step: a junction, a decision point or a route, as it started and ended.</summary>
    Step,

    /// <summary>A decision the run recorded: the question, the answer, who answered.</summary>
    Decision,

    /// <summary>A line the run logged.</summary>
    Log,
}
