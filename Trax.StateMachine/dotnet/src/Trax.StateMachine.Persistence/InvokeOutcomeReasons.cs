using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// The typed reasons an invoked run's outcome is applied as a failure, or not applied at all. Each is logged with
/// the machine, the instance and the state, never with the run's output or the instance's context.
/// </summary>
internal static class InvokeOutcomeReasons
{
    /// <summary>The run's output, or the snapshot its reduction produced, is past the 64 KiB snapshot cap.</summary>
    public const string TooLarge = "invoke-outcome-too-large";

    /// <summary>The run completed, but no <c>OnDone</c> edge's guard accepts its output.</summary>
    public const string OutputUnaccepted = "invoke-output-unaccepted";

    /// <summary>The run completed, but its output could not be recorded for the machine (it could not be serialized).</summary>
    public const string OutputUnrecorded = "invoke-output-unrecorded";

    /// <summary>The chosen edge's reduction produced a context its target refuses, or threw.</summary>
    public const string OutcomeRejected = "invoke-outcome-rejected";

    /// <summary>The target invokes a train of its own, and that run could not be queued.</summary>
    public const string NextRunRefused = "invoke-next-run-refused";

    /// <summary>
    /// The run can no longer be found: neither its work queue entry nor its metadata row exists, so something
    /// (metadata retention, an operator) deleted them before its outcome was delivered. How it ended is unknown.
    /// </summary>
    public const string RunMissing = "invoke-run-missing";
}
